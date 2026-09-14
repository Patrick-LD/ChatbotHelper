# ChatbotHelper

RAG-chatbot i C# med dynamiske tools — 3. semester eksamensprojekt.

En chatbot der kombinerer **viden** (RAG-søgning i dokumentation via vektor-database)
og **handling** (dynamisk registrerede tools, f.eks. "opret en medarbejder"). Botten
vurderer selv, om et spørgsmål kræver dokumentationssøgning, et tool-kald eller begge.

👉 **[BUILD_GUIDE.md](BUILD_GUIDE.md)** — projektplan, arkitektur, faser og tjekpunkter.
👉 **[docs/FASE-1-FORKLARET.md](docs/FASE-1-FORKLARET.md)** — gennemgang af fase 1-koden og begrundelserne bag valgene.
👉 **[docs/FASE-2-FORKLARET.md](docs/FASE-2-FORKLARET.md)** — RAG-kernen forklaret, inkl. evaluering og baseline.
👉 **[docs/FASE-3-FORKLARET.md](docs/FASE-3-FORKLARET.md)** — handlings-tools og bekræftelses-flowet forklaret.

## Teknologi

| Lag | Valg |
| --- | --- |
| Backend | ASP.NET Core Web API (.NET 9) |
| AI-framework | Microsoft.Extensions.AI (`IChatClient`) + OllamaSharp |
| LLM | Ollama lokalt → cloud-API senere |
| Embeddings | Ollama (`nomic-embed-text`) |
| Vektor-database | Postgres + pgvector (Docker) |
| Tool-protokol | MCP (`ModelContextProtocol`) |

## Status

| Fase | Status |
| --- | --- |
| GitHub-opsætning (branches, protection, CI) | ✅ verificeret |
| Fase 1 — Fundament | ✅ verificeret mod llama3.1 |
| Fase 2 — RAG-kernen | ✅ baseline 16/20 (80 %) |
| Fase 3 — Statiske tools | ✅ 23/26 (88 %), tool-scenarier 6/6 |
| Fase 4 — Dynamisk tool-registry + MCP | ⬜ |
| Fase 5 — Hærdning og guidning | ⬜ |

## Kom i gang

```bash
# Forudsætninger: .NET 9 SDK, Docker Desktop, Ollama
ollama pull llama3.1
ollama pull nomic-embed-text

docker compose up -d                        # Postgres + pgvector på 127.0.0.1:5432
dotnet test Chatbot.sln
dotnet run --project src/Chatbot.DummyHr    # HR-dummy ("PersonaleNet") på http://localhost:5100
dotnet run --project src/Chatbot.Api        # http://localhost:5022
```

Swagger UI: <http://localhost:5022/swagger>. Klar-tjek: `GET /health`.

Indeksér dokumentationen første gang (og efter ændringer i `data/dokumentation`):

```bash
curl -X POST http://localhost:5022/ingest
```

Chat via curl — svaret indeholder et `conversationId`, som sendes med i næste kald,
så botten husker konteksten, og `sources` med de dokumentationsafsnit, botten slog op:

```bash
curl -X POST http://localhost:5022/chat -H "Content-Type: application/json"   -d '{"message":"Hvordan opretter jeg en ny medarbejder?"}'

curl -X POST http://localhost:5022/chat -H "Content-Type: application/json"   -d '{"message":"Hvilke oplysninger skal jeg have klar?","conversationId":"<id fra svaret>"}'
```

Handlinger kræver bekræftelse: bed botten oprette en medarbejder, og svaret indeholder `pendingAction`
med en opsummering. Svar "ja" i samme samtale for at udføre, "nej" for at annullere, eller ret oplysningerne:

```bash
curl -X POST http://localhost:5022/chat -H "Content-Type: application/json" \
  -d '{"message":"Opret Mette Nielsen, mette@firma.dk, Konsulent i Salg, start 2026-11-01"}'

curl -X POST http://localhost:5022/chat -H "Content-Type: application/json" \
  -d '{"message":"ja","conversationId":"<id fra svaret>"}'
```

Samme flows ligger klikbart i [Chatbot.Api.http](src/Chatbot.Api/Chatbot.Api.http).
`GET /search?q=...` viser de rå søgeresultater uden modellen — det første sted at kigge, når et svar er dårligt.

### Evaluering

Evalueringssættet ([docs/evaluering/evalueringssaet.json](docs/evaluering/evalueringssaet.json)) køres mod et kørende API
og skriver en rapport. Kør det efter hver ændring af chunking, prompt eller model:

```bash
dotnet run --project tools/Chatbot.Eval -- docs/evaluering/evalueringssaet.json docs/evaluering/resultater/<dato>-<aendring>.md
```

### Projektstruktur

| Sti | Indhold |
| --- | --- |
| `src/Chatbot.Api` | Tyndt web-lag: DI-opsætning, `POST /chat`, `POST /ingest`, `GET /search`, `GET /health`, Swagger |
| `src/Chatbot.Core` | Orkestrering (`ChatService`, `IChatToolProvider`, bekræftelses-flow), RAG (`TextChunker`, `IngestionService`, `DocumentSearchTool`), handlings-tools (`EmployeeTools`, `PendingAction`, `ConfirmationParser`), interfaces |
| `src/Chatbot.Infrastructure` | Implementeringer mod eksterne systemer: `PgVectorStore` (Postgres + pgvector), `HttpEmployeeService` (HR-API) |
| `src/Chatbot.DummyHr` | Dummy-HR-API i hukommelsen — "PersonaleNet" til test af handlinger |
| `tests/Chatbot.Tests` | Enhedstests mod fakes: `FakeChatClient`, `FakeEmbeddingGenerator`, `InMemoryVectorStore`, `FakeEmployeeService` |
| `tools/Chatbot.Eval` | Konsolværktøj der kører evalueringssættet og skriver en Markdown-rapport |
| `data/dokumentation` | Testdokumentation (fiktiv) der indekseres af `/ingest` |
| `docs/evaluering` | Evalueringssæt og resultater pr. kørsel |

Systemprompt, model, timeout og historik-længde konfigureres i `Chatbot`-sektionen, chunking, `TopK`, `MinScore`,
embedding-model og databaseforbindelse i `Rag`-sektionen, og HR-API'ets adresse og bekræftelsers levetid i
`Tools`-sektionen i [appsettings.json](src/Chatbot.Api/appsettings.json) — ingen kodeændring nødvendig.

> **Ollama-endpointet er `127.0.0.1` og ikke `localhost`.** Kører der samtidig en Ollama i Docker,
> lytter den på IPv6 på samme port 11434, og `localhost` kan ramme den forkerte server — med
> `model not found` som resultat. Tjek hvem der svarer med `curl http://127.0.0.1:11434/api/tags`.

Fejl i `Chatbot`- eller `Rag`-sektionen (tom systemprompt, ugyldigt endpoint, chunk-overlap større end chunk)
stopper opstarten med en tydelig besked i stedet for at vise sig som mærkelige svar senere.
Kan Postgres ikke nås, starter API'et alligevel — chatten virker, men søgningen fejler, og loggen siger hvorfor.

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
