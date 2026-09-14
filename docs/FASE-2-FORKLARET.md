# Fase 2 forklaret — RAG-kernen: hvad koden gør, og hvorfor

Denne fil er gennemgangen af fase 2 i samme ånd som [FASE-1-FORKLARET.md](FASE-1-FORKLARET.md):
**hvad** hver del gør, men mest **hvorfor** — for det er begrundelserne, der skal kunne forsvares
til eksamen. Nederst står evalueringen, som er dét, der beviser, at det virker.

Læs den med koden åben ved siden af.

---

## 1. Den vigtigste indsigt: modellen ved intet om jeres virksomhed

I fase 1 lærte vi, at modellen ingen hukommelse har — vi sender hele samtalen med hver gang.
Fase 2 bygger på samme princip, bare med et nyt problem: modellen ved heller intet om *jeres*
dokumentation. Den har læst internettet, ikke personalehåndbogen. Spørger man "hvor mange
feriedage har jeg?", gætter den — og gætter overbevisende.

Løsningen har samme form som i fase 1: **vi lægger svaret på bordet, før vi spørger.** Vi finder
de afsnit i dokumentationen, der handler om spørgsmålet, og sender dem med i prompten. Så står
svaret ordret i den tekst, modellen læser. Det er hele idéen bag RAG (*Retrieval-Augmented
Generation*): hent først, generér bagefter.

Det rejser tre spørgsmål, og resten af koden er svarene på dem:

1. **Hvordan finder vi de rigtige afsnit** blandt hundredvis? → Embeddings og vektor-database (afsnit 2-3).
2. **Hvad er "et afsnit"?** Et dokument er for stort, en sætning for lille. → Chunking (afsnit 4).
3. **Hvornår skal der søges?** Ikke ved "hej", altid ved "hvordan opretter jeg…". → Søgning som tool (afsnit 5).

---

## 2. Embeddings: tekst som tal

En embedding-model gør en tekst til en liste af tal — en vektor. `nomic-embed-text` giver
768 tal for hver tekst, uanset om teksten er ét ord eller et helt afsnit. Det interessante er
egenskaben: **tekster med samme betydning får vektorer, der ligger tæt på hinanden.**
"Hvor mange feriedage har jeg?" og "Alle medarbejdere har 25 feriedage om året" ligner ikke
hinanden ord for ord, men deres vektorer gør.

Afstanden måles med *cosinus-lighed*: 1 = samme betydning, omkring 0 = urelateret. Det er det
tal, der står som `score` i svarene fra `/search` og `/chat`.

To konsekvenser, der forklarer konfigurationen:

- **Embeddings er ikke kompatible på tværs af modeller.** En vektor fra `nomic-embed-text`
  kan ikke sammenlignes med en fra en anden model. Skifter man model, skal *alt* embeddes igen.
  Derfor er ingestion bygget som ét job, der tømmer og genopbygger (afsnit 4), og derfor står
  `Dimensions` i konfigurationen: tabellen låses til 768, og ingestion stopper med en tydelig fejl,
  hvis modellen giver noget andet.
- **Både dokumenter og spørgsmål skal embeddes med samme model.** Se `DocumentSearchService`:
  spørgsmålet går gennem den samme `IEmbeddingGenerator` som chunks'ene gjorde under ingestion.

Registreringen i `Program.cs` følger fase 1-mønstret: `IEmbeddingGenerator` er et interface fra
`Microsoft.Extensions.AI`, og Ollama-implementeringen er det eneste sted, der ved, hvem der
leverer vektorerne. Skiftet til en cloud-model rører kun den linje.

---

## 3. Vektor-databasen: pgvector, og hvorfor ikke Qdrant

Vektorerne skal gemmes et sted, hvor man hurtigt kan spørge "hvilke af de 45 (eller 45.000)
vektorer ligger tættest på denne?". Det er en vektor-database.

Projektplanen gav valget mellem Qdrant og pgvector med reglen: *vælg pgvector, hvis du alligevel
skal have Postgres.* Det skal vi — fase 4 skal have tool-registry i en database, og fase 5 skal
have chathistorik. **Én database til det hele er én ting mindre at drive, sikkerhedskopiere og
forklare.** Derfor pgvector.

Prisen er, at Qdrant har et mere specialiseret .NET-bibliotek. Det er den pris, interfacet
`IVectorStore` er forsikringen mod: `IngestionService`, `DocumentSearchService` og alle tests
kender kun interfacet. `PgVectorStore` er den eneste fil med SQL i. Testene bruger en
`InMemoryVectorStore` på 60 linjer — som samtidig beviser, at abstraktionen holder.

