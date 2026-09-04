using System;
using System.Collections.Generic;

namespace SecureOverlay.Domain
{
    public enum LiveCopilotAction { Answer, Retrieve, Clarify }
    public enum CopilotMode { Interview, Briefing }
    public enum InterviewDeliveryStyle { Standard, Desi }

    public sealed record LiveTurnDecision(
        LiveCopilotAction Action,
        string QuestionType,
        string Intent,
        string AnswerBasis,
        string EntityType,
        string EntityId,
        string RetrievalQuery,
        IReadOnlyList<string> PreferredDocumentIds,
        int TargetSeconds,
        bool AllowCode,
        double Confidence,
        int ProtocolVersion = 1);

    public sealed record LiveCopilotRetrieval(
        string Status,
        IReadOnlyList<Entities.RetrievedContextSnippet> Snippets,
        string KbRevision = "");

    public sealed record LiveCopilotResult(
        string Answer,
        LiveTurnDecision Decision,
        int ModelCallCount,
        int ProtocolRetryCount,
        string RetrievalStatus,
        IReadOnlyList<Entities.RetrievedContextSnippet> ActiveEvidence);

    public sealed class PhantomProtocolException : Exception
    {
        public PhantomProtocolException(string code) : base(code) => Code = code;
        public string Code { get; }
    }
}
