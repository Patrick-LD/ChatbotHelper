# Fase 4 forklaret — dynamisk tool-registry og MCP: tools uden kodeændringer

Denne fil er gennemgangen af fase 4 i samme ånd som [FASE-1](FASE-1-FORKLARET.md),
[FASE-2](FASE-2-FORKLARET.md) og [FASE-3](FASE-3-FORKLARET.md): **hvad** koden gør, men mest
**hvorfor**. Fase 4 er den fase, hvor tools holder op med at være klasser i koden og bliver rækker
i databasen — og hvor "hvem må hvad" for første gang bliver en tabel og ikke en hensigt.

Læs den med koden åben ved siden af.

---

## 1. Den vigtigste indsigt: et tool er data plus en handler-type

I fase 3 var `opret_medarbejder` en C#-metode med `[Description]`-attributter. Det virkede, men
det betød, at hvert nyt tool krævede kode, build og deploy — og at kun en udvikler kunne tilføje et.
Projektplanens test af fase 4 er kontant: *"Kan en anden end dig tilføje et tool uden din hjælp?"*

Svaret bygger på én adskillelse. Alt, modellen skal vide om et tool, er **data**:

| Kolonne | Hvad modellen bruger den til |
| --- | --- |
| `name` | Funktionsnavnet den kalder |
| `description` | Teksten den vælger toolet ud fra (stadig "koden", jf. fase 2) |
| `parameters_schema` | JSON-skemaet for argumenterne — sendes uændret til modellen |

Og alt, *systemet* skal vide for at udføre kaldet, er en **handler-type** plus dens konfiguration:

| `handler_type` | `handler_config` | Hvem gør arbejdet |
| --- | --- | --- |
| `http` | metode, URL, body-skabelon, svar-skabeloner | `HttpToolHandler` — ét HTTP-kald |
| `mcp` | server, tool | `McpToolHandler` — videresender til en MCP-server |
| `internal` | handler-nøgle | `InternalToolHandler` — en klasse i koden (`IInternalTool`) |

Nye tool-**typer** er kode (der er tre). Nye tools af en kendt type er rækker. Det er hele fase 4.

```
Bruger:     "Kører PersonaleNet lige nu?"
Registry:   SELECT … FROM tools WHERE is_active AND rolle matcher  →  7 rækker
Loader:     7 × RegistryFunction (AIFunction bygget af rækken, ikke af reflection)
Model:      [kalder hr_systemstatus()]
Handler:    http → GET http://localhost:5100/health → {"status":"ok","employees":1}
            successMessage-skabelon → "PersonaleNet kører (status: ok) og har 1 medarbejder(e) registreret."
Model:      "PersonaleNet kører og har én medarbejder registreret."
```

`hr_systemstatus` findes ikke i koden. Det blev tilføjet med én `PUT /tools/hr_systemstatus`, mens
API'et kørte, og var med i næste tur. Det er leverancen.

---

## 2. Datamodellen (4.1): fire tabeller

`PgToolRegistry.EnsureCreatedAsync` opretter dem ved opstart — SQL'en dér *er* skemaet:

- **`tools`** — navn (primærnøgle, `[a-z][a-z0-9_]*`), beskrivelse, `parameters_schema` (jsonb),
  `handler_type`, `handler_config` (jsonb), `requires_confirmation`, `summary_template`, `is_active`.
- **`roles`** — bare et navn og en beskrivelse. Seedes med `medarbejder` og `hr`.
- **`tool_roles`** — koblingen. Et tool uden rækker her kan **ingen** bruge. Det er bevidst: en glemt
  tildeling skal give "toolet mangler", ikke "alle har adgang".
- **`mcp_servers`** — MCP-servere, der kan importeres fra: transport (`stdio`/`http`), kommando,
  argumenter, URL, aktiv.

### Hvorfor Postgres og rå SQL?

Fase 2 valgte pgvector netop med henvisning til denne fase: *"én database til det hele"*. Fire
tabeller retfærdiggør ikke en ORM, og Npgsql-mønstret fra `PgVectorStore` genbruges 1:1. Tabellerne
oprettes med `CREATE TABLE IF NOT EXISTS` som vektor-tabellen — der er ingen migrationsmotor endnu,
og det er en bevidst udskydelse (se afsnit 8).

### Migreringen af fase 3's tools

`ToolSeed.Default` er fase 3's tre tools som rækker. De indsættes kun, hvis tabellen er tom — derefter
er databasen sandheden. Beskrivelserne er ordret dem, der bestod fase 3-evalueringen, så modellens
adfærd ikke ændres af flytningen. To ting er værd at lægge mærke til:

