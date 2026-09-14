# Projektplan: RAG-chatbot i C# med dynamiske tools

## 🎯 Projektoverblik

En chatbot i C#, der kombinerer to evner: **viden** (RAG-søgning i dokumentation via en vektor-database) og **handling** (dynamisk registrerede tools, der kan udføre opgaver som "opret en medarbejder"). Botten skal selv vurdere, om et spørgsmål kræver dokumentationssøgning, et tool-kald eller begge dele — og guide brugeren undervejs.

Slutmålet er en platform, hvor nye tools kan tilføjes uden kodeændringer, f.eks. via en database eller MCP-servere.

**Rammer:** Solo-projekt, ingen fast deadline. Der startes med Ollama (lokal model) og skiftes til en cloud-API senere.

---

## ⚠️ Vigtige beslutninger før start

1. **Framework: Semantic Kernel / Microsoft.Extensions.AI.** Microsofts officielle .NET AI-framework har indbygget function calling, vector store-connectors og dynamisk registrering af funktioner. Byg ikke infrastrukturen selv fra bunden.

2. **Dynamiske tools = data, ikke kode.** Et tool er en *definition* (navn, beskrivelse, JSON-skema for parametre, handler-konfiguration som f.eks. et HTTP-endpoint eller et MCP-tool). Definitionerne gemmes i en database og loades ved runtime. Ingen øvre grænse for antal.

3. **MCP fra start.** Brug det officielle C# SDK (`ModelContextProtocol` på NuGet). En MCP-server eksponerer selv sine tools med navne og skemaer — dynamiske tools bliver næsten gratis. Overvej at wrappe egne systemer (f.eks. medarbejder-API'et) som MCP-servere.

4. **Vektor-database: start simpelt.** Qdrant (Docker, godt .NET-klientbibliotek) eller pgvector (hvis Postgres alligevel skal bruges til tool-registry og chathistorik — så er der én database til det hele). Abstrahér adgangen bag et interface, så den kan skiftes senere.

5. **Rettighedsstyring pr. tool er et krav — ikke alle må bruge alle tools.** Brugere og/eller roller skal kunne tildeles rettigheder til de enkelte tools, så f.eks. kun HR kan kalde "opret medarbejder". Konkret betyder det: tool-registret skal have en kobling mellem tools og roller/brugere, og orkestratoren må kun loade de tools ind i samtalen, som den aktuelle bruger har rettighed til — så modellen slet ikke kan se eller kalde resten. Dertil kommer bekræftelse før udførsel ("Jeg opretter nu Lars Hansen — bekræft?") og audit-log af alle tool-kald. Uden dette kan botten misbruges via prompt injection.

---

## 🖥️ LLM-strategi: Ollama først, cloud senere

**Beslutning:** Udvikling starter på Ollama (lokal, gratis), og modellen skiftes ud med en cloud-API (f.eks. Azure OpenAI), når kvaliteten skal op.

- **Chatmodel:** Vælg en lokal model, der understøtter function calling — f.eks. Llama 3.1/3.3, Qwen 2.5/3 eller Mistral. 7-8B-modeller kører på almindelig hardware, men vælger tools mindre pålideligt end cloud-modeller. Husk det ved fejlsøgning: det er ofte modellen, ikke koden.
- **Embedding-model:** Kør også lokalt via Ollama (f.eks. `nomic-embed-text` eller `mxbai-embed-large`).
- **Vigtig detalje:** Embeddings er **ikke kompatible på tværs af modeller**. Skiftes embedding-modellen, skal hele vektor-databasen re-indekseres. Byg derfor ingestion-pipelinen som et job, der kan genkøres med ét klik.
- **Integration:** Ollama eksponerer et API på `http://localhost:11434`. Brug Ollama-connectors til Semantic Kernel / Microsoft.Extensions.AI. Registrér `IChatClient` og `IEmbeddingGenerator` i DI — ved skifte til cloud ændres kun registreringen, ikke resten af koden.
- **Skiftet gøres datadrevet:** Kør evalueringssættet (se Kloge træk) på både Ollama og den nye model, så gevinsten kan måles konkret.

Bemærk: En almindelig chat-licens (f.eks. ChatGPT Plus) giver **ikke** API-adgang — API'en er et separat produkt med pay-per-use-betaling via forudbetalte credits. Ollama-strategien udskyder denne omkostning, til den giver værdi.

---

## 🏗️ Arkitektur

