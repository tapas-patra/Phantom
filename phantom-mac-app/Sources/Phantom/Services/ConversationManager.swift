import Foundation
import NaturalLanguage

enum InterviewIntent: String, Equatable {
    case personal = "Personal"
    case general = "General"
    case hybrid = "Hybrid"
    case ambiguous = "Ambiguous"
}

enum AnswerSource: String, Equatable {
    case knowledgeBase = "KB"
    case universal = "Universal"
    case mixed = "KB + Universal"
    case template = "Template"
    case clarification = "Clarification"
}

struct AnswerResolution {
    let source: AnswerSource
    let intent: InterviewIntent

    var instruction: String {
        switch source {
        case .knowledgeBase:
            return "Source: KB. Candidate-specific claims must be supported by Interview Grounding or Relevant account knowledge. Do not invent projects, roles, outcomes, metrics, employers, or technologies."
        case .mixed:
            return "Source: KB + Universal. Use grounded knowledge for candidate-specific facts. Use general knowledge only to explain concepts and tradeoffs; do not turn it into a claim about the candidate."
        case .template:
            return "Source: Template. There is no verified candidate evidence for this personal question. Provide a concise interview-ready template with placeholders or conditional wording, and do not present it as the candidate's real experience."
        case .universal:
            return "Source: Universal. Answer the general interview question directly. Do not claim the candidate used a project, employer, technology, metric, or achievement unless it appears in grounded context."
        case .clarification:
            return "Source: Clarification. Ask one concise question to establish whether the interviewer wants the candidate's real experience or a general explanation. Do not guess or invent a personal answer."
        }
    }
}

@MainActor
final class ConversationManager {
    static let recentFullMessageCount = 10
    private var activeProjectId = ""
    private var previousDocumentIds: [String] = []
    private var previousSnippets: [KnowledgeSnippet] = []
    private var knowledgeRevision = ""
    private var groundingCache: [String: String] = [:]
    private(set) var lastTrace = "route=Direct scope=None"
    private(set) var lastAnswerResolution = AnswerResolution(source: .universal, intent: .general)

    func requestMessages(
        question: String,
        interviewType: String,
        resume: String,
        jobDescription: String,
        conversation: [ChatMessage],
        modelId: String,
        knowledgeEnabled: Bool,
        knowledgeBase: StartupSnapshot.KnowledgeBase?,
        knowledgeSnippets: [KnowledgeSnippet],
        semanticIntent: InterviewIntent
    ) -> [ChatMessage] {
        if let knowledgeBase { updateRevision(knowledgeBase) }
        let projectScoped = requiresCandidateProjectGrounding(question, intent: semanticIntent)
        var system = InterviewPrompt.resolve(interviewType, resume: projectScoped ? "" : resume, jobDescription: jobDescription)
        var grounding = knowledgeEnabled ? structuredGrounding(for: question, intent: semanticIntent, knowledgeBase: knowledgeBase) : nil
        var snippets = knowledgeSnippets
        let resolution = resolveAnswerResolution(intent: semanticIntent, grounding: grounding, snippets: snippets)
        if resolution.source != .knowledgeBase && resolution.source != .mixed {
            grounding = nil
            snippets = []
        }
        if let grounding, !grounding.text.isEmpty {
            system += "\n\n" + grounding.text
        }
        if !snippets.isEmpty {
            previousSnippets = snippets
            previousDocumentIds = Array(Set(snippets.map(\.documentId))).sorted()
        } else if isDocumentFollowUp(question), !previousSnippets.isEmpty {
            snippets = previousSnippets
        }
        if knowledgeEnabled, !snippets.isEmpty {
            system += "\n\nRelevant account knowledge:\n" + snippets.map {
                "[\($0.documentTitle)]\n\($0.text)"
            }.joined(separator: "\n\n")
        } else if knowledgeEnabled, resolution.source == .template {
            system += "\n\nNo grounded profile or document evidence matched this question. Say that the requested candidate detail is not available; do not invent it."
        }
        lastAnswerResolution = resolution
        lastTrace += " intent=\(resolution.intent.rawValue) source=\(resolution.source.rawValue)"
        system += "\n\nAnswer Source Contract:\n\(resolution.instruction)"

        let configuration = ModelContextRegistry.configuration(for: modelId)
        var budget = max(500, configuration.maxContextTokens - configuration.maxResponseTokens - estimate(system))
        let valid = conversation.filter { !$0.content.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }
        var recentReversed: [ChatMessage] = []
        for message in valid.reversed() {
            guard recentReversed.count < Self.recentFullMessageCount else { break }
            let tokens = estimate(message.content)
            if !recentReversed.isEmpty, budget - tokens < 100 { break }
            recentReversed.append(message)
            budget -= tokens
        }
        let recent = recentReversed.reversed()
        var older: [ChatMessage] = []
        var assistantPairs = 0
        for message in valid.dropLast(recentReversed.count).reversed() {
            guard assistantPairs < configuration.slidingWindowSize else { break }
            let content = message.summary?.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty == false
                ? message.summary!
                : String(message.content.prefix(200))
            let tokens = estimate(content)
            guard budget - tokens >= 100 else { break }
            older.append(ChatMessage(id: message.id, role: message.role, content: content, summary: message.summary))
            budget -= tokens
            if message.role == "assistant" { assistantPairs += 1 }
        }
        return [ChatMessage(role: "system", content: system)] + Array(older.reversed()) + Array(recent)
    }