- `soeg_i_dokumentation` er nu en `internal`-række, der peger på nøglen `dokumentationssoegning`.
  Koden (`DocumentSearchTool`) leverer kun udførelsen; navn, beskrivelse og roller står i databasen.
  Dokumentationssøgningen kan altså slås fra pr. rolle eller omdøbes uden kode — "søgning er bare
  endnu et tool" er nu bogstaveligt.
- `opret_medarbejder` og `find_medarbejder` er `http`-rækker mod HR-dummy'en. `EmployeeTools`,
  `IEmployeeService` og `HttpEmployeeService` er slettet — det generiske HTTP-kald erstatter dem.

---

## 3. Runtime-loaderen (4.2): `RegistryFunction`

`Microsoft.Extensions.AI` har en abstrakt `AIFunction`. Fase 2-3 lavede instanser med
`AIFunctionFactory.Create(metode)`, som læser navn, beskrivelse og skema med reflection. Fase 4
arver i stedet direkte og lader rækken svare:

```csharp
public override string Name => _tool.Name;
public override string Description => _tool.Description;
public override JsonElement JsonSchema => _tool.ParametersSchema;
```

`UseFunctionInvocation()` i chat-pipelinen ser ingen forskel: den sender skemaet til modellen, og
når modellen kalder, lander argumenterne i `InvokeCoreAsync`. Dér sker to ting:

1. **Argumenter uden for skemaet fjernes.** Modellen skal ikke kunne smugle et ekstra felt med ind
   i en HTTP-body ("`isAdmin: true`"). Kun `properties` fra skemaet slipper igennem.
2. **Bekræftelses-flowet er generisk.** Har rækken `requires_confirmation`, udføres intet: skemaets
   `required` tjekkes (tomme strenge tæller som manglende — "spørg brugeren, gæt ikke"),
   `summary_template` udfyldes ("Opret {fuldeNavn} ({email}) …"), og forslaget gemmes som en
   `PendingAction` — præcis fase 3, bare skrevet én gang for alle tools.

Selve udførelsen efter brugerens "ja" sker i `DynamicActionExecutor`. Den slår toolet op **igen** med
turens roller, før den kalder handleren. Et forslag fra i går må ikke kunne udføres, hvis toolet siden
er deaktiveret, eller rettigheden er fjernet. `IActionExecutor` er bevaret som interface (nu med
`CanExecuteAsync`), så et særligt tool stadig kan få sin egen executor.

### `DynamicToolProvider`: ingen cache, med vilje

Registret læses ved hver tur. Det er én lille forespørgsel, og det er netop dét, der gør, at en ny
række virker uden genstart. Er databasen nede, får modellen ingen tools, og loggen siger hvorfor —
samme holdning som `VectorStoreInitializer` i fase 2: chatten må ikke dø af et manglende tool.

### Rettigheder håndhæves i SQL, ikke i prompten

```sql
WHERE t.is_active
  AND EXISTS (SELECT 1 FROM tool_roles tr WHERE tr.tool_name = t.name AND tr.role_name = ANY($1))
```

Modellen får kun de tools, brugerens roller giver adgang til. Den kan ikke kalde
`opret_medarbejder` som `medarbejder` — ikke fordi prompten siger "du må ikke", men fordi funktionen
ikke findes i dens verden. Det er "kan ikke" frem for "må ikke", og det er den eneste slags
rettighed, der holder mod en model, der læser dokumenter som instruktioner.

Rollerne kommer fra headeren `X-Roles: medarbejder,hr` eller, hvis den mangler, fra
`Tools:DefaultRoles`. Det er en **påstand**, ikke et bevis — fase 5.1 kobler rigtig autentificering
på. Men hele kæden bagved (tabel → SQL-filter → hvad modellen ser → hvad der må udføres) er på plads
og testet, så 5.1 kun skal levere "hvem er brugeren".

