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
- [ ] Installér Ollama fra ollama.com
- [ ] Hent en chatmodel: `ollama pull llama3.1`
- [ ] Test at den svarer i terminalen: `ollama run llama3.1`
- [ ] Installér Docker Desktop (skal bruges til vektor-databasen i fase 2)

**1.2 Projektopsætning**
- [ ] Opret et ASP.NET Core Web API-projekt (`dotnet new webapi`)
- [ ] Opret en solution med to projekter: `Chatbot.Api` og `Chatbot.Core` (interfaces og domænelogik — så forbliver API'et tyndt)
- [ ] Installér NuGet-pakker: `Microsoft.SemanticKernel` og Ollama-connectoren
- [ ] Sæt Git-repo op med `.gitignore` fra dag ét

**1.3 Første chat**
- [ ] Registrér `IChatClient` mod Ollama (`http://localhost:11434`) i DI
- [ ] Lav et `POST /chat`-endpoint, der tager en besked og returnerer modellens svar
- [ ] Tilføj chathistorik: gem samtalens beskeder (i hukommelsen er fint til at starte med) og send dem med i hvert kald
- [ ] Skriv en simpel systemprompt ("Du er en hjælpsom assistent for...") og læs den fra en konfigurationsfil, så den er nem at justere

**1.4 Afprøvning**
- [ ] Test via Swagger/curl: stil 3-4 opfølgende spørgsmål og bekræft, at botten husker konteksten

**Leverance:** Du kan chatte med botten lokalt — uden tools og uden RAG.

### Fase 2 — RAG-kernen
**Mål:** Botten kan svare ud fra dokumentation.

**2.1 Vektor-database**
- [ ] Beslut: Qdrant eller pgvector (vælg pgvector, hvis du alligevel vil have Postgres til tool-registry og chathistorik)
- [ ] Start databasen i Docker og gem opsætningen i en `docker-compose.yml`
- [ ] Definér et interface i `Chatbot.Core` (f.eks. `IVectorStore` med `UpsertAsync` og `SearchAsync`), og implementér det mod den valgte database

**2.2 Embeddings**
- [ ] Hent embedding-model: `ollama pull nomic-embed-text`
- [ ] Registrér `IEmbeddingGenerator` i DI
- [ ] Test: generér en embedding for en teststreng og bekræft dimensionen

**2.3 Ingestion-pipeline**
- [ ] Saml 5-10 rigtige dokumenter fra jeres dokumentation som testdata
- [ ] Byg indlæsning af dokumenter (start med Markdown/tekst; PDF og Word kan komme senere)
- [ ] Implementér chunking: start simpelt med ~500 tokens pr. chunk og lidt overlap — finjustér senere ud fra evalueringssættet
- [ ] Gem metadata pr. chunk (kildedokument, afsnit/overskrift), så botten kan henvise til kilden
- [ ] Pak det hele som et genkørbart job (konsol-kommando eller endpoint), der tømmer og genopbygger indekset

**2.4 Søgning som tool**
- [ ] Implementér `søg_i_dokumentation`: tag brugerens spørgsmål → embedding → hent top 3-5 chunks
- [ ] Registrér det som Semantic Kernel-funktion med en god beskrivelse
- [ ] Justér systemprompten: botten skal svare ud fra de hentede chunks og sige det ærligt, hvis dokumentationen ikke dækker spørgsmålet

**2.5 Evaluering**
- [ ] Skriv evalueringssættet: 15-20 spørgsmål med facit
- [ ] Kør sættet og notér resultatet — det er din baseline fremover

**Leverance:** "Hvordan opretter jeg en medarbejder?" besvares korrekt ud fra dokumentationen.

### Fase 3 — Statiske tools (proof of concept)
**Mål:** Botten kan udføre én handling.

**3.1 Test-API**
- [ ] Lav et lille dummy-API (eller brug et testmiljø af det rigtige system) med f.eks. et "opret medarbejder"-endpoint — så kan du eksperimentere uden risiko

**3.2 Function calling**
- [ ] Hardcod 1-2 tools som Semantic Kernel-funktioner (navn, beskrivelse, typede parametre)
- [ ] Slå automatisk function calling til og verificér, at modellen kalder toolet på "opret en medarbejder der hedder Lars"
- [ ] Test grænsetilfælde: stiller botten opklarende spørgsmål, når der mangler oplysninger (f.eks. e-mail)?

**3.3 Bekræftelses-flow**
- [ ] Byg et to-trins-flow: botten opsummerer handlingen ("Jeg opretter nu Lars Hansen med e-mail x — bekræft?") og udfører først ved brugerens ja
- [ ] Håndtér afvisning: brugeren skal kunne rette oplysningerne i stedet for at starte forfra

**3.4 Kombination af viden og handling**
- [ ] Test det fulde flow: "Hvordan opretter jeg en medarbejder?" → botten søger i dokumentationen, forklarer, og tilbyder at udføre det
- [ ] Udvid evalueringssættet med 5 tool-scenarier

**Leverance:** Botten kan både forklare og udføre en opgave i samme samtale.
*Bemærk: Hvis den lokale model vælger tools upålideligt, kan dette være tidspunktet at skifte til en cloud-API.*

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
