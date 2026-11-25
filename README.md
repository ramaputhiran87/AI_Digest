# Enterprise AI Digester

Evidence-backed document review with a deterministic specialist-agent pipeline.

## Structure

- `Frontend/AI.Digest.Web` - Angular standalone components, signals, and responsive dashboard
- `Backend/AI.Digest.Api` - .NET 10 Web API, document parsers, and deterministic specialist agents

## Supported documents

Markdown, plain text, HTML, JSON, and XML. Findings include evidence from the original document and source line references. Analysis runs on parsed source content; compressed Markdown is generated separately for download.

The current analysis uses deterministic rules and patterns. Azure OpenAI, LangGraph orchestration, Entra ID, and SQL Server are configuration/scaffolding only and are not required for local analysis.

The dashboard includes priority and agent findings, confidence, JSON export, executive-review Markdown, and compressed-review Markdown.

## Run

```powershell
cd Backend/AI.Digest.Api
dotnet run --urls http://localhost:5050
```

```powershell
cd Frontend/AI.Digest.Web
npm install
npm start
```

Configuration values for Entra ID, Azure OpenAI, and SQL Server can be supplied through `appsettings.json` or environment variables as those integrations are implemented. They are not required to run the deterministic local analysis.
