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
*Modellen vælger rigtigt mellem søg og handl i de testede scenarier; se evalueringen for tallene.*

### Fase 4 — Dynamisk tool-registry + MCP
**Mål:** Tools uden kodeændringer.

**4.1 Datamodel**
- [ ] Design tool-tabellen: navn, beskrivelse, JSON-skema for parametre, handler-type (HTTP/MCP/intern), endpoint-konfiguration, aktiv/inaktiv
- [ ] Tilføj rettighedskoblingen: tabeller for roller og tool-rolle-tildelinger, så ikke alle kan benytte alle tools
- [ ] Migrér de hardcodede tools fra fase 3 ind som rækker i tabellen

**4.2 Runtime-loader**
- [ ] Byg en loader, der oversætter en tool-definition til en Semantic Kernel-funktion ved runtime (JSON-skema → parametre, handler-konfiguration → HTTP-kald)
- [ ] Filtrér på brugerens rettigheder: kun tilladte tools loades ind i samtalen, så modellen slet ikke kan se resten
- [ ] Test: tilføj et nyt tool udelukkende via en database-række og bekræft, at botten kan bruge det uden genstart/deploy

**4.3 MCP-integration**
- [ ] Installér `ModelContextProtocol`-pakken fra NuGet
- [ ] Tilslut en eksisterende MCP-server som første test (f.eks. en filesystem- eller test-server)
- [ ] Map MCP-serverens tools ind i samme registry-model, inkl. rettigheder
- [ ] Overvej: skal jeres egne systemer wrappes som MCP-servere fremadrettet?

**4.4 Skalering**
- [ ] Hvis tool-antallet vokser (50+): indeksér tool-beskrivelserne i vektor-databasen og filtrér semantisk, før tools gives til modellen

**Leverance:** Et nyt tool kan tilføjes via en database-række eller en MCP-server — uden deploy.

### Fase 5 — Hærdning og guidning
**Mål:** Klar til rigtige brugere.

**5.1 Sikkerhed og sporbarhed**
- [ ] Kobl rettighedsstyringen på rigtig autentificering (hvem er brugeren, hvilke roller har vedkommende?)
- [ ] Byg audit-log: hvert tool-kald logges med bruger, tidspunkt, parametre og resultat
- [ ] Gennemgå prompt injection-scenarier: kan indhold fra dokumentationen eller tool-svar narre botten til uønskede handlinger?

**5.2 Robusthed**
- [ ] Håndtér fejlslagne tool-kald: botten skal forklare, hvad der gik galt, og foreslå næste skridt — ikke bare fejle stille
- [ ] Tilføj timeouts og retries på eksterne kald
- [ ] Flyt chathistorik fra hukommelse til databasen, så samtaler overlever genstart

**5.3 Brugeroplevelse**
- [ ] Finpuds systemprompten, så botten aktivt guider ("Det lyder som om du leder efter X — vil du have, at jeg gør det for dig?")
- [ ] Lad botten henvise til kilder ("Det står beskrevet i personalehåndbogen, afsnit 3")
- [ ] Test med 2-3 rigtige brugere og saml deres spørgsmål ind — de spørger anderledes, end du forventer

**5.4 Klar til skiftet**
- [ ] Kør evalueringssættet en sidste gang på Ollama og gem resultatet, så skiftet til cloud-API kan måles

**Leverance:** En bot du tør give til andre.

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
