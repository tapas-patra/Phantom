using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using SecureOverlay.Domain;
using SecureOverlay.Platform.Windows;

namespace SecureOverlay
{
    public sealed class LiveRequestTrace : IDisposable
    {
        private const long MaxFileBytes = 5 * 1024 * 1024;
        private const int MaxFiles = 5;
        private static readonly string ProcessSessionId = Guid.NewGuid().ToString("N");
        private static readonly AsyncLocal<LiveRequestTrace?> CurrentTrace = new();
        private static readonly Channel<string> Lines = Channel.CreateBounded<string>(new BoundedChannelOptions(2048)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        private static readonly Task Writer = Task.Run(WriteLoopAsync);
        private static long _droppedLineCount;

        private readonly long _startedAt = Stopwatch.GetTimestamp();
        private readonly ConcurrentDictionary<string, byte> _milestones = new(StringComparer.Ordinal);
        private bool _disposed;
        private int _retryCount;
        private int _terminalWritten;
        private string _operationId = string.Empty;
        private string _stage = "dispatch";
        private int _modelCall;

        private LiveRequestTrace(string provider, string model, bool isVoice, bool hasImage, string mode, string deliveryStyle, string executionLane, string usageSource)
        {
            TurnId = Guid.NewGuid().ToString("N");
            Provider = provider;
            Model = model;
            IsVoice = isVoice;
            HasImage = hasImage;
            Mode = mode;
            DeliveryStyle = deliveryStyle;
            ExecutionLane = executionLane;
            UsageSource = usageSource;
            CurrentTrace.Value = this;
            Mark("session_started");
            Mark("request_dispatched");
        }

        public static LiveRequestTrace? Current => CurrentTrace.Value;
        public static long DroppedLineCount => Interlocked.Read(ref _droppedLineCount);
        public string SessionId => ProcessSessionId;
        public string TurnId { get; }
        public string CorrelationId => TurnId;
        public string Provider { get; private set; }
        public string Model { get; private set; }
        public bool IsVoice { get; }
        public bool HasImage { get; }
        public string Mode { get; }
        public string DeliveryStyle { get; }
        public string ExecutionLane { get; private set; }
        public string UsageSource { get; private set; }
        public string Route { get; private set; } = "unknown";
        public bool UsedRetrieval { get; private set; }
        public int InputTokenEstimate { get; private set; }
        public int RetryCount => _retryCount;
        public string OperationId => _operationId;
        public double ElapsedMilliseconds => Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds;

        public static LiveRequestTrace Begin(string provider, string model, bool isVoice, bool hasImage, string mode = "interview", string deliveryStyle = "standard", string executionLane = "managed", string usageSource = "premium_managed")
            => new(provider, model, isVoice, hasImage, mode, deliveryStyle, executionLane, usageSource);

        public void SwitchLane(string executionLane, string usageSource)
        {
            ExecutionLane = executionLane;
            UsageSource = usageSource;
            Write("execution_lane_changed", "fallback");
        }

        public void SetContext(string route, bool usedRetrieval, int inputTokenEstimate)
        {
            Route = string.IsNullOrWhiteSpace(route) ? "unknown" : route;
            UsedRetrieval = usedRetrieval;
            InputTokenEstimate = Math.Max(0, inputTokenEstimate);
            Mark("context_assembly_completed");
        }

        public void StartOperation(string stage, int modelCall)
        {
            _operationId = Guid.NewGuid().ToString("N");
            _stage = stage;
            _modelCall = Math.Max(0, modelCall);
            Mark(modelCall > 0 ? "model_call_started" : "retrieval_started", uniquePerOperation: true);
        }

        public void CompleteOperation(string eventName, string outcome, string? errorCode = null, int snippetCount = 0)
            => Write(eventName, outcome, errorCode, snippetCount);

        public void SetDecision(LiveTurnDecision decision, int modelCallCount, string retrievalStatus)
        {
            Route = decision.Action.ToString().ToLowerInvariant();
            UsedRetrieval = decision.Action == LiveCopilotAction.Retrieve;
            Write("control_frame_parsed", "success",
                questionType: decision.QuestionType,
                intent: decision.Intent,
                action: Route,
                answerBasis: decision.AnswerBasis,
                confidenceBucket: decision.Confidence < .5 ? "low" : decision.Confidence < .8 ? "medium" : "high",
                entityType: decision.EntityType,
                hasEntityId: !string.IsNullOrEmpty(decision.EntityId),
                protocolVersion: decision.ProtocolVersion,
                modelCallCount: modelCallCount,
                retrievalStatus: retrievalStatus);
        }

        public void MarkRetry(string errorCode = "provider_error")
        {
            Interlocked.Increment(ref _retryCount);
            Write("provider_retry_started", "retry", errorCode);
        }

        public void RotateProvider(string provider, string model)
        {
            Provider = string.IsNullOrWhiteSpace(provider) ? Provider : provider;
            Model = string.IsNullOrWhiteSpace(model) ? Model : model;
            Mark("provider_rotated", uniquePerOperation: true);
        }

        public void RejectControl(string errorCode)
            => Write("control_frame_rejected", "rejected", errorCode);

        public bool Mark(string eventName, bool uniquePerOperation = false)
        {
            var key = uniquePerOperation ? $"{_operationId}:{eventName}" : eventName;
            if (!_milestones.TryAdd(key, 0)) return false;
            Write(eventName, "success");
            return true;
        }

        public void Fail(string eventName, string errorCode)
        {
            if (Interlocked.Exchange(ref _terminalWritten, 1) != 0) return;
            Write(eventName, "error", errorCode);
        }

        public void Complete(int outputLength, string outcome)
        {
            if (Interlocked.Exchange(ref _terminalWritten, 1) != 0) return;
            Write(outcome == "cancelled" ? "turn_cancelled" : outcome == "error" ? "turn_failed" : "answer_completed", outcome,
                bufferedCharacters: Math.Max(0, outputLength));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Write("session_ended", "success");
            if (ReferenceEquals(CurrentTrace.Value, this)) CurrentTrace.Value = null;
        }

        private void Write(
            string eventName, string outcome, string? errorCode = null, int snippetCount = 0,
            string? questionType = null, string? intent = null, string? action = null,
            string? answerBasis = null, string? confidenceBucket = null, string? entityType = null,
            bool? hasEntityId = null, int? protocolVersion = null, int? modelCallCount = null,
            string? retrievalStatus = null, int? bufferedCharacters = null)
        {
            var envelope = new
            {
                timestamp_utc = DateTime.UtcNow,
                level = outcome == "error" ? "Error" : outcome is "retry" or "rejected" ? "Warning" : "Information",
                service = "phantom-windows-desktop",
                component = "live_copilot",
                @event = eventName,
                session_id = SessionId,
                turn_id = TurnId,
                operation_id = _operationId,
                mode = Mode,
                delivery_style = DeliveryStyle,
                execution_lane = ExecutionLane,
                usage_source = UsageSource,
                stage = _stage,
                provider = Provider,
                model = Model,
                model_call = _modelCall,
                attempt = RetryCount + 1,
                elapsed_ms = Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds,
                outcome,
                error_code = errorCode,
                input_type = IsVoice ? "voice" : "typed",
                image_present = HasImage,
                estimated_input_tokens = InputTokenEstimate,
                snippet_count = Math.Max(0, snippetCount),
                question_type = questionType,
                intent,
                action,
                answer_basis = answerBasis,
                confidence_bucket = confidenceBucket,
                entity_type = entityType,
                has_entity_id = hasEntityId,
                protocol_version = protocolVersion,
                model_call_count = modelCallCount,
                retrieval_status = retrievalStatus,
                buffered_characters = bufferedCharacters
            };
            if (!Lines.Writer.TryWrite(JsonSerializer.Serialize(envelope)))
                Interlocked.Increment(ref _droppedLineCount);
        }

        private static async Task WriteLoopAsync()
        {
            try
            {
                var path = WindowsAppPaths.PerformanceLogPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await foreach (var line in Lines.Reader.ReadAllAsync())
                {
                    RotateIfNeeded(path);
                    await File.AppendAllTextAsync(path, line + Environment.NewLine).ConfigureAwait(false);
                }
            }
            catch
            {
                // Diagnostic writes are deliberately non-blocking and never recurse into logging.
            }
        }

        private static void RotateIfNeeded(string path)
        {
            if (!File.Exists(path) || new FileInfo(path).Length < MaxFileBytes) return;
            var oldest = $"{path}.{MaxFiles - 1}";
            if (File.Exists(oldest)) File.Delete(oldest);
            for (var index = MaxFiles - 2; index >= 1; index--)
            {
                var source = $"{path}.{index}";
                if (File.Exists(source)) File.Move(source, $"{path}.{index + 1}", true);
            }
            File.Move(path, $"{path}.1", true);
        }
    }
}