    func shouldRetrieveKnowledge(for question: String) -> Bool {
        let text = question.lowercased()
        let directTechnical = ["what is ", "explain dependency", "write code", "algorithm", "difference between"]
        let personal = isPersonalQuestion(question)
        if !personal, directTechnical.contains(where: text.hasPrefix) { return false }
        return [
            "my resume", "my profile", "my strength", "my experience", "my project", "recent project",
            "previous project", "current role", "previous role", "day-to-day", "tell me about yourself",
            "worked with", "from my notes", "document", "deployment", "implementation", "low-level detail", "why did you choose",
            "personally own", "personally owned", "your contribution", "my contribution", "achievement", "accomplishment",
            "why should we hire", "career history", "employment history", "education", "certification"
        ].contains(where: text.contains) || personal
    }

    func semanticIntent(for question: String, knowledgeBase: StartupSnapshot.KnowledgeBase? = nil) -> InterviewIntent {
        let text = question.lowercased()
        if let projects = knowledgeBase?.projectCards,
           selectProject(question, projects: projects) != nil {
            return .hybrid
        }
        if isProfileQuestion(question) || isExperienceQuestion(question) {
            return .personal
        }
        if requiresCandidateProjectGrounding(question, intent: .personal) {
            return isProjectDetailQuestion(question) ? .hybrid : .personal
        }
        if isClearlyGeneralQuestion(text) {
            return .general
        }
        guard let embedding = NLEmbedding.sentenceEmbedding(for: .english) else {
            return .ambiguous
        }

        let prototypes: [(InterviewIntent, [String])] = [
            (.personal, ["Describe a project you built and the results you achieved.", "Tell me about your own work experience and responsibilities."]),
            (.general, ["Explain a software engineering concept and compare tradeoffs.", "How would you design a technical system in a hypothetical scenario?"]),
            (.hybrid, ["Describe how you used a technical concept in your project and why it was appropriate."])
        ]
        let ranked = prototypes.compactMap { intent, examples -> (InterviewIntent, Double)? in
            let distance = examples.map { embedding.distance(between: question, and: $0) }.min()
            return distance.map { (intent, $0) }
        }.sorted { $0.1 < $1.1 }
        guard let best = ranked.first else { return .ambiguous }
        if ranked.count > 1, ranked[1].1 - best.1 < 0.06 { return .ambiguous }
        return best.0
    }

    func shouldSearchKnowledge(for question: String, preferredDocumentIds: [String], intent: InterviewIntent, knowledgeBase: StartupSnapshot.KnowledgeBase?) -> Bool {
        if isDocumentFollowUp(question) { return !preferredDocumentIds.isEmpty }
        guard intent == .personal || intent == .hybrid else { return false }
        guard let knowledgeBase else { return true }
        if isProfileQuestion(question), knowledgeBase.profileCard != nil { return false }
        if let projects = knowledgeBase.projectCards,
           !projects.isEmpty,
           requiresCandidateProjectGrounding(question, intent: intent) || selectProject(question, projects: projects) != nil {
            return false
        }
        if let experiences = knowledgeBase.experienceCards,
           !experiences.isEmpty,
           isExperienceTimelineQuestion(question) || isExperienceQuestion(question) || selectExperience(question, experiences: experiences) != nil {
            return false
        }
        return intent == .personal || intent == .hybrid
    }