Tre detaljer i `PgVectorStore`, der er værd at kunne forklare:

- **`embedding vector(768)`** — kolonnen har fast dimension. Postgres afviser en vektor med
  en anden længde. Det er det fysiske værn mod at blande to embedding-modeller.
- **`<=>` er cosinus-afstand**, og `1 - afstand` er cosinus-lighed. Vi sorterer efter afstand
  (`ORDER BY embedding <=> $1`) og returnerer ligheden, fordi den er lettere at læse.
- **HNSW-indekset** gør søgningen hurtig, når der er mange chunks. Med 45 chunks er det
  ligegyldigt; med 45.000 er det forskellen på millisekunder og sekunder. Det koster intet
  at have fra start.

Projektet `Chatbot.Infrastructure` er nyt. Det findes, fordi `Chatbot.Core` ikke skal kende
Npgsql: Core har interfaces og logik, Infrastructure har databaser og eksterne systemer, Api er
tyndt lim. Det er den lagdeling, der senere gør det muligt at teste alt uden Docker.

---

## 4. Chunking og ingestion: fra dokument til søgbare stykker

### Hvorfor chunks?

Man kunne embedde hele dokumenter. Men en embedding er ét punkt — den kan kun betyde én ting.
Personalehåndbogen handler om ferie *og* sygdom *og* pension *og* frokost; dens ene vektor
ligger et sted midt imellem og matcher intet spørgsmål godt. Omvendt er en enkelt sætning for
lidt: "Ferieåret løber fra 1. september" siger ikke, hvor mange dage man har.

Derfor **chunks**: stykker på et par hundrede ord, der handler om ét emne. Projektplanen siger
"~500 tokens" — det er de 2000 tegn i `ChunkSize` (ca. 4 tegn pr. token).

### Strategien i `TextChunker`

1. **Del ved overskrifter først.** Dokumentationen er Markdown, og forfatteren har allerede
   delt den i emner med `##`. Det er den bedste chunking, man kan få, for det er et menneskes
   vurdering af, hvor et emne starter og slutter. Bonus: hver chunk *kender* sin overskrift,
   og det er den metadata, botten bruger til "det står i personalehåndbogen, afsnit Ferie".
2. **Del lange afsnit i stykker med overlap.** Er et afsnit over 2000 tegn, klippes det —
   helst ved et afsnitsskift, ellers en sætning, aldrig midt i et ord. Overlappet på 200 tegn
   betyder, at en sætning, der lander på grænsen, kommer med i begge stykker i stedet for ingen.
3. **Gentag overskriften forrest i chunkens tekst.** Det ser redundant ud, men gør en målbar
   forskel for korte chunks: "Adgangskoder — Skiftes hver 180. dag" embeddes tættere på
   spørgsmålet "hvor ofte skal jeg skifte kode?" end "Skiftes hver 180. dag" alene.

Det er bevidst det simpleste, der kan virke. Evalueringssættet (afsnit 6) er det, der skal
afgøre, om det er godt nok, eller om chunking skal blive klogere.

### Ingestion som genkørbart job

`IngestionService.RunAsync` gør ét: **tøm alt og byg op forfra.** Ikke "opdatér de ændrede".
Det er bevidst, fordi de to ting, der oftest ændrer sig — embedding-model og chunk-strategi —
begge kræver fuld genopbygning, og fordi et job, der altid gør det samme, er nemmere at stole
på end et, der prøver at være smart. Med 7 dokumenter tager det 3 sekunder. Med 700 tager det
nogle minutter, og det er stadig fint for et job, man kører efter ændringer.

Jobbet ligger bag `POST /ingest`, så det kan køres uden adgang til serveren — det er "ét klik"
fra projektplanen.

---

## 5. Søgning som tool: modellen bestemmer selv, om den skal søge

Her er det arkitektoniske valg, der adskiller dette projekt fra en simpel RAG-tutorial.

Den naive løsning er: *søg altid.* Hver besked embeddes, top-5 chunks klistres ind i prompten,
modellen svarer. Det virker — men det spilder tid og kontekst på "hej" og "tak", det bliver
mærkeligt ved "hvad hedder jeg?" (hvorfor søger den i personalehåndbogen?), og vigtigst: **det
skalerer ikke til fase 3-4.** Når botten også skal kunne *oprette* en medarbejder, skal noget
beslutte, om et spørgsmål kræver viden, handling eller begge dele. Det "noget" er modellen selv.

Derfor er søgningen et **tool** — en funktion, modellen får beskrevet og selv kan vælge at kalde:

