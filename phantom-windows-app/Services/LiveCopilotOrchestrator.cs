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
            string questionText,
            bool retrievalAvailable,
            bool hasActiveEvidence,
            ModelStream firstModel,
            Func<LiveTurnDecision, CancellationToken, Task<LiveCopilotRetrieval>> retrieve,
            Func<LiveTurnDecision, LiveCopilotRetrieval, ModelStream> secondModel,
            Action<string> publish,
            Action? resetPublishedAttempt,
            CancellationToken cancellationToken,
            Func<LiveTurnDecision, IReadOnlyList<string>>? preferredDocumentsForDecision = null,
            Action<string>? protocolRejected = null,
            Action<LiveTurnDecision, int, bool>? decisionParsed = null)
        {
            _ = retrievalAvailable;
            _ = hasActiveEvidence;
            var modelCalls = 0;
            var protocolRetries = 0;
            PhantomControlFrameParser parser;
            var firstResponse = string.Empty;
            var fallbackBuffer = string.Empty;

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
                    firstResponse = first.Response;
                    if (!string.IsNullOrEmpty(first.Error)) throw new InvalidOperationException(first.Error);
                    if (!parser.HasReceivedChunks && first.Response.Length > 0)
                    {
                        var visible = parser.Feed(first.Response);
                        if (visible.Length > 0) publish(visible);
                    }
                    parser.Complete();
                    break;
                }
                catch (PhantomProtocolException error)
                {
                    var candidate = parser.GetFallbackAnswerText();
                    if (string.IsNullOrWhiteSpace(candidate))
                    {
                        candidate = PhantomControlFrameParser.ExtractBareAnswer(firstResponse);
                    }

                    if (!string.IsNullOrWhiteSpace(candidate))
                    {
                        fallbackBuffer = candidate;
                    }

                    protocolRejected?.Invoke(error.Code);
                    if (IsCompleteAnswer(fallbackBuffer))
                    {
                        protocolRetries = Math.Max(protocolRetries, 1);
                        protocolRejected?.Invoke("control_frame_fallback");
                        var fallbackDecision = PhantomControlFrameParser.FallbackAnswerDecision();
                        decisionParsed?.Invoke(fallbackDecision, modelCalls, false);
                        publish(fallbackBuffer);
                        return new LiveCopilotResult(
                            fallbackBuffer, fallbackDecision, modelCalls, protocolRetries, "not_requested",
                            Array.Empty<RetrievedContextSnippet>());
                    }

                    if (protocolRetries == 0)
                    {
                        protocolRetries++;
                        resetPublishedAttempt?.Invoke();
                        continue;
                    }

                    throw;
                }
            }

            var decision = parser.Decision!;
            if (decision.Action == LiveCopilotAction.Retrieve)
            {
                var query = string.IsNullOrWhiteSpace(decision.RetrievalQuery)
                    ? LiveCopilotRetrievePolicy.NormalizeRetrievalQuery(decision, questionText)
                    : decision.RetrievalQuery;
                var docs = decision.PreferredDocumentIds.Count > 0
                    ? decision.PreferredDocumentIds
                    : preferredDocumentsForDecision?.Invoke(decision) ?? Array.Empty<string>();
                decision = decision with { RetrievalQuery = query, PreferredDocumentIds = docs };
            }

            decisionParsed?.Invoke(decision, modelCalls, false);
            if (protocolRetries > 0 && decision.Action == LiveCopilotAction.Retrieve)
                throw new PhantomProtocolException("control_repair_retrieve_invalid");
            if (decision.Action != LiveCopilotAction.Retrieve)
            {
                var answer = PhantomControlFrameParser.ResolveAnswer(firstResponse);
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
            => PhantomControlFrameParser.ExtractAnswerBody(response);
    }
}