    func preferredDocumentIds(for question: String, intent: InterviewIntent, knowledgeBase: StartupSnapshot.KnowledgeBase?) -> [String] {
        if isDocumentFollowUp(question), !previousDocumentIds.isEmpty {
            lastTrace = "route=Retrieve scope=PreviousDocuments documents=\(previousDocumentIds.joined(separator: ","))"
            return previousDocumentIds
        }
        let text = question.lowercased()
        if intent != .general, let projects = knowledgeBase?.projectCards,
           requiresCandidateProjectGrounding(question, intent: intent) || selectProject(question, projects: projects) != nil {
            let alternate = ["another project", "other project", "different project", "any other project"].contains(where: text.contains)
            let followUp = !activeProjectId.isEmpty && ["this", "that", "it", "the project"].contains(where: text.contains)
            let generic = ["my project", "your project", "recent project", "a project", "any project"].contains(where: text.contains)
            let project = selectProject(question, projects: projects)
                ?? (alternate ? projects.first(where: { $0.projectCardId != activeProjectId }) : nil)
                ?? (followUp ? projects.first(where: { $0.projectCardId == activeProjectId }) : nil)
                ?? (generic ? projects.first(where: \.isRecent) : nil)
                ?? (generic ? projects.sorted(by: { $0.sortOrder < $1.sortOrder }).first : nil)
            let ids = project?.sourceDocumentIds ?? []
            lastTrace = "route=Project scope=ActiveProject target=\(project?.title ?? "missing") documents=\(ids.joined(separator: ","))"
            return ids
        }
        if intent != .general, let experiences = knowledgeBase?.experienceCards,
           isExperienceQuestion(question) || selectExperience(question, experiences: experiences) != nil {
            let ids = isExperienceTimelineQuestion(question)
                ? experiences.flatMap(\.sourceDocumentIds)
                : selectExperience(question, experiences: experiences)?.sourceDocumentIds ?? []
            lastTrace = "route=Experience scope=Structured documents=\(ids.joined(separator: ","))"
            return Array(Set(ids)).sorted()
        }
        if intent != .general, isProfileQuestion(question), let profile = knowledgeBase?.profileCard {
            lastTrace = "route=Profile scope=Structured documents=\(profile.sourceDocumentIds.joined(separator: ","))"
            return profile.sourceDocumentIds
        }
        lastTrace = shouldRetrieveKnowledge(for: question) ? "route=Retrieve scope=Global" : "route=Direct scope=None"
        return []
    }

    func warm(_ knowledgeBase: StartupSnapshot.KnowledgeBase) {
        updateRevision(knowledgeBase)
        if let profile = knowledgeBase.profileCard {
            groundingCache["profile:general"] = profileGrounding(profile, question: "profile")
            groundingCache["profile:strength"] = profileGrounding(profile, question: "strength")
        }
        for experience in knowledgeBase.experienceCards ?? [] {
            groundingCache["experience:\(experience.experienceCardId)"] = experienceGrounding(experience)
        }
        for project in knowledgeBase.projectCards ?? [] {
            for variant in ["overview", "architecture", "stack", "challenge", "impact"] {
                groundingCache["project:\(project.projectCardId):\(variant)"] = projectGrounding(project, question: variant)
            }
        }
    }

    func localKnowledgeSnippets(question: String, resume: String, jobDescription: String, preferredDocumentIds: [String], intent: InterviewIntent) -> [KnowledgeSnippet] {
        let query = tokens(question)
        guard !query.isEmpty else { return [] }
        var documents = [("local-resume", "Resume", resume)]
        if intent != .personal {
            documents.append(("local-job-description", "Job description", jobDescription))
        }
        let eligibleDocuments = documents
            .filter { preferredDocumentIds.isEmpty || preferredDocumentIds.contains($0.0) }
        return eligibleDocuments.flatMap { id, title, text in
            text.components(separatedBy: "\n\n").filter { !$0.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }.map { chunk in
                let overlap = query.intersection(tokens(chunk)).count
                return KnowledgeSnippet(documentId: id, documentTitle: title, text: String(chunk.prefix(1_200)), score: Double(overlap) / Double(max(1, query.count)))
            }
        }.filter { $0.score >= 0.20 }.sorted { $0.score > $1.score }.prefix(3).map { $0 }
    }