```
Bruger:   Hvordan opretter jeg en ny medarbejder?
Model:    [kalder soeg_i_dokumentation("oprettelse af ny medarbejder")]
System:   [kører søgningen, sender 5 uddrag tilbage til modellen]
Model:    Kun HR-administratorer kan oprette i PersonaleNet ... [Kilde: ...]
```

Brugeren ser kun sidste linje. Loopet i midten kører `UseFunctionInvocation()` i
`Program.cs`: den lægger sig som et lag omkring `IChatClient`, fanger modellens ønske om at
kalde en funktion, kører den og sender resultatet tilbage, indtil modellen svarer med tekst.
`ChatService` ved intet om det — den giver bare `Tools` med i kaldet.

Tre ting at kunne forklare:

- **Beskrivelsen er koden.** `[Description(...)]` på `DocumentSearchTool.SearchAsync` er den
  eneste information, modellen har, når den beslutter om den skal søge. Den er skrevet som til
  en ny kollega: *hvornår* ("hver gang brugeren spørger om virksomhedens regler…, også når du
  tror, du kender svaret") og *hvornår ikke* ("ikke til småsnak eller almen viden"). Svarer
  botten dårligt, fordi den ikke søgte, er det beskrivelsen — ikke C#-koden — der skal rettes.
- **Systemprompten bakker op.** Den siger det samme som tool-beskrivelsen og tilføjer den
  vigtigste regel: *svar udelukkende ud fra uddragene, og sig ærligt, hvis de ikke dækker.*
  Uden den sætning fylder modellen hullerne med gæt.
- **`IChatToolProvider` er fremtidssikringen.** I fase 2 er der én provider med ét tool.
  I fase 3 kommer "opret medarbejder" til som endnu en provider. I fase 4 erstattes de af en
  provider, der læser tool-definitioner fra databasen og filtrerer på brugerens rettigheder.
  `ChatService` ændres ikke.

Hvorfor virker det med en lokal 8B-model? Fordi llama3.1 er trænet til function calling, og
fordi der kun er ét tool at vælge imellem. Med 10 tools bliver det sværere, og det er præcis
det, tjekpunktet efter fase 3 handler om.

### Kilder i svaret

`RetrievalContext` er en lille liste, der lever i ét request (*scoped*). Toolet lægger de fundne
chunks i den, og `ChatService` læser den ud til `Sources` i svaret. Det giver to ting: brugeren
kan se, *hvad* botten byggede svaret på, og evalueringen kan tjekke, at det var det *rigtige*
dokument — ikke bare at svaret indeholdt de rigtige ord.

---

## 6. Evaluering: baseline og MinScore

### Hvorfor et evalueringssæt?

Projektplanen siger: lav det fra dag ét. Grunden er, at RAG-kvalitet ikke kan mærkes — den skal
måles. Man kan stille tre spørgsmål i Swagger, få tre gode svar og tro, det virker. Sættet på
20 spørgsmål med facit ([evalueringssaet.json](evaluering/evalueringssaet.json)) stiller de samme spørgsmål
hver gang, på samme måde, og giver ét tal. Ændrer man chunking, prompt eller model, kører man
det igen og ser, om tallet gik op eller ned. Det er også sådan, skiftet fra Ollama til en
cloud-model bliver en beslutning frem for en fornemmelse.

Sættet er bevidst ikke kun "spørgsmål med svar i dokumentationen". Det har også:

- **Spørgsmål uden dækning** (firmabil). Det rigtige svar er "det står ikke i dokumentationen".
  Det er den sværeste test, for modellen *vil* svare.
- **Almen viden** (hovedstaden i Frankrig). Botten skal ikke afvise alt, der ikke er i
  personalehåndbogen — og den skal helst ikke søge først.

Bedømmelsen i `tools/Chatbot.Eval` er grov: forventede nøgleord skal være i svaret, forbudte
må ikke være det, og den forventede kilde skal være slået op. Den kan tage fejl begge veje —
et rigtigt svar med andre ord fejler, et forkert svar med de rigtige tal består. Men den er
*konsistent*, og det er det, en baseline skal være. Læs altid de faktiske svar i rapporten,
ikke kun procenten.

### Det første fund: MinScore

Den første manuelle test afslørede det klassiske RAG-problem. "Hvad er reglerne for firmabil?"
gav fem uddrag om firma*kort* og kørselsgodtgørelse med lighed omkring 0,57 — og modellen
skrev pligtskyldigt et svar om firmabil ud fra dem. Søgningen returnerer *altid* de nærmeste
fem; den siger ikke, om de nærmeste er tæt nok.

Rigtige svar lå på 0,70-0,80. Støj lå på 0,50-0,58. Derfor `MinScore = 0.6`: hits under
grænsen kasseres, toolet svarer "ingen relevante uddrag fundet", og modellen får eksplicit
besked om at sige det ærligt. Efter ændringen: "Søgning 'reglerne for firmabil': 5 hits,
0 over MinScore=0.6."

Grænsen er en knap, ikke en sandhed. Den afhænger af embedding-modellen (en anden model giver
en anden fordeling af scores) og af dokumentationen. Det er dét, evalueringssættet skal bruges
til at justere — og hvorfor den er konfiguration, ikke en konstant.

### Baseline

Resultatet af den første kørsel står i [resultater/2026-09-09-baseline.md](evaluering/resultater/2026-09-09-baseline.md)
med alle spørgsmål, svar og kilder. Opsætningen var:

| Knap | Værdi |
| --- | --- |
| Chatmodel | llama3.1 (8B, Q4_K_M) via Ollama |
| Embedding-model | nomic-embed-text (768 dim) |
| ChunkSize / ChunkOverlap | 2000 / 200 tegn |
| TopK | 5 |
| MinScore | 0,6 |
| Dokumenter | 7 fiktive dokumenter, 45 chunks |

**Resultat: 16 af 20 korrekte = 80 %.** Tjekpunktet efter fase 2 er 80 %, så det er grønt lys —
lige akkurat. De fire fejl er analyseret i rapporten og fordeler sig sådan:

| Type | Antal | Eksempel |
| --- | --- | --- |
| Modellen læste forkert | 2 | Q05: skrev "5 feriedage" for "5 feriefridage" trods rigtigt uddrag |
| Retrieval fandt ikke afsnittet | 1 | Q17: "prøvetid" kom ikke i top 5 — botten sagde ærligt, at den ikke kunne finde det |
| Prompten var for hård | 1 | Q20: botten søgte efter "hovedstaden i Frankrig" og afviste at svare |

Det vigtige er, at **ingen af fejlene ligger i pipelinens kode** — de ligger i modellen, i
retrieval-kvaliteten og i prompten. Det er præcis de tre ting, evalueringssættet er lavet til at
skrue på. Q20 afslørede desuden, at støj kan score op til 0,65, så båndet mellem relevant
(0,68-0,80) og irrelevant er smallere end den første manuelle test antydede.

Næste iteration bør starte med det gratis: systemprompten (så almen viden ikke afvises) og
MinScore/TopK. Først derefter chunking og hybrid-søgning.

---

## 7. Hvad der bevidst *ikke* er gjort

- **Ingen PDF/Word-indlæsning.** `IDocumentLoader` er klar til det, men Markdown er nok til at
  bevise pipelinen. Rigtige dokumenter kommer, når projektet møder rigtig dokumentation.
- **Ingen hybrid-søgning (nøgleord + vektor).** Rene vektorsøgninger er svage på præcise
  termer som "Regnskab360" eller "lokal 4000". Postgres kan fuldtekstsøgning, så det er en
  naturlig næste forbedring — *hvis* evalueringen viser, at det er der, fejlene ligger.
- **Ingen re-ranking eller query-omskrivning.** Begge kan løfte kvaliteten, begge koster
  et ekstra modelkald. Først når baseline viser behovet.
- **Tool-kald gemmes ikke i historikken.** Kun spørgsmål og endeligt svar. Uddragene kan være
  store, og modellen kan hente dem igen, hvis den får brug for dem. Det holder prompten lille.

Reglen bag alle fire: mål først, byg bagefter. Det er projektplanens "kloge træk" i praksis.

---

## 8. Sådan afprøver du det selv

```bash
docker compose up -d                      # Postgres + pgvector
dotnet run --project src/Chatbot.Api      # http://localhost:5022
```

Kør derefter i [Chatbot.Api.http](../src/Chatbot.Api/Chatbot.Api.http) eller med curl:

```bash
curl -X POST http://localhost:5022/ingest
curl "http://localhost:5022/search?q=feriedage"
curl -X POST http://localhost:5022/chat -H "Content-Type: application/json" \
  -d '{"message":"Hvordan opretter jeg en ny medarbejder?"}'
```

Og evalueringen:

```bash
dotnet run --project tools/Chatbot.Eval -- docs/evaluering/evalueringssaet.json docs/evaluering/resultater/$(date +%F)-min-aendring.md
```

Fejlsøgning i rækkefølge: (1) Er indekset tomt? Loggen siger det ved opstart. (2) Fik botten de
rigtige chunks? `GET /search` viser præcis det, den fik. (3) Søgte den overhovedet? Loggen viser
"Søgning …" for hvert tool-kald — mangler linjen, valgte modellen ikke at søge, og så er det
tool-beskrivelsen eller systemprompten, der skal justeres.
