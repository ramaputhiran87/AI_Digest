# AI Document Intelligence Architecture

The configured LLM is the primary analysis engine. The old deterministic `AgentServices` implementation is used only when a caller explicitly selects the rules-based fallback.

```mermaid
flowchart TD
    Browser[Angular dashboard] --> Api[.NET 10 API]
    Api --> Parse[Extract text and preserve source lines]
    Parse --> Chunks[Overlapping context chunks]
    Chunks --> Provider{Configured provider}
    Provider --> Gemini[Gemini Flash]
    Provider --> Ollama[Ollama / Qwen3]
    Gemini --> Understanding[LLM document understanding]
    Ollama --> Understanding
    Understanding --> Agents[Parallel LLM specialists]
    Agents --> Consolidation[LLM deduplication and final report]
    Consolidation --> Validation[Schema and exact evidence validation]
    Validation --> Result[Structured JSON, model metadata, exports]
    Result --> Browser
    Provider -. explicit request only .-> Fallback[Deterministic fallback]
```

Each successful AI review invokes the model for document understanding, five specialist tasks (security, architecture, compliance, business impact, and recommendations), and final consolidation. Long documents use per-chunk understanding and specialist calls, with bounded parallelism and a final document-level synthesis.

Programmatic code extracts supported formats, preserves source line references, validates required report fields and severity values, and rejects quotations that cannot be found in the cited source. Evidence validation establishes quotation traceability only; it is not human verification or confirmation of real-world exploitability.

Provider errors are returned explicitly as HTTP 503; there is no automatic switch to deterministic analysis. Provider, model, timing, call count, and available token usage are returned in `modelInfo`. Setup and API examples are in [README.md](README.md).
