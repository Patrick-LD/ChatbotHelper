# ChatbotHelper

RAG-chatbot i C# med dynamiske tools — 3. semester eksamensprojekt.

En chatbot der kombinerer **viden** (RAG-søgning i dokumentation via vektor-database)
og **handling** (dynamisk registrerede tools, f.eks. "opret en medarbejder"). Botten
vurderer selv, om et spørgsmål kræver dokumentationssøgning, et tool-kald eller begge.

👉 **[BUILD_GUIDE.md](BUILD_GUIDE.md)** — projektplan, arkitektur, faser og tjekpunkter.

## Teknologi

| Lag | Valg |
| --- | --- |
| Backend | ASP.NET Core Web API (.NET 9) |
| AI-framework | Semantic Kernel / Microsoft.Extensions.AI |
| LLM | Ollama lokalt → cloud-API senere |
| Embeddings | Ollama (`nomic-embed-text`) |
| Vektor-database | Qdrant eller pgvector (Docker) |
| Tool-protokol | MCP (`ModelContextProtocol`) |

## Status

| Fase | Status |
| --- | --- |
| GitHub-opsætning (branches, protection, CI) | ✅ verificeret |
| Fase 1 — Fundament | ⬜ |
| Fase 2 — RAG-kernen | ⬜ |
| Fase 3 — Statiske tools | ⬜ |
| Fase 4 — Dynamisk tool-registry + MCP | ⬜ |
| Fase 5 — Hærdning og guidning | ⬜ |

## Kom i gang

```bash
# Forudsætninger: .NET 9 SDK, Docker Desktop, Ollama
ollama pull llama3.1
ollama pull nomic-embed-text

dotnet restore Chatbot.sln
dotnet run --project src/Chatbot.Api
```

Solution og projekter oprettes i fase 1.2 — se BUILD_GUIDE.md.

## Branches og pipeline

| Branch | Rolle | Beskyttet |
| --- | --- | --- |
| `main` | Produktion. Push hertil trigger deploy. | Ja |
| `develop` | **Default branch.** Integration af features. | Ja |
| `feat/...`, `fix/...` | Arbejdsbranches → PR til `develop`. | Nej |

Flow: `feat/xxx` → PR → `develop` → PR → `main`.

### Branch protection

Rulesets `Protect Main` og `Protect develop`, begge **active**:

- Restrict deletions og block force pushes
- PR påkrævet, 1 godkendelse, stale approvals afvises ved nyt push
- Påkrævet status check `build-and-test` med strict policy
- **Bypass:** repository admin (så du kan merge egne PR'er i et solo-projekt)

### Workflows

| Fil | Trigger | Funktion |
| --- | --- | --- |
| `ci-pipeline.yml` | push/PR mod `develop`, `main` | Job `build-and-test` — det check protection kræver |
| `deploy-backend.yml` | push til `main`, manuel | Publish + deploy til Azure App Service |
| `deploy-frontend.yml` | push til `main`, manuel | Build + upload til Azure Blob Storage (`$web`) |

Alle tre er betingede: build/test/deploy springes over, indtil `Chatbot.sln`
respektive `frontend/package.json` findes. CI'en er derfor grøn fra dag ét, og
begynder at gøre rigtigt arbejde af sig selv, når koden kommer.

### Secrets

Oprettes under Settings → Secrets and variables → Actions, når deploy skal bruges:

| Secret | Bruges af |
| --- | --- |
| `AZURE_WEBAPP_PUBLISH_PROFILE` | `deploy-backend.yml` |
| `AZURE_STORAGE_CONNECTION_STRING` | `deploy-frontend.yml` |