```
Bruger → Chat-API (ASP.NET Core)
              │
         Orkestrator (Semantic Kernel)
              │  LLM får: systemprompt + chathistorik + tilgængelige tools
              │
     ┌────────┼──────────────┐
     │        │              │
  RAG-tool  Dynamiske     MCP-klienter
  (søg i     tools (fra    (eksterne
  vektor-db) tool-registry  servere)
             i database)
```

**Tre bærende principper:**

- **Dokumentationssøgning er bare endnu et tool.** `søg_i_dokumentation` eksponeres på linje med de andre tools. LLM'en vælger selv, om den skal søge, handle eller begge dele. Det giver naturlige flows: "Hvordan opretter jeg en medarbejder?" → botten søger, forklarer processen og tilbyder at udføre den.
- **Tool-registry i databasen:** En tabel med tool-definitioner (navn, beskrivelse, JSON-skema, handler-type: HTTP/MCP/intern, endpoint-konfiguration). Ved hver samtale loades relevante tools som Semantic Kernel-funktioner. Ved 50+ tools: filtrér tools semantisk (via vektor-databasen), før de gives til modellen.
- **Ingestion-pipeline til RAG:** Dokumenter → chunking → embeddings → vektor-database. Kør som separat, genkørbart job, så dokumentation kan opdateres og modeller skiftes.

---

## 🗂️ Faser

### Fase 1 — Fundament
**Mål:** Et kørende skelet.

**1.1 Lokalt miljø**
- [x] Installér Ollama fra ollama.com (v0.33.2)
- [x] Hent en chatmodel: `ollama pull llama3.1` (og `nomic-embed-text` er allerede hentet — klar til fase 2.2)
- [x] Test at den svarer — verificeret gennem `POST /chat`
- [x] Installér Docker Desktop (bruges til Postgres/pgvector fra fase 2)

> **Fælde: to Ollama-servere på port 11434 (løst).** Der kørte en Ollama i Docker fra et tidligere
> projekt. Docker publicerer på `[::]`, mens Windows-installationen binder `127.0.0.1` — begge på
> port 11434. `localhost` slår op som IPv6 og ramte derfor Docker-containeren, som har andre modeller.
> Begge servere havde `nomic-embed-text`, så fejlen ville have været usynlig i fase 2: indeksering og
> søgning mod hver sin udgave af embedding-modellen giver ingen fejl, kun dårligere søgeresultater.
>
> Derfor er den gamle stak stoppet (`ollama`, `qdrant`, `chatbot-backend`, `chatbot-frontend`) —
> stoppet, ikke slettet, og volumes er urørte. Hentes tilbage med
> `docker start ollama qdrant chatbot-backend chatbot-frontend`. Konfigurationen peger fortsat
> eksplicit på `127.0.0.1` og ikke `localhost`, så det ikke kan gå galt igen.
>
> Sidegevinst: port 6333/6334 er ledige til fase 2's egen Qdrant.

