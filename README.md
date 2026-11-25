# Enterprise AI Digester

Evidence-based document analysis using a LangGraph-style supervisor workflow.

## Structure

- `Frontend/AI.Digest.Web` - Angular standalone components and signals
- `Backend/AI.Digest.Api` - .NET 10 Web API and specialist agent workflow

## Supported documents

Markdown, plain text, HTML, JSON, and XML. Every emitted finding must contain source evidence and line references.

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

Configure Entra ID, Azure OpenAI, and SQL Server in `appsettings.json` or environment variables. Azure OpenAI is optional: deterministic evidence-backed analysis runs without it.
