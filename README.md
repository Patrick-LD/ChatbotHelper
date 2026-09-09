# ChatbotHelper

RAG-chatbot i C# med dynamiske tools — 3. semester eksamensprojekt.

En chatbot der kombinerer **viden** (RAG-søgning i dokumentation via vektor-database)
og **handling** (dynamisk registrerede tools, f.eks. "opret en medarbejder"). Botten
vurderer selv, om et spørgsmål kræver dokumentationssøgning, et tool-kald eller begge.

👉 **[BUILD_GUIDE.md](BUILD_GUIDE.md)** — projektplan, arkitektur, faser og tjekpunkter.
👉 **[docs/FASE-1-FORKLARET.md](docs/FASE-1-FORKLARET.md)** — gennemgang af fase 1-koden og begrundelserne bag valgene.

## Teknologi

| Lag | Valg |
| --- | --- |
| Backend | ASP.NET Core Web API (.NET 9) |
| AI-framework | Microsoft.Extensions.AI (`IChatClient`) + OllamaSharp |
| LLM | Ollama lokalt → cloud-API senere |
| Embeddings | Ollama (`nomic-embed-text`) |
| Vektor-database | Qdrant eller pgvector (Docker) |
| Tool-protokol | MCP (`ModelContextProtocol`) |

## Status

| Fase | Status |
| --- | --- |
| GitHub-opsætning (branches, protection, CI) | ✅ verificeret |
| Fase 1 — Fundament | ✅ verificeret mod llama3.1 |
| Fase 2 — RAG-kernen | ⬜ |
| Fase 3 — Statiske tools | ⬜ |
| Fase 4 — Dynamisk tool-registry + MCP | ⬜ |
| Fase 5 — Hærdning og guidning | ⬜ |

## Kom i gang

```bash
# Forudsætninger: .NET 9 SDK, Docker Desktop, Ollama
ollama pull llama3.1

dotnet test Chatbot.sln
dotnet run --project src/Chatbot.Api   # http://localhost:5022
```

Swagger UI: <http://localhost:5022/swagger>. Klar-tjek: `GET /health`.

Chat via curl — svaret indeholder et `conversationId`, som sendes med i næste kald,
så botten husker konteksten:

```bash
curl -X POST http://localhost:5022/chat -H "Content-Type: application/json" \
  -d '{"message":"Jeg hedder Rene. Hvad kan du hjælpe med?"}'

curl -X POST http://localhost:5022/chat -H "Content-Type: application/json" \
  -d '{"message":"Hvad hedder jeg?","conversationId":"<id fra svaret>"}'
```

Samme flow ligger klikbart i [Chatbot.Api.http](src/Chatbot.Api/Chatbot.Api.http).

### Projektstruktur

| Sti | Indhold |
| --- | --- |
| `src/Chatbot.Api` | Tyndt web-lag: DI-opsætning, `POST /chat`, `GET /health`, Swagger |
| `src/Chatbot.Core` | `IChatService`/`ChatService` (orkestrering), `IConversationStore` (historik), `ChatbotOptions` |
| `tests/Chatbot.Tests` | Enhedstests af orkestreringen mod en fake `IChatClient` |

Systemprompt, model og historik-længde konfigureres i `Chatbot`-sektionen i
[appsettings.json](src/Chatbot.Api/appsettings.json) — ingen kodeændring nødvendig.

> **Ollama-endpointet er `127.0.0.1` og ikke `localhost`.** Kører der samtidig en Ollama i Docker,
> lytter den på IPv6 på samme port 11434, og `localhost` kan ramme den forkerte server — med
> `model not found` som resultat. Tjek hvem der svarer med `curl http://127.0.0.1:11434/api/tags`.

Fejl i `Chatbot`-sektionen (tom systemprompt, ugyldigt endpoint, negativ historik-længde)
stopper opstarten med en tydelig besked i stedet for at vise sig som mærkelige svar senere.

### Fejlsøgning

Første linje i loggen ved opstart viser, hvilken server og model der faktisk bruges:

```
Chatbot klar. Model llama3.1 via http://127.0.0.1:11434. Historik: 20 beskeder.
```

I `Development` logges **hele prompten og hele svaret** (`Microsoft.Extensions.AI` står på
`Trace` i [appsettings.Development.json](src/Chatbot.Api/appsettings.Development.json)).
Det er vejen til at se, hvad modellen faktisk fik — og fra fase 2 hvilke dokumentations-chunks
der kom med. Skru ned til `Information`, hvis loggen bliver for larmende.

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