**1.2 Projektopsætning**
- [x] Opret et ASP.NET Core Web API-projekt (`dotnet new webapi`)
- [x] Opret en solution med to projekter: `Chatbot.Api` og `Chatbot.Core` (interfaces og domænelogik — så forbliver API'et tyndt) — plus `tests/Chatbot.Tests`, så CI'ens `dotnet test` har noget at køre
- [x] Installér NuGet-pakker: ~~`Microsoft.SemanticKernel`~~ `Microsoft.Extensions.AI` + `OllamaSharp` (se note nedenfor)
- [x] Sæt Git-repo op med `.gitignore` fra dag ét

**1.3 Første chat**
- [x] Registrér `IChatClient` mod Ollama (`http://localhost:11434`) i DI
- [x] Lav et `POST /chat`-endpoint, der tager en besked og returnerer modellens svar
- [x] Tilføj chathistorik: gem samtalens beskeder (i hukommelsen er fint til at starte med) og send dem med i hvert kald
- [x] Skriv en simpel systemprompt ("Du er en hjælpsom assistent for...") og læs den fra en konfigurationsfil, så den er nem at justere

**1.4 Afprøvning**
- [x] Test via Swagger/curl: stil 3-4 opfølgende spørgsmål og bekræft, at botten husker konteksten — 4 ture kørt mod llama3.1: botten gengav navn og afdeling fra tur 1 og kunne referere til "punkt nummer to" fra sit eget tidligere svar

> **Pakkevalg:** `Microsoft.SemanticKernel.Connectors.Ollama` er stadig alpha og kræver `#pragma`-undertrykkelse
> af SKEXP-advarsler. Vi bruger i stedet `Microsoft.Extensions.AI` (den abstraktion Semantic Kernel selv bygger
> på) med `OllamaSharp` som connector. Adgangen sker gennem `IChatClient`, som er den samme grænseflade uanset
> udbyder — Semantic Kernel kan lægges ovenpå i fase 3, hvis dets planner/tool-features viser sig at være nødvendige.

**Leverance:** Du kan chatte med botten lokalt — uden tools og uden RAG.

### Fase 2 — RAG-kernen
**Mål:** Botten kan svare ud fra dokumentation.

**2.1 Vektor-database**
- [x] Beslut: Qdrant eller pgvector → **pgvector**, fordi Postgres alligevel skal bruges til tool-registry (fase 4) og chathistorik (fase 5) — én database til det hele
- [x] Start databasen i Docker og gem opsætningen i en `docker-compose.yml` (`pgvector/pgvector:pg17`, bundet til `127.0.0.1:5432`)
- [x] Definér `IVectorStore` i `Chatbot.Core` (`EnsureCreatedAsync`, `ClearAsync`, `UpsertAsync`, `SearchAsync`, `CountAsync`) og implementér det i det nye projekt `Chatbot.Infrastructure` (`PgVectorStore`, cosinus-lighed, HNSW-indeks)

**2.2 Embeddings**
- [x] Hent embedding-model: `ollama pull nomic-embed-text`
- [x] Registrér `IEmbeddingGenerator` i DI (OllamaSharp, samme mønster som `IChatClient`)
- [x] Test: embedding af en teststreng gav 768 dimensioner — `Rag:Embedding:Dimensions` er sat til 768, og ingestion fejler tydeligt, hvis modellen giver noget andet

**2.3 Ingestion-pipeline**
- [x] Saml 5-10 dokumenter som testdata → 7 **fiktive** dokumenter i `data/dokumentation` (personalehåndbog, medarbejderoprettelse, IT-adgange, udgifter, onboarding, fratrædelse, hjemmearbejde). Skal erstattes med rigtig dokumentation, når den er til rådighed
- [x] Byg indlæsning af dokumenter (`FileDocumentLoader`: Markdown/tekst, rekursivt)
- [x] Implementér chunking (`TextChunker`: del ved overskrifter, derefter ~2000 tegn ≈ 500 tokens med 200 tegns overlap, klip ved afsnit/sætning)
- [x] Gem metadata pr. chunk (kildedokument, overskrift, løbenummer) — returneres som `sources` i chat-svaret
- [x] Pak det hele som et genkørbart job → `POST /ingest` tømmer og genopbygger indekset (7 dokumenter → 45 chunks på 3 s)

**2.4 Søgning som tool**
- [x] Implementér `soeg_i_dokumentation`: spørgsmål → embedding → top 5 chunks over `MinScore`
- [x] Registrér det som funktion → `AIFunctionFactory.Create` via `IChatToolProvider`, udført af `UseFunctionInvocation()` i `IChatClient`-pipelinen (Microsoft.Extensions.AI i stedet for Semantic Kernel, jf. fase 1)
- [x] Justér systemprompten: svar ud fra uddragene, henvis til kilden, sig ærligt når dokumentationen ikke dækker

**2.5 Evaluering**
- [x] Skriv evalueringssættet: 20 spørgsmål med facit i `docs/evaluering/evalueringssaet.json` (16 med svar, 2 uden dækning, 2 almen viden) og et værktøj til at køre det: `tools/Chatbot.Eval`
- [x] Kør sættet og notér resultatet → **16/20 = 80 %** ([baseline-rapport](docs/evaluering/resultater/2026-09-09-baseline.md))

> **Fund under evaluering:** Ren vektorsøgning returnerer *altid* de nærmeste chunks — også når de
> ikke er relevante. "Hvad er reglerne for firmabil?" gav fem uddrag om firma*kort* (lighed ~0,57),
> og modellen digtede et svar ud fra dem. Derfor `MinScore = 0.6`: hits under grænsen kasseres, og
> toolet siger eksplicit, at intet blev fundet. Relevante hits lå på 0,68-0,80, støj på 0,50-0,65 —
> båndet er smalt, og grænsen skal justeres pr. embedding-model. De fire fejl i baseline fordeler sig
> på model (2), retrieval (1) og prompt (1); ingen ligger i pipelinens kode. Analysen står i rapporten.

👉 Gennemgang af koden og begrundelserne: [docs/FASE-2-FORKLARET.md](docs/FASE-2-FORKLARET.md)

**Leverance:** "Hvordan opretter jeg en medarbejder?" besvares korrekt ud fra dokumentationen — verificeret: modellen kalder selv `soeg_i_dokumentation`, svarer ud fra `medarbejderoprettelse-i-personalenet.md` og henviser til afsnittet.

### Fase 3 — Statiske tools (proof of concept)
**Mål:** Botten kan udføre én handling.

**3.1 Test-API**
- [x] Lav et lille dummy-API → `src/Chatbot.DummyHr` ("PersonaleNet" i hukommelsen på port 5100): `POST /employees` (400 ved manglende felter, 409 ved dublet-e-mail), `GET /employees?name=`, `DELETE /employees` til nulstilling. Separat projekt, fordi det er sådan rigtige tools ser ud i fase 4: eksterne systemer bag HTTP

**3.2 Function calling**
- [x] Hardcod 1-2 tools → `EmployeeTools` med `opret_medarbejder` (skrive-tool, kræver bekræftelse) og `find_medarbejder` (læse-tool, udføres straks). Samme `IChatToolProvider` som fase 2's søgning — `ChatService` er uændret
- [x] Automatisk function calling er slået til (`UseFunctionInvocation`), og modellen kalder toolet på "Opret Mette Nielsen, mette.nielsen@firma.dk, Konsulent i Salg, start 1. november 2026"
- [x] Grænsetilfælde: "Opret en medarbejder der hedder Lars Hansen" → botten spørger om e-mail, afdeling, stilling og startdato og forbereder intet. Toolet validerer selv og svarer modellen "mangler: … Gæt ikke"

**3.3 Bekræftelses-flow**
- [x] To-trins-flow: toolet gemmer en `PendingAction` pr. samtale og opretter *intet*; modellen viser opsummeringen; brugerens "ja" tolkes af `ConfirmationParser` (deterministisk, ikke et modelkald) og udføres af `IActionExecutor` uden at spørge modellen. Forslag udløber efter 30 min, og de slettes *før* udførelsen, så et dobbelt "ja" ikke opretter to gange
- [x] Afvisning: "nej" annullerer med fast svar. Uklare svar ("e-mailen skal være …") går til modellen med en systemnote om det ventende forslag, så den kalder toolet igen med rettede oplysninger — verificeret: rettet e-mail endte i HR-dummy'en

**3.4 Kombination af viden og handling**
- [x] Fuldt flow testet: "Hvordan opretter jeg en ny medarbejder?" → søger i dokumentationen og forklarer med kilde (kalder *ikke* opret_medarbejder) → "Ja tak, opret Peter Jensen …" → forslag venter på bekræftelse. Botten tilbyder endnu ikke af sig selv at udføre handlingen — det er fase 5.3
- [x] Evalueringssættet udvidet med 6 tool-scenarier (T01-T06). Værktøjet kan nu køre flertrins-samtaler (`turns`) og tjekke `expectedPending`, og det nulstiller HR-dummy'en før kørslen → [rapport](docs/evaluering/resultater/2026-09-10-fase-3.md)

> **Fund under fase 3:** OllamaSharps HttpClient har en indbygget timeout på 100 s. Fire parallelle
> samtaler mod én lokal model står i kø, og de bagerste ramte grænsen med HTTP 500. Timeouten er nu
> konfigurerbar (`Chatbot:Ollama:TimeoutSeconds`, 300), og et udløb giver 504 med forklaring.
> En lokal model er én kø, ikke en skalerbar tjeneste — det er også et argument i cloud-beslutningen.

👉 Gennemgang af koden og begrundelserne: [docs/FASE-3-FORKLARET.md](docs/FASE-3-FORKLARET.md)

**Leverance:** Botten kan både forklare og udføre en opgave i samme samtale — verificeret mod llama3.1 og HR-dummy'en.
**Evaluering: 23/26 = 88 %** (tool-scenarier 6/6, fase 2-delen 17/20). De tre fejl er alle i RAG-delen; to af dem er
llama3.1, der svarer med et rå JSON-objekt, når den har tre tools — afværget med `ReplySanitizer`, men det er det
første konkrete tegn på, at en lokal 8B-model bliver upålidelig med flere tools. Test med cloud-model før fase 4.

### Fase 4 — Dynamisk tool-registry + MCP
**Mål:** Tools uden kodeændringer.

**4.1 Datamodel**
- [x] Design tool-tabellen → `tools` i samme Postgres som vektor-indekset: `name`, `description`, `parameters_schema` (jsonb), `handler_type` (`internal`/`http`/`mcp`), `handler_config` (jsonb), `requires_confirmation`, `summary_template`, `is_active`. Oprettes af `PgToolRegistry.EnsureCreatedAsync` ved opstart — SQL'en dér er skemaet
- [x] Tilføj rettighedskoblingen → `roles` + `tool_roles`. Et tool uden rolle-rækker kan *ingen* bruge (bevidst: en glemt tildeling skal give "mangler", ikke "alle må"). Seedet med rollerne `medarbejder` og `hr`
- [x] Migrér de hardcodede tools fra fase 3 ind som rækker → `ToolSeed` indsætter dem første gang, registret er tomt: `soeg_i_dokumentation` (intern handler), `find_medarbejder` og `opret_medarbejder` (HTTP-rækker med body- og svar-skabeloner). `EmployeeTools`/`IEmployeeService`/`HttpEmployeeService` er slettet

**4.2 Runtime-loader**
- [x] Byg en loader, der oversætter en tool-definition til en funktion ved runtime → `RegistryFunction : AIFunction` (M.E.AI, jf. fase 1 — ikke Semantic Kernel): navn/beskrivelse/JSON-skema kommer fra rækken, argumenter uden for skemaet fjernes, og `requires_confirmation` giver fase 3's bekræftelses-flow generisk. Handler pr. type: `HttpToolHandler` (metode, URL med `{pladsholdere}`, body-skabelon, `successMessage`/`itemTemplate`/`emptyMessage`/`errorMessage`), `InternalToolHandler`, `McpToolHandler`
- [x] Filtrér på brugerens rettigheder → `DynamicToolProvider` læser kun tools, turens roller har adgang til (filtret er i SQL), og `DynamicActionExecutor` tjekker igen ved "ja". Roller kommer fra headeren `X-Roles` eller `Tools:DefaultRoles` — en påstand indtil fase 5.1. Verificeret: som `medarbejder` får modellen 6 tools uden `opret_medarbejder`, og intet forslag opstår
- [x] Test: nyt tool via `PUT /tools/hr_systemstatus` (GET mod HR-dummy'ens `/health`) mens API'et kørte → modellen kaldte det i næste tur. Admin-endpoints: `/tools`, `/roles`, `/mcp-servers` med validering (`ToolDefinitionValidator` + handlerens `Validate`)

**4.3 MCP-integration**
- [x] Installér `ModelContextProtocol` 2.2.0 (officielt C#-SDK; `McpClient`, `StdioClientTransport`/`HttpClientTransport`)
- [x] Tilslut en eksisterende MCP-server → `@modelcontextprotocol/server-filesystem` via `npx` (stdio) mod `data/dokumentation`, registreret som række i `mcp_servers`. 14 tools listet på ~2 s; "Hvilke filer ligger i mappen …?" → modellen kalder `filer_list_directory`, og botten lister de syv dokumenter
- [x] Map MCP-serverens tools ind i samme registry-model → `POST /mcp-servers/{navn}/import` laver tool-rækker (`filer_list_directory` …) med serverens beskrivelse og skema, `handler_type = mcp` og de angivne roller. Derefter er de almindelige rækker: roller, aktiv/inaktiv og bekræftelse styres som for alt andet. Kun tre læse-tools importeret; skrive-tools skal importeres med `requiresConfirmation: true`
- [x] Overvej: skal jeres egne systemer wrappes som MCP-servere? → Anbefaling i [FASE-4-FORKLARET](docs/FASE-4-FORKLARET.md) §5: HTTP-rækker til eksisterende REST-API'er nu (én PUT, ingen ny proces); MCP når flere AI-klienter skal dele et system, eller tool-sættet ændrer sig så ofte, at "serveren beskriver sig selv" sparer vedligehold

**4.4 Skalering**
- [ ] Hvis tool-antallet vokser (50+): indeksér tool-beskrivelserne i vektor-databasen og filtrér semantisk, før tools gives til modellen → ikke relevant med 7 tools; mekanikken er klar (registret leverer definitioner, loaderen bygger funktioner — filtret bliver et trin imellem)

> **Fund under fase 4:** (1) Får llama3.1 *ikke* et tool, den kender fra systemprompten, skriver den
> tool-kaldet som rå JSON i svaret (`{"name":"opret_medarbejder","parameters":{…}}`). Ufarligt —
> funktionen findes ikke, intet udføres — men prompten må ikke nævne dynamiske tools ved navn, og
> `ReplySanitizer` erstatter nu et opdigtet tool-kald med en forklaring. (2) Konfigurationsbinding
> *lægger til* et array med standardværdier: `DefaultRoles` blev `[medarbejder, hr, medarbejder, hr]`
> — rollerne normaliseres nu ét sted. (3) Mappen ligger i OneDrive, som ikke opdaterer mtime på
> redigerede filer; MSBuild ser derfor ændringer som "uændrede". Slet `obj/Debug` før build, når noget ikke giver mening.

👉 Gennemgang af koden og begrundelserne: [docs/FASE-4-FORKLARET.md](docs/FASE-4-FORKLARET.md)

**Leverance:** Et nyt tool kan tilføjes via en database-række (`PUT /tools/{navn}`) eller en MCP-server (`POST /mcp-servers/{navn}/import`) — uden deploy. Verificeret mod llama3.1, HR-dummy'en og filesystem-MCP-serveren. 112 enhedstests grønne.
**Evaluering: 24/28 = 86 %** med de tre seedede tools ([rapport](docs/evaluering/resultater/2026-09-14-fase-4.md)) og
**23/28 = 82 %** med alle 7 tools i registret ([rapport](docs/evaluering/resultater/2026-09-14-fase-4-7-tools.md)).
Tool-scenarier 6/6 og rettigheds-scenarier 2/2 i begge kørsler — migreringen til registret har ikke ændret handlings-adfærden.
Alle fejl er RAG-spørgsmål, hvor llama3.1 springer søgningen over; fire ekstra tools kostede ét svar mere. Hold antallet
af tools pr. rolle lavt, og test med en cloud-model før fase 5's prompt-finpudsning.

### Fase 5 — Hærdning og guidning
**Mål:** Klar til rigtige brugere.

**5.1 Sikkerhed og sporbarhed**
- [x] Kobl rettighedsstyringen på rigtig autentificering → API-nøgle i `X-Api-Key` (`ApiKeyAuthenticationHandler`, ASP.NET's claims-pipeline) koblet til bruger-id og roller i `Auth:ApiKeys`. Fallback-policy: alt kræver autentificering undtagen `/health`; `/tools`, `/roles`, `/mcp-servers`, `/ingest` og `/audit` kræver rollen `admin`. `X-Roles`-headeren er væk. Samtaler har en ejer — en anden brugers `conversationId` giver 403 (så ingen kan sige "ja" til andres forslag). En identitetsudbyder er én handler mere ved siden af
- [x] Byg audit-log → tabellen `audit_log` (`PgAuditLog`): bruger, roller, samtale, tool, `kind` (kaldt/forberedt/udført/annulleret/udløbet/afvist/fejlet), parametre (jsonb), resultat, succes, varighed. Skrives fra `RegistryFunction`, `DynamicActionExecutor` og `ChatService`; må aldrig vælte en tur. Opslag: `GET /audit?limit=&userId=` (admin)
- [x] Gennemgå prompt injection-scenarier → fiktivt testdokument `leverandoer-faq.md` med en "SYSTEMBESKED" der beder botten oprette en bruger. Forsvar i lag: uddrag og tool-svar indrammes som DATA (`<<<uddrag>>>`), SIKKERHED-afsnit i prompten, bekræftelse før udførelse, rolle-filtrering. Evalueringscase P01: botten svarede korrekt med kilde og forberedte intet. Injection via *tool-svar* er dækket af samme indramning, men ikke evalueret automatisk

**5.2 Robusthed**
- [x] Håndtér fejlslagne tool-kald → handlers svarede allerede med tekst (fase 4); nu fanger `RegistryFunction`/`DynamicActionExecutor` også uventede undtagelser: modellen får en forklaring med næste skridt, audit får `fejlet`, og efter et "ja" siger botten, at det er uvist, om noget blev ændret
- [x] Tilføj timeouts og retries → `Microsoft.Extensions.Http.Resilience` på tool-klienten med eksponentiel backoff, **kun for GET/HEAD/OPTIONS** (et POST der nåede frem må ikke gentages), `Tools:Registry:HttpRetries` = 2. Timeouts fandtes pr. tool, på Ollama og MCP
- [x] Flyt chathistorik til databasen → `PgConversationStore` (`conversations`, `messages`) og `PgPendingActionStore` (`pending_actions`). Samtaler og ventende handlinger overlever genstart; `MaxHistoryMessages` er et `LIMIT` i SQL. Verificeret: opfølgende spørgsmål i samme samtale husker konteksten

**5.3 Brugeroplevelse**
- [x] Finpuds systemprompten, så botten aktivt guider → første forsøg (et GUIDNING- og et SIKKERHED-afsnit) så rigtigt ud manuelt, men evalueringen faldt til 20/29 (69 %): llama3.1 sprang søgningen over på seks dokumentationsspørgsmål og opdigtede e-mail/afdeling/startdato på T01/T06 i stedet for at spørge ([rapport](docs/evaluering/resultater/2026-09-14-fase-5-lang-prompt.md)). Prompten er kortet ned igen til fase 4-formen med én sikkerhedssætning og "spørg først — kald ikke toolet endnu" som én sætning. Fundet førte til `ValidateArguments`: skemaets `required`/`format`/`pattern`/`minLength`/`enum` håndhæves, og pladsholdere (`[navn]`) og `@example.com` afvises før et forslag gemmes
- [x] Lad botten henvise til kilder → prompten beder om kilder i almindeligt sprog; verificeret: "Det står i personalehaandbogen under Ferie, at …"
- [ ] Test med 2-3 rigtige brugere → kan ikke automatiseres. Skabelon og fremgangsmåde i [docs/evaluering/brugertest.md](docs/evaluering/brugertest.md): giv dem en nøgle med deres rigtige rolle, skriv spørgsmålene ned ordret, før de fejlede ind i evalueringssættet før noget rettes

**5.4 Klar til skiftet**
- [x] Kør evalueringssættet en sidste gang på Ollama → 29 cases (P01 ny, R01/R02 med rigtige nøgler): [rapport](docs/evaluering/resultater/2026-09-14-fase-5.md). Det er baselinen, skiftet til cloud måles imod

> **Fund under fase 5:** (1) "Påkrævet og ikke tom" er ikke nok validering, når modellen selv udfylder
> formularen — pladsholdere og opdigtede eksempelværdier passerer. Skemaet i tool-rækken bestemmer nu
> reglerne, og bekræftelsen er stadig det lag, der holder. (2) Retries på skrivende HTTP-kald er en
> fejlkilde, ikke en robusthed. (3) Roslyns compiler-server cacher kildefiler på sti+tidsstempel; i en
> OneDrive-mappe uden mtime-opdatering kompilerer den gammelt indhold — byg med `-p:UseSharedCompilation=false`.

👉 Gennemgang af koden og begrundelserne: [docs/FASE-5-FORKLARET.md](docs/FASE-5-FORKLARET.md)

**Leverance:** En bot du tør give til andre — med nøgle, roller, ejerskab, audit og bekræftelse. 124 enhedstests grønne.

### Fase 6 — Frontend (Vue)
**Mål:** En brugerflade, medarbejdere kan bruge — i IST's udtryk.

**6.1 Fundament**
- [x] Vue 3 + Vite + TypeScript + Pinia + Vue Router i `frontend/` (`npm create vue@latest` med Vitest og ESLint/oxlint). Vite-proxy `/api` → `http://localhost:5022`, så udvikling kører uden CORS; produktion bygger API-adressen ind via `VITE_API_BASE`
- [x] Designtokens læst fra ist.com's tema (`frontend/src/styles/tokens.css`): mint `#c9f6dc`, mørk teal `#174655`, pasteller `#fff6d9`/`#ffd3ca`, sort navigation, pill-knapper (500px, 2px kant, 700), 20px-kort, 24px gap, 1200px maks. PP Object Sans/PP Editorial New er kommercielle → system-sans + Instrument Serif nu; `fonts.css` er klar til self-hostede filer i `public/fonts/` (gitignored)
- [x] Backend: `GET /me` (bruger-id, navn, roller for nøglen) og CORS-policy fra `Cors:AllowedOrigins` (kun `X-Api-Key` + `Content-Type`, kun konfigurerede origins; tom liste = ingen CORS)

**6.2 Login og chat**
- [x] Login med API-nøgle: valideres mod `/me`, gemmes i `sessionStorage` (overlever reload, ikke lukket fane); dev-genveje til de tre udviklingsnøgler kun i `import.meta.env.DEV`. Router-guard uden nøgle → `/login`; 401 fra API'et logger ud
- [x] Chat: beskedbobler, "skriver…"-indikator, kilder som foldbar liste ("personalehaandbog › Ferie · 0,73"), forslag på tom side, "Ny samtale", Enter sender / Shift+Enter ny linje, `aria-live` på svar
- [x] Bekræftelses-kort når `pendingAction` er sat: "Ja, udfør" / "Nej, annullér" sender præcis `ja`/`nej` (det `ConfirmationParser` genkender); feltet er stadig åbent til rettelser
- [x] Fejl: API'ets to fejlformer (`{error}` og ProblemDetails) normaliseres til én `ApiError`; 403 (andres samtale) nulstiller samtalen, 503/504/netværk giver fejlboble med "Prøv igen", som gensender uden dubletter

**6.3 Kvalitet**
- [x] 24 Vitest-tests (fejlnormalisering, auth- og chat-store inkl. ja/nej, retry og 401/403, komponenter), `vue-tsc` typecheck, ESLint + oxlint; nyt `frontend`-job i CI (node 24, `npm ci`, lint, typecheck, test, build); `deploy-frontend.yml` opdateret til node 24, `npm ci` og `VITE_API_BASE` fra repo-variablen `API_BASE_URL`
- [ ] Brugertest med rigtige brugere — samme skabelon som fase 5.3 ([docs/evaluering/brugertest.md](docs/evaluering/brugertest.md))

👉 Gennemgang af koden og begrundelserne: [docs/FASE-6-FORKLARET.md](docs/FASE-6-FORKLARET.md)

**Leverance:** `cd frontend && npm run dev` → log ind med en dev-nøgle → spørg, få svar med kilder, bed om en oprettelse og bekræft med ét klik.

---

## 💡 Kloge træk

- **Følg fasernes rækkefølge.** Mange starter med den dynamiske tool-motor (den er sjovest) og opdager så, at RAG-svarene er ubrugelige. RAG-kvalitet (chunking, retrieval) er dér, det meste tid går.
- **Lav et evalueringssæt fra dag ét:** 15-20 spørgsmål med facit. Kør dem ved hver ændring af chunking, prompt eller model. Sættet gør også skiftet fra Ollama til cloud målbart.
- **Abstrahér LLM- og vektor-db-adgang bag interfaces** (`IChatClient` / `IEmbeddingGenerator`). Så kan udbyder eller database skiftes uden at røre resten.
- **Behandl tool-beskrivelser som førsteklasses indhold.** Modellens evne til at vælge rigtigt tool afhænger næsten udelukkende af beskrivelsernes kvalitet. Skriv dem som til en ny kollega: hvornår skal toolet bruges — og hvornår ikke.
- **Log alt fra start:** Hele prompten, hentede chunks, tool-kald med parametre. Når botten svarer mærkeligt, er loggen den eneste vej til at forstå hvorfor.
- **Gem evalueringsresultaterne fra Ollama-perioden**, så beslutningen om hvornår der skiftes til cloud bliver datadrevet i stedet for mavefornemmelse.

---

## 🔁 Tjekpunkter

**Efter fase 2:** Er RAG-svarene gode på rigtig dokumentation?
- ✅ Grønt lys: Mindst 80 % af evalueringssættet besvares korrekt.
- ⛔ Rødt lys: Vage eller forkerte svar → arbejd med chunking og retrieval, før der bygges videre.

**Efter fase 3:** Vælger modellen pålideligt det rigtige tool, og føles bekræftelses-flowet naturligt?
- ⛔ Rødt lys: Den kalder tools, når den burde søge (eller omvendt) → justér tool-beskrivelser og systemprompt. Overvej om det er den lokale models begrænsning — test evt. med en cloud-model, før du omskriver noget.

**Efter fase 4:** Kan en anden end dig tilføje et tool uden din hjælp? Det er den egentlige test af "dynamisk".

---

## 🚀 Første skridt (næste 24-48 timer)

1. Installér Ollama og hent en model: `ollama pull llama3.1`
2. Opret ASP.NET Core-projektet med Semantic Kernel
3. Få den første chat til at svare lokalt
4. Læs dokumentationen for `ModelContextProtocol`-SDK'et

Det kan realistisk være kørende på en enkelt aften — og det er stærkt motiverende at have noget kørende fra dag ét.