    func reset() {
        activeProjectId = ""
        previousDocumentIds = []
        previousSnippets = []
        groundingCache = [:]
        knowledgeRevision = ""
        lastTrace = "route=Direct scope=None"
        lastAnswerResolution = AnswerResolution(source: .universal, intent: .general)
    }

    static func estimate(_ text: String) -> Int { max(1, text.count / 4) }

    func finalizeAssistantResponse(_ response: String) -> (content: String, summary: String) {
        let pattern = #"SUMMARY:\s*(.+?)(?:\n|$)"#
        guard let regex = try? NSRegularExpression(pattern: pattern, options: [.caseInsensitive]),
              let match = regex.firstMatch(in: response, range: NSRange(response.startIndex..., in: response)),
              let summaryRange = Range(match.range(at: 1), in: response) else {
            return (response, response.count > 100 ? String(response.prefix(100)) + "..." : response)
        }
        let summary = String(response[summaryRange]).trimmingCharacters(in: .whitespacesAndNewlines)
        let content = regex.stringByReplacingMatches(
            in: response,
            range: NSRange(response.startIndex..., in: response),
            withTemplate: ""
        ).trimmingCharacters(in: .whitespacesAndNewlines)
        return (content, summary)
    }

    private func estimate(_ text: String) -> Int { Self.estimate(text) }

    private struct Grounding {
        let text: String
        let hasCandidateEvidence: Bool
    }

    private func structuredGrounding(
        for question: String,
        intent: InterviewIntent,
        knowledgeBase: StartupSnapshot.KnowledgeBase?
    ) -> Grounding? {
        guard let knowledgeBase else { return nil }
        let text = question.lowercased()
        let projects = knowledgeBase.projectCards ?? []
        let projectAsk = intent != .general && (requiresCandidateProjectGrounding(question, intent: intent) || selectProject(question, projects: projects) != nil)
        if projectAsk {
            let alternate = ["another project", "other project", "different project", "any other project"].contains(where: text.contains)
            let followUp = !activeProjectId.isEmpty && ["this", "that", "it", "the project"].contains(where: text.contains)
            let generic = ["my project", "your project", "recent project", "a project", "any project"].contains(where: text.contains)
            let selected = selectProject(question, projects: projects)
                ?? (alternate ? projects.first(where: { $0.projectCardId != activeProjectId }) : nil)
                ?? (followUp ? projects.first(where: { $0.projectCardId == activeProjectId }) : nil)
                ?? (generic ? projects.first(where: \.isRecent) : nil)
                ?? (generic ? projects.sorted(by: { $0.sortOrder < $1.sortOrder }).first : nil)
            if let selected {
                activeProjectId = selected.projectCardId
                let variant = ["architecture", "stack", "challenge", "impact"].first(where: text.contains) ?? "overview"
                lastTrace = "route=Project scope=ActiveProject target=\(selected.title) variant=\(variant)"
                return Grounding(text: groundingCache["project:\(selected.projectCardId):\(variant)"] ?? projectGrounding(selected, question: text), hasCandidateEvidence: true)
            }
            return Grounding(text: "No grounded candidate project evidence is available. Give only a clearly labelled example that the candidate must adapt; do not present it as real experience.", hasCandidateEvidence: false)
        }

        if let experiences = knowledgeBase.experienceCards,
            !experiences.isEmpty,
           intent != .general,
           isExperienceQuestion(question) || selectExperience(question, experiences: experiences) != nil {
            if isExperienceTimelineQuestion(question) {
                return Grounding(text: "Career chronology below is authoritative and ordered oldest to newest. Never reverse it.\n\n" + experiences.sorted(by: { $0.sortOrder < $1.sortOrder }).map(experienceGrounding).joined(separator: "\n\n"), hasCandidateEvidence: true)
            }
            guard let selected = selectExperience(question, experiences: experiences) else {
                return Grounding(text: "No grounded candidate work experience matches this question. Say that the requested experience is not available; do not invent it.", hasCandidateEvidence: false)
            }
            lastTrace = "route=Experience scope=Structured target=\(selected.company)"
            return Grounding(text: groundingCache["experience:\(selected.experienceCardId)"] ?? experienceGrounding(selected), hasCandidateEvidence: true)
        }

        if intent != .general, isProfileQuestion(question), let profile = knowledgeBase.profileCard {
            let variant = text.contains("strength") ? "strength" : "general"
            lastTrace = "route=Profile scope=Structured variant=\(variant)"
            return Grounding(text: groundingCache["profile:\(variant)"] ?? profileGrounding(profile, question: text), hasCandidateEvidence: true)
        }
        return nil
    }

