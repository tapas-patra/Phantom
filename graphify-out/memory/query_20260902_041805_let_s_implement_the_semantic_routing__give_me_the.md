---
type: "query"
date: "2026-09-02T04:18:05.409050+00:00"
question: "Let's implement the semantic routing. Give me the fully production ready implemented output"
contributor: "graphify"
---

# Q: Let's implement the semantic routing. Give me the fully production ready implemented output

## Answer

Expanded from graph vocab: [conversation, answer, response, route, retrieval, knowledge, context, personal, source, prompt, interview, stream]. Implemented a bounded 175ms semantic probe for ambiguous interview questions, run in parallel with existing KB retrieval. The answer policy chooses KB, Universal, KB + Universal, or Template and persists source/intent metadata. Removed KB-miss canned short circuits; tightened experience selection so unknown skills do not select an unrelated job. Added a dependency-free Windows routing-check project. Desktop and check projects build; execution requires a Windows Desktop runtime. graphify update was attempted but the installed command failed with Operation not permitted.,--type,query,--nodes,ConversationManager,RetrievedContextSnippet,ResponsePlan,KnowledgeBaseEmbeddingService,--outcome,useful