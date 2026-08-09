using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using SecureOverlay.Platform.Windows;

namespace SecureOverlay
{
    public sealed class LiveRequestTrace : IDisposable
    {
        private static readonly AsyncLocal<LiveRequestTrace?> CurrentTrace = new();
        private static readonly Channel<string> Lines = Channel.CreateUnbounded<string>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        private static readonly Task Writer = Task.Run(WriteLoopAsync);
        private readonly long _startedAt = Stopwatch.GetTimestamp();
        private readonly ConcurrentDictionary<string, byte> _milestones = new(StringComparer.Ordinal);
        private bool _disposed;
        private int _retryCount;

        private LiveRequestTrace(string provider, string model, bool isVoice, bool hasImage)
        {
            CorrelationId = Guid.NewGuid().ToString("N");
            Provider = provider;
            Model = model;
            IsVoice = isVoice;
            HasImage = hasImage;
            CurrentTrace.Value = this;
            Mark("send_clicked");
        }

        public static LiveRequestTrace? Current => CurrentTrace.Value;
        public string CorrelationId { get; }
        public string Provider { get; }
        public string Model { get; }
        public bool IsVoice { get; }
        public bool HasImage { get; }
        public string Route { get; private set; } = "unknown";
        public bool UsedRetrieval { get; private set; }
        public int InputTokenEstimate { get; private set; }
        public int RetryCount => _retryCount;

        public static LiveRequestTrace Begin(string provider, string model, bool isVoice, bool hasImage)
            => new(provider, model, isVoice, hasImage);

        public void SetContext(string route, bool usedRetrieval, int inputTokenEstimate)
        {
            Route = string.IsNullOrWhiteSpace(route) ? "unknown" : route;
            UsedRetrieval = usedRetrieval;
            InputTokenEstimate = Math.Max(0, inputTokenEstimate);
            Mark("context_ready");
        }

        public void MarkRetry() => Interlocked.Increment(ref _retryCount);

        public bool Mark(string milestone)
        {
            if (!_milestones.TryAdd(milestone, 0))
            {
                return false;
            }

            Write(new
            {
                correlation_id = CorrelationId,
                milestone,
                elapsed_ms = Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds,
                route = Route,
                provider = Provider,
                model = Model,
                input = IsVoice ? "voice" : "typed",
                has_image = HasImage,
                retrieval = UsedRetrieval,
                input_token_estimate = InputTokenEstimate,
                retry_count = RetryCount
            });
            return true;
        }

        public void Complete(int outputLength, string outcome)
        {
            Write(new
            {
                correlation_id = CorrelationId,
                milestone = "response_completed",
                elapsed_ms = Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds,
                route = Route,
                provider = Provider,
                model = Model,
                input = IsVoice ? "voice" : "typed",
                has_image = HasImage,
                retrieval = UsedRetrieval,
                input_token_estimate = InputTokenEstimate,
                output_length = Math.Max(0, outputLength),
                retry_count = RetryCount,
                outcome
            });
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (ReferenceEquals(CurrentTrace.Value, this))
            {
                CurrentTrace.Value = null;
            }
        }

        private static void Write(object payload) => Lines.Writer.TryWrite(JsonSerializer.Serialize(payload));

        private static async Task WriteLoopAsync()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(WindowsAppPaths.PerformanceLogPath)!);
                await using var writer = new StreamWriter(WindowsAppPaths.PerformanceLogPath, append: true) { AutoFlush = true };
                await foreach (var line in Lines.Reader.ReadAllAsync())
                {
                    await writer.WriteLineAsync(line);
                }
            }
            catch
            {
            }
        }
    }
}