    private func resolveAnswerResolution(intent: InterviewIntent, grounding: Grounding?, snippets: [KnowledgeSnippet]) -> AnswerResolution {
        let hasEvidence = grounding?.hasCandidateEvidence == true || !snippets.isEmpty
        switch intent {
        case .personal:
            return AnswerResolution(source: hasEvidence ? .knowledgeBase : .template, intent: intent)
        case .hybrid:
            return AnswerResolution(source: hasEvidence ? .mixed : .universal, intent: intent)
        case .general:
            return AnswerResolution(source: .universal, intent: intent)
        case .ambiguous:
            return AnswerResolution(source: hasEvidence ? .knowledgeBase : .clarification, intent: intent)
        }
    }

    private func profileGrounding(_ profile: KnowledgeProfile, question: String) -> String {
        var lines = ["Use only this candidate profile for personal/background questions. If a detail is missing, say so instead of inventing it."]
        append(&lines, "Full name", profile.fullName)
        append(&lines, "Short intro", profile.shortIntro)
        append(&lines, "Current role", profile.currentRole)
        if profile.yearsOfExperience > 0 { lines.append("Years of experience: \(profile.yearsOfExperience)") }
        append(&lines, "Strengths", profile.strengths.joined(separator: ", "))
        append(&lines, "Skills", profile.skills.joined(separator: ", "))
        append(&lines, "Domains", profile.domains.joined(separator: ", "))
        let details = profile.candidateInfo.isEmpty ? profile.resumeText : profile.candidateInfo
        append(&lines, "Candidate information", String(details.prefix(question.contains("strength") ? 600 : 1_200)))
        return lines.joined(separator: "\n")
    }

    private func experienceGrounding(_ value: KnowledgeExperience) -> String {
        var lines = [value.isCurrent
            ? "Answer from this current role only. Do not mix responsibilities from previous companies."
            : "Answer from this past role only and do not present it as current work."]
        append(&lines, "Company", value.company)
        append(&lines, "Role", value.role)
        append(&lines, "Period", [value.startDate, value.isCurrent ? "Present" : value.endDate].filter { !$0.isEmpty }.joined(separator: " – "))
        append(&lines, "Summary", value.summary)
        append(&lines, "Responsibilities and achievements", value.responsibilities)
        append(&lines, "Skills and tools", value.skills.joined(separator: ", "))
        return lines.joined(separator: "\n")
    }

    private func projectGrounding(_ value: KnowledgeProject, question: String) -> String {
        var lines = ["Use only this project for the current answer. Other projects and earlier assistant claims are not evidence. If a detail is missing, say so instead of inventing it."]
        append(&lines, "Project", value.title)
        append(&lines, "Role", value.role)
        append(&lines, "Summary", value.summary)
        if question.contains("stack") || question.contains("architecture") || !question.contains("challenge") {
            append(&lines, "Stack", value.stack.joined(separator: ", "))
            append(&lines, "Architecture", value.architecture)
        }
        if question.contains("challenge") || !question.contains("architecture") { append(&lines, "Challenges", value.challenges) }
        append(&lines, "Impact", value.impact)
        return lines.joined(separator: "\n")
    }

    private func append(_ lines: inout [String], _ label: String, _ value: String) {
        let clean = value.trimmingCharacters(in: .whitespacesAndNewlines)
        if !clean.isEmpty { lines.append("\(label): \(clean)") }
    }

    private func requiresCandidateProjectGrounding(_ question: String, intent: InterviewIntent) -> Bool {
        guard intent != .general else { return false }
        let text = question.lowercased()
        let explicitCandidateProject = [
            "my project", "your project", "recent project", "previous project", "a project you", "project you worked",
            "what did you build", "what have you built", "what did you work on", "tell me about a project"
        ].contains(where: text.contains)
        let activeFollowUp = !activeProjectId.isEmpty && ["this", "that", "it", "the project"].contains(where: text.contains)
        return explicitCandidateProject || activeFollowUp || (!activeProjectId.isEmpty && isProjectDetailQuestion(question))
    }

