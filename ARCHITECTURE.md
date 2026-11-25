# LangGraph Digester Architecture

The workflow is implemented in `DocumentAnalysisService` and `AgentServices` with an explicit graph-like stage sequence:

Document Ingestion -> Parsing -> Normalization -> Requirement/Security -> Compliance -> Business Impact -> Contradiction -> Human Review -> Confidence -> Supervisor.

Specialist agents emit findings only from source statements. The supervisor deduplicates by finding type and source text, promotes priority, calculates the response confidence, and removes unsupported output.
