import Foundation

@MainActor
final class ConversationManager {
    static let recentFullMessageCount = 10
    private var activeProjectId = ""
    private var previousDocumentIds: [String] = []
    private var previousSnippets: [KnowledgeSnippet] = []
    private var knowledgeRevision = ""
    private var groundingCache: [String: String] = [:]
    private(set) var lastTrace = "route=Direct scope=None"

    func requestMessages(
        question: String,
        interviewType: String,
        resume: String,
        jobDescription: String,
        conversation: [ChatMessage],
        modelId: String,
        knowledgeEnabled: Bool,
        knowledgeBase: StartupSnapshot.KnowledgeBase?,
        knowledgeSnippets: [KnowledgeSnippet]
    ) -> [ChatMessage] {
        if let knowledgeBase { updateRevision(knowledgeBase) }
        let projectScoped = isProjectQuestion(question)
        var system = InterviewPrompt.resolve(interviewType, resume: projectScoped ? "" : resume, jobDescription: jobDescription)
        let grounding = knowledgeEnabled ? structuredGrounding(for: question, knowledgeBase: knowledgeBase) : nil
        if let grounding, !grounding.isEmpty {
            system += "\n\n" + grounding
        }
        var snippets = knowledgeSnippets
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
        } else if knowledgeEnabled, shouldRetrieveKnowledge(for: question), grounding == nil {
            system += "\n\nNo grounded profile or document evidence matched this question. Say that the requested candidate detail is not available; do not invent it."
        }

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
        let personal = ["my ", "your experience", "candidate", "resume", "profile", "project", "current role", "previous role"].contains(where: text.contains)
        if !personal, directTechnical.contains(where: text.hasPrefix) { return false }
        return [
            "my resume", "my profile", "my strength", "my experience", "my project", "recent project",
            "previous project", "current role", "previous role", "day-to-day", "tell me about yourself",
            "worked with", "from my notes", "document", "deployment", "implementation", "low-level detail", "why did you choose"
        ].contains(where: text.contains)
    }

    func preferredDocumentIds(for question: String, knowledgeBase: StartupSnapshot.KnowledgeBase?) -> [String] {
        if isDocumentFollowUp(question), !previousDocumentIds.isEmpty {
            lastTrace = "route=Retrieve scope=PreviousDocuments documents=\(previousDocumentIds.joined(separator: ","))"
            return previousDocumentIds
        }
        guard isProjectQuestion(question), let projects = knowledgeBase?.projectCards else {
            lastTrace = shouldRetrieveKnowledge(for: question) ? "route=Retrieve scope=Global" : "route=Direct scope=None"
            return []
        }
        let project = selectProject(question, projects: projects)
            ?? projects.first(where: { $0.projectCardId == activeProjectId })
        let ids = project?.sourceDocumentIds ?? []
        lastTrace = "route=Project scope=ActiveProject target=\(project?.title ?? "missing") documents=\(ids.joined(separator: ","))"
        return ids
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

    func localKnowledgeSnippets(question: String, resume: String, jobDescription: String, preferredDocumentIds: [String]) -> [KnowledgeSnippet] {
        let query = tokens(question)
        guard !query.isEmpty else { return [] }
        let documents = [("local-resume", "Resume", resume), ("local-job-description", "Job description", jobDescription)]
            .filter { preferredDocumentIds.isEmpty || preferredDocumentIds.contains($0.0) }
        return documents.flatMap { id, title, text in
            text.components(separatedBy: "\n\n").filter { !$0.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }.map { chunk in
                let overlap = query.intersection(tokens(chunk)).count
                return KnowledgeSnippet(documentId: id, documentTitle: title, text: String(chunk.prefix(1_200)), score: Double(overlap) / Double(max(1, query.count)))
            }
        }.filter { $0.score > 0 }.sorted { $0.score > $1.score }.prefix(3).map { $0 }
    }

    func reset() {
        activeProjectId = ""
        previousDocumentIds = []
        previousSnippets = []
        groundingCache = [:]
        knowledgeRevision = ""
        lastTrace = "route=Direct scope=None"
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

    private func structuredGrounding(
        for question: String,
        knowledgeBase: StartupSnapshot.KnowledgeBase?
    ) -> String? {
        guard let knowledgeBase else { return nil }
        let text = question.lowercased()
        let projects = knowledgeBase.projectCards ?? []
        let projectAsk = isProjectQuestion(question)
        if projectAsk {
            let alternate = ["another project", "other project", "different project", "any other project"].contains(where: text.contains)
            let selected = selectProject(question, projects: projects)
                ?? (alternate ? projects.first(where: { $0.projectCardId != activeProjectId }) : nil)
                ?? projects.first(where: { $0.projectCardId == activeProjectId })
                ?? projects.first(where: \.isRecent)
                ?? projects.sorted(by: { $0.sortOrder < $1.sortOrder }).first
            if let selected {
                activeProjectId = selected.projectCardId
                let variant = ["architecture", "stack", "challenge", "impact"].first(where: text.contains) ?? "overview"
                lastTrace = "route=Project scope=ActiveProject target=\(selected.title) variant=\(variant)"
                return groundingCache["project:\(selected.projectCardId):\(variant)"] ?? projectGrounding(selected, question: text)
            }
            return "No grounded candidate project evidence is available. Give only a clearly labelled example that the candidate must adapt; do not present it as real experience."
        }

        let experienceAsk = ["my experience", "your experience", "current role", "previous role", "responsibilities", "day-to-day", "worked at", "company", "employer"].contains(where: text.contains)
        if experienceAsk, let experiences = knowledgeBase.experienceCards, !experiences.isEmpty {
            if ["timeline", "career journey", "career path", "all experience"].contains(where: text.contains) {
                return "Career chronology below is authoritative and ordered oldest to newest. Never reverse it.\n\n" + experiences.sorted(by: { $0.sortOrder < $1.sortOrder }).map(experienceGrounding).joined(separator: "\n\n")
            }
            let selected = experiences.first(where: {
                text.contains($0.company.lowercased()) || text.contains($0.role.lowercased())
                    || $0.skills.contains(where: { text.contains($0.lowercased()) })
            }) ?? experiences.first(where: \.isCurrent) ?? experiences.sorted(by: { $0.sortOrder < $1.sortOrder }).last!
            lastTrace = "route=Experience scope=Structured target=\(selected.company)"
            return groundingCache["experience:\(selected.experienceCardId)"] ?? experienceGrounding(selected)
        }

        let profileAsk = ["tell me about yourself", "introduce yourself", "my profile", "my resume", "my strength", "my weakness", "background", "current position"].contains(where: text.contains)
        if profileAsk, let profile = knowledgeBase.profileCard {
            let variant = text.contains("strength") ? "strength" : "general"
            lastTrace = "route=Profile scope=Structured variant=\(variant)"
            return groundingCache["profile:\(variant)"] ?? profileGrounding(profile, question: text)
        }
        return nil
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

    private func isProjectQuestion(_ question: String) -> Bool {
        let text = question.lowercased()
        return ["project", "architecture", "system design", "tech stack", "stack", "challenge", "impact", "implementation", "deployment"].contains(where: text.contains)
            || (!activeProjectId.isEmpty && ["this", "that", "it", "the project"].contains(where: text.contains))
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