    private func isProjectDetailQuestion(_ question: String) -> Bool {
        let text = question.lowercased()
        return ["architecture", "design", "stack", "technology", "tech", "challenge", "impact", "tradeoff", "trade-off", "guardrail", "scalab", "deployment", "implementation", "why did you"].contains(where: text.contains)
    }

    private func isClearlyGeneralQuestion(_ text: String) -> Bool {
        ["what is ", "define ", "explain ", "compare ", "difference between ", "how does ", "why does "]
            .contains(where: text.hasPrefix)
    }

    private func isPersonalQuestion(_ question: String) -> Bool {
        let text = question.lowercased()
        return [
            "my ", "your experience", "your background", "your role", "your project", "project", "candidate", "resume", "profile",
            "current role", "previous role", "tell me about yourself", "personally own", "personally owned", "your contribution",
            "achievement", "accomplishment", "why should we hire", "career history", "employment history", "education", "certification"
        ].contains(where: text.contains)
    }

    private func isExperienceQuestion(_ question: String) -> Bool {
        let text = question.lowercased()
        return [
            "experience", "company", "employer", "current role", "previous role", "day-to-day", "responsibilit",
            "personally own", "personally owned", "contribution", "achievement", "accomplishment", "career history", "employment history"
        ].contains(where: text.contains)
    }

    private func isExperienceTimelineQuestion(_ question: String) -> Bool {
        let text = question.lowercased()
        return ["career history", "employment history", "work history", "chronolog", "career journey"].contains(where: text.contains)
    }

    private func selectExperience(_ question: String, experiences: [KnowledgeExperience]) -> KnowledgeExperience? {
        let text = question.lowercased()
        let query = tokens(text)
        let match = experiences.map { experience -> (KnowledgeExperience, Int) in
            let identity = [experience.company, experience.role].joined(separator: " ")
            var score = text.contains(experience.company.lowercased()) ? 12 : 0
            if !experience.role.isEmpty, text.contains(experience.role.lowercased()) { score += 10 }
            score += query.intersection(tokens(identity + " " + experience.skills.joined(separator: " "))).count
            return (experience, score)
        }.filter { $0.1 >= 2 }.max(by: { $0.1 < $1.1 })?.0
        if let match { return match }
        let generic = ["my experience", "your experience", "current role", "previous role", "day-to-day", "responsibilit"].contains(where: text.contains)
        guard generic else { return nil }
        if text.contains("previous role") {
            return experiences.filter { !$0.isCurrent }.sorted(by: { $0.sortOrder > $1.sortOrder }).first
        }
        return experiences.first(where: \.isCurrent) ?? experiences.sorted(by: { $0.sortOrder > $1.sortOrder }).first
    }

    private func isProfileQuestion(_ question: String) -> Bool {
        let text = question.lowercased()
        return [
            "tell me about yourself", "introduce yourself", "walk me through your background", "profile", "background", "strength", "skill", "education", "certification", "why should we hire"
        ].contains(where: text.contains)
    }

    private func isDocumentFollowUp(_ question: String) -> Bool {
        let text = question.lowercased()
        return ["same document", "that document", "from it", "in that file", "continue from"].contains(where: text.contains)
    }

    private func selectProject(_ question: String, projects: [KnowledgeProject]) -> KnowledgeProject? {
        let text = question.lowercased()
        let query = tokens(text)
        return projects.map { project -> (KnowledgeProject, Int) in
            var score = text.contains(project.title.lowercased()) ? 12 : 0
            if !project.slug.isEmpty, text.contains(project.slug.lowercased()) { score += 10 }
            score += query.intersection(tokens(project.title + " " + project.summary + " " + project.stack.joined(separator: " "))).count
            return (project, score)
        }.filter { $0.1 >= 2 }.max(by: { $0.1 < $1.1 })?.0
    }

    private func tokens(_ text: String) -> Set<String> {
        Set(text.lowercased().split(whereSeparator: { !$0.isLetter && !$0.isNumber }).map(String.init).filter { $0.count > 2 })
    }

    private func updateRevision(_ knowledgeBase: StartupSnapshot.KnowledgeBase) {
        let revision = "\(knowledgeBase.knowledgeBaseId):\(knowledgeBase.embeddingVersion):\(knowledgeBase.lastProcessedAtUtc?.timeIntervalSince1970 ?? 0)"
        if revision != knowledgeRevision {
            knowledgeRevision = revision
            groundingCache.removeAll()
            previousDocumentIds.removeAll()
            previousSnippets.removeAll()
        }
    }

}