> **Fund under fase 4:** Konfigurationsbinding *lægger til* et array med standardværdier i stedet
> for at erstatte det. `DefaultRoles = ["medarbejder","hr"]` i koden plus det samme i appsettings
> gav `[medarbejder, hr, medarbejder, hr]` i loggen. Harmløst her (SQL'ens `ANY` er ligeglad), men
> rollerne normaliseres nu (trim, små bogstaver, distinct) ét sted i `ChatService`.

---

## 4. HTTP-handleren: skabeloner i stedet for kode

Det generiske HTTP-kald skal kunne det, fase 3's håndskrevne `HttpEmployeeService` kunne — og
svare på dansk uden et ekstra modelkald. Løsningen er `{pladsholder}`-skabeloner i `handler_config`:

```json
{
  "method": "POST",
  "url": "http://localhost:5100/employees",
  "body": { "fullName": "{fuldeNavn}", "email": "{email}", "department": "{afdeling}",
            "jobTitle": "{stilling}", "startDate": "{startdato}" },
  "successMessage": "{fullName} er nu oprettet i PersonaleNet som {jobTitle} i {department} med start {startDate} (medarbejder-id {id}). …",
  "errorMessage": "HR-systemet afviste oprettelsen: {error} Ret oplysningerne, og prøv igen."
}
```

- **`body`** oversætter modellens parameternavne (danske) til systemets felter (engelske). En værdi,
  der kun er én pladsholder (`"{antal}"`), beholder JSON-typen, så tal forbliver tal.
- **`url`**-pladsholdere URL-kodes (`?name={navn}` → `name=Lars%20Hansen`).
- **`successMessage`** udfyldes med svarets top-felter plus parametrene; **`itemTemplate`** giver én
  linje pr. element i et liste-svar (`find_medarbejder`), **`emptyMessage`** dækker tom liste og 404.
- **`errorMessage`** får `{error}` fra typiske fejl-bodies (`{"error": …}`, `{"errors": […]}`,
  ProblemDetails). Uden skabeloner returneres svarets rå tekst — modeller læser JSON fint.

Vigtigst: handleren **kaster ikke** for forventelige fejl. Kan systemet ikke nås, svarer det 5xx eller
går timeouten, får modellen en sætning, den kan give videre ("Systemet bag 'x' kunne ikke nås …").
Det er begyndelsen på fase 5.2's "botten skal forklare, hvad der gik galt, ikke bare fejle stille".

`TemplateRenderer` er bevidst primitiv: `{navn}` og intet andet. Ingen udtryk, ingen betingelser.
Skal en kollega kunne skrive en tool-række, må skabelonsproget ikke kræve, at man læser koden.

---

## 5. MCP-integrationen (4.3)

`ModelContextProtocol` (det officielle C#-SDK, 2.2.0) leverer klientsiden. En MCP-server er en
proces (stdio) eller et HTTP-endpoint, der selv fortæller, hvilke tools den har — med navn,
beskrivelse og JSON-skema. Det passer som hånd i handske til registret:

1. `PUT /mcp-servers/filer` registrerer serveren: `npx -y @modelcontextprotocol/server-filesystem <mappe>`.
2. `GET /mcp-servers/filer/tools` starter processen og viser dens 14 tools, som serveren beskriver dem.
3. `POST /mcp-servers/filer/import` med `tools: ["list_directory", "read_text_file", "search_files"]`
   og `roles: ["medarbejder","hr"]` laver tre rækker: `filer_list_directory` osv., med serverens
   egen beskrivelse og skema kopieret ind, `handler_type = mcp` og `handler_config = {server, tool}`.

Fra det øjeblik er MCP-tools **almindelige rækker**. Roller, aktiv/inaktiv og bekræftelse styres
præcis som for HTTP-tools — en MCP-server kan ikke give modellen et tool, registret ikke har sagt god
for. Det er pointen med at mappe ind i "samme registry-model, inkl. rettigheder". Importen tog
bevidst kun tre læse-tools; `write_file`, `move_file` og `edit_file` blev sprunget over. Skal de med,
importeres de med `requiresConfirmation: true`, så de går gennem bekræftelses-flowet.

`McpClientPool` holder én forbindelse pr. server på tværs af requests. At starte `npx` for hvert
tool-kald ville koste sekunder; nu koster første kald ~2 s (processtart) og resten millisekunder.
Ændres serverens definition, oprettes en ny klient; fejler et kald, kasseres den gamle.

Verificeret mod llama3.1: "Hvilke filer ligger i mappen …/data/dokumentation?" → modellen kalder
`filer_list_directory`, filesystem-serveren svarer, og botten lister de syv dokumenter.

### Skal jeres egne systemer wrappes som MCP-servere?

Overvejelsen fra projektplanen, med fase 4's erfaring:

- **For (MCP):** Serveren ejer sine tool-beskrivelser og skemaer og kan opdatere dem uden at røre
  chatbotten (`import` igen). Andre AI-klienter (IDE'er, andre bots) kan bruge samme server.
  Standardiseret fejlhåndtering og streaming følger med.
- **For (HTTP-rækker):** Ingen ny proces at drive. Et system med et almindeligt REST-API kan kobles på
  i dag med én PUT — og skabelonerne giver danske svar uden kode. Til 5-10 tools mod eksisterende
  API'er er det klart det billigste.
- **Anbefaling:** Brug HTTP-rækker til jeres egne, eksisterende API'er nu. Wrap et system som
  MCP-server, når (a) mere end én AI-klient skal bruge det, eller (b) tool-sættet ændrer sig så ofte,
  at "serveren beskriver sig selv" sparer vedligehold. Registret er ligeglad — begge veje ender som rækker.

---

## 6. Admin-endpoints: svaret på "kan en anden tilføje et tool?"

| Endpoint | Gør |
| --- | --- |
| `GET /tools`, `GET /tools/{navn}` | Viser registret, også inaktive tools |
| `PUT /tools/{navn}` | Opretter/erstatter et tool. Valideres af `ToolDefinitionValidator` + handlerens `Validate` |
| `DELETE /tools/{navn}` | Sletter (sæt hellere `isActive: false`) |
| `GET /roles`, `PUT /roles/{navn}` | Roller |
| `GET/PUT/DELETE /mcp-servers/{navn}` | MCP-servere |
| `GET /mcp-servers/{navn}/tools` | Hvad serveren tilbyder |
| `POST /mcp-servers/{navn}/import` | Importér som rækker med roller |

Valideringen er det eneste værn mellem en tastefejl og et ubrugeligt tool, så den er konkret: navnet
skal kunne bruges som funktionsnavn, skemaet skal være `"type": "object"`, `required` må kun nævne
felter der findes, `summaryTemplate`'s pladsholdere skal være parametre, og et tool med bekræftelse
*skal* have en `summaryTemplate` (ellers siger brugeren ja til "opret_x med {…json…}"). En PUT med
fem fejl får fem beskeder tilbage på én gang.

Der er **ingen adgangskontrol** på disse endpoints — som på `/ingest`. Det er fase 5.1. Indtil da er
API'et et udviklerværktøj og må ikke eksponeres for andre.

---

## 7. Tests og verifikation

**Enhedstests (112, alle grønne)** — de nye dækker det, der ikke må gå galt:

- `RegistryFunctionTests`: skema eksponeres fra rækken; læse-tools udføres straks; argumenter uden
  for skemaet fjernes; skrive-tools gemmer forslag og kalder *ikke* handleren; manglende `required`
  giver "mangler: …, gæt ikke".
- `DynamicToolProviderTests`: `medarbejder` ser to tools, `hr` tre, ingen roller = ingen tools,
  inaktive og tools uden handler springes over, database nede = tom liste uden undtagelse.
- `DynamicActionExecutorTests`: udfører via handleren; afviser, når rollen ikke længere giver adgang.
- `HttpToolHandlerTests`: body-skabelon, URL-kodning, liste-formatering, tom liste, 4xx →
  `errorMessage`, 5xx og "kan ikke nås" → tekst, JSON-typer bevares, konfigurationsfejl fanges.
- `ToolSeedTests`: fase 3's tre tools består valideringen; kun `opret_medarbejder` kræver
  bekræftelse og er forbeholdt `hr`.

**Manuelt mod llama3.1 + HR-dummy + Postgres + filesystem-MCP-serveren** (samme session, sekventielt):

| Scenarie | Resultat |
| --- | --- |
| Opret Mette Nielsen … (standardroller) → "ja" | Forslag venter; "ja" udfører via `http`-handleren; HR-dummy'en har hende |
| Samme besked med `X-Roles: medarbejder` | Modellen får 6 tools uden `opret_medarbejder`; intet forslag, intet oprettet |
| "Er Mette Nielsen oprettet?" | `find_medarbejder` → `GET /employees?name=Mette%20Nielsen` → svar med hendes data |
| "Hvor mange feriedage har jeg?" | `soeg_i_dokumentation` via `internal`-handleren; kilder returneres som før |
| `PUT /tools/hr_systemstatus` → "Kører PersonaleNet?" | Toolet kaldes i næste tur — ingen genstart |
| MCP: "Hvilke filer ligger i mappen …?" | `filer_list_directory` → filesystem-serveren svarer |
| "Opret en medarbejder der hedder Lars Hansen" | Ingen forberedelse; botten spørger om de manglende oplysninger |

Evalueringssættet har fået to rettigheds-scenarier (R01/R02 med `roles`), og evalueringsværktøjet
sender `X-Roles`. To fulde kørsler mod llama3.1:

| Kørsel | Resultat | Tool-scenarier | Rettigheder |
| --- | --- | --- | --- |
| [Tre seedede tools](evaluering/resultater/2026-09-14-fase-4.md) (sammenligneligt med fase 3) | 24/28 = 86 % | 6/6 | 2/2 |
| [Alle 7 tools i registret](evaluering/resultater/2026-09-14-fase-4-7-tools.md) | 23/28 = 82 % | 6/6 | 2/2 |

Handlings- og rettighedsdelen er fejlfri i begge. Alle fejl er dokumentationsspørgsmål, hvor modellen
**ikke søgte** — og fire ekstra tools kostede ét svar mere. Det er fase 3's advarsel ("en lokal 8B-model
bliver upålidelig med flere tools") målt: tool-antallet pr. rolle er en kvalitetsknap, ikke kun en
sikkerhedsknap. Roller er dermed også midlet mod tool-støj.

---

## 8. Hvad der bevidst *ikke* er gjort

- **Ingen semantisk filtrering af tools (4.4).** Projektplanen siger "hvis tool-antallet vokser
  (50+)". Der er syv. Mekanikken er klar til det — registret leverer definitioner, loaderen bygger
  funktioner — så filtret bliver et trin imellem, når det bliver relevant.
- **Ingen migrationsmotor.** `CREATE TABLE IF NOT EXISTS` som i fase 2. Første skemaændring på en
  database med data er det rigtige tidspunkt at indføre migrationer — ikke før.
- **Ingen adgangskontrol på admin-endpoints og ingen rigtig autentificering.** Fase 5.1.
- **Ingen cache af registret.** Én forespørgsel pr. tur er billig og gør "uden genstart" trivielt sandt.
- **Ingen Semantic Kernel.** Projektplanens ordlyd ("oversæt til en Semantic Kernel-funktion") er
  opfyldt med `Microsoft.Extensions.AI`'s `AIFunction`, jf. fase 1's beslutning. Det er samme rolle.
- **MCP-tools importeres uden bekræftelse som standard.** Importen af filesystem-serveren tog kun
  læse-tools. Skrive-tools skal importeres med `requiresConfirmation: true` — det er den, der importerer,
  der ved, hvad toolet gør.

---

## 9. Sådan afprøver du det selv

```bash
docker compose up -d                        # Postgres (pgvector + tool-registry)
dotnet run --project src/Chatbot.DummyHr    # HR-dummy på http://localhost:5100
dotnet run --project src/Chatbot.Api        # Chatbot på http://localhost:5022
```

Loggen viser ved opstart: `Tool-registret er klar med 3 tools (3 aktive): find_medarbejder, opret_medarbejder, soeg_i_dokumentation`.

Prøv derefter flowet i [Chatbot.Api.http](../src/Chatbot.Api/Chatbot.Api.http) under "Fase 4", eller:

```bash
curl http://localhost:5022/tools                                   # registret

# Nyt tool uden genstart
curl -X PUT http://localhost:5022/tools/hr_systemstatus -H "Content-Type: application/json" --data-binary @- <<'EOF'
{ "description": "Tjekker om HR-systemet PersonaleNet kører, og hvor mange medarbejdere der er registreret.",
  "parametersSchema": { "type": "object", "properties": {} },
  "handlerType": "http",
  "handlerConfig": { "method": "GET", "url": "http://localhost:5100/health",
                     "successMessage": "PersonaleNet kører (status: {status}) og har {employees} medarbejder(e)." },
  "roles": ["medarbejder", "hr"] }
EOF

curl -X POST http://localhost:5022/chat -H "Content-Type: application/json" \
  -d '{"message":"Kører PersonaleNet lige nu?"}'

# Rettigheder: som medarbejder findes opret_medarbejder ikke
curl -X POST http://localhost:5022/chat -H "Content-Type: application/json" -H "X-Roles: medarbejder" \
  -d '{"message":"Opret Mette Nielsen, mette@firma.dk, Konsulent i Salg, start 2026-11-01"}'
# → ingen pendingAction
```

Fejlsøgning: hver tur logger `Roller [medarbejder, hr] giver 7 tools: …` — står toolet ikke der, er
det inaktivt, uden rolle, eller rollen mangler i headeren. `Tool x (Http) kaldes` viser, at modellen
valgte det; `HTTP-tool x: GET …` viser det faktiske kald; `AUDIT tool=…` viser bekræftede handlinger
med roller og parametre. En `PUT /tools/…` der giver 400 lister alle fejl i `errors`.
