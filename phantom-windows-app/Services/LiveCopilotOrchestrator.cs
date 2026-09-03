using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SecureOverlay.Domain;
using SecureOverlay.Domain.Entities;

namespace SecureOverlay.Services
{
    public sealed class LiveCopilotOrchestrator
    {
        public delegate Task<(string Response, string Error)> ModelStream(
            Action<string> onChunk,
            Action onRetryCleanup,
            CancellationToken cancellationToken);

        public async Task<LiveCopilotResult> ExecuteAsync(
            IEnumerable<string> allowedEntityIds,
            IEnumerable<string> allowedDocumentIds,
            ModelStream firstModel,
            Func<LiveTurnDecision, CancellationToken, Task<LiveCopilotRetrieval>> retrieve,
            Func<LiveTurnDecision, LiveCopilotRetrieval, ModelStream> secondModel,
            Action<string> publish,
            Action? resetPublishedAttempt,
            CancellationToken cancellationToken,
            Action<string>? protocolRejected = null)
        {
            var modelCalls = 0;
            var protocolRetries = 0;
            PhantomControlFrameParser parser;
            string firstResponse;

            while (true)
            {
                parser = new PhantomControlFrameParser(allowedEntityIds, allowedDocumentIds);
                modelCalls++;
                try
                {
                    var first = await firstModel(
                        chunk =>
                        {
                            var visible = parser.Feed(chunk);
                            if (visible.Length > 0) publish(visible);
                        },
                        () =>
                        {
                            parser = new PhantomControlFrameParser(allowedEntityIds, allowedDocumentIds);
                            resetPublishedAttempt?.Invoke();
                        },
                        cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(first.Error)) throw new InvalidOperationException(first.Error);
                    if (!parser.HasReceivedChunks && first.Response.Length > 0)
                    {
                        var visible = parser.Feed(first.Response);
                        if (visible.Length > 0) publish(visible);
                    }
                    parser.Complete();
                    firstResponse = first.Response;
                    break;
                }
                catch (PhantomProtocolException error) when (protocolRetries == 0)
                {
                    protocolRetries++;
                    protocolRejected?.Invoke(error.Code);
                    resetPublishedAttempt?.Invoke();
                }
            }

            var decision = parser.Decision!;
            if (protocolRetries > 0 && decision.Action == LiveCopilotAction.Retrieve)
                throw new PhantomProtocolException("control_repair_retrieve_invalid");
            if (decision.Action != LiveCopilotAction.Retrieve)
            {
                var answer = ExtractBody(firstResponse);
                if (!IsCompleteAnswer(answer)) throw new InvalidOperationException("The AI provider returned an incomplete response.");
                return new LiveCopilotResult(
                    answer, decision, modelCalls, protocolRetries, "not_requested", Array.Empty<RetrievedContextSnippet>());
            }

            var retrieval = await retrieve(decision, cancellationToken).ConfigureAwait(false);
            var finalResponse = string.Empty;
            var second = secondModel(decision, retrieval);
            modelCalls++;
            var completed = await second(
                chunk =>
                {
                    finalResponse += chunk;
                    publish(chunk);
                },
                () =>
                {
                    finalResponse = string.Empty;
                    resetPublishedAttempt?.Invoke();
                },
                cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(completed.Error)) throw new InvalidOperationException(completed.Error);
            if (finalResponse.Length == 0) finalResponse = completed.Response;
            if (!IsCompleteAnswer(finalResponse)) throw new InvalidOperationException("The AI provider returned an incomplete response.");
            return new LiveCopilotResult(finalResponse, decision, modelCalls, protocolRetries, retrieval.Status, retrieval.Snippets);
        }

        public static bool IsCompleteAnswer(string response)
        {
            if (string.IsNullOrWhiteSpace(response)) return false;
            return response.Split("```", StringSplitOptions.None).Length % 2 == 1;
        }

        public static string ExtractBody(string response)
        {
            var marker = PhantomControlFrameParser.BodyDelimiter;
            var index = response.IndexOf(marker, StringComparison.Ordinal);
            return index < 0 ? string.Empty : response[(index + marker.Length)..];
        }
    }
}
