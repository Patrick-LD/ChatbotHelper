# Fase 1 forklaret — hvad koden gør, og hvorfor den ser sådan ud

Denne fil er en gennemgang af fase 1 for dig, der bygger chatbot for første gang.
Den forklarer **hvad** hver fil gør, men lægger mest vægt på **hvorfor** — for det er
begrundelserne, du skal kunne forsvare til eksamen, og de er også dem, der gør fase 2-5 lette.

Læs den med koden åben ved siden af.

---

## 1. Den vigtigste indsigt: modellen har ingen hukommelse

Det er den ting, der overrasker alle første gang.

En sprogmodel er en **funktion uden hukommelse**. Den får noget tekst ind og giver noget
tekst ud. Den husker *intet* mellem to kald. Ollama gemmer ikke din samtale. Modellen
"lærer" ikke af det, du skrev for et øjeblik siden.

Så hvordan kan botten så huske, at du hedder Rene?

**Fordi vi sender hele samtalen med hver gang.** Når du stiller dit fjerde spørgsmål,
sender vi ikke bare det fjerde spørgsmål — vi sender alle tre tidligere spørgsmål og
alle tre tidligere svar med, og til sidst det nye spørgsmål. Modellen læser det hele
forfra, hver gang, og *fremstår* derfor som om den husker.

Sådan ser det ud i praksis. Tur 1 sender:

```
[System]    Du er en hjælpsom assistent for medarbejdere...
[Bruger]    Jeg heder Rene og arbejder i HR-afdelingen.
```

Tur 2 sender det hele igen, plus modellens eget svar, plus det nye spørgsmål:

```
[System]    Du er en hjælpsom assistent for medarbejdere...
[Bruger]    Jeg heder Rene og arbejder i HR-afdelingen.
[Assistent] Hej Rene! Godt at du har brug for hjælp...
[Bruger]    Hvad hedder jeg, og hvilken afdeling arbejder jeg i?
```

Nu står svaret ordret i den tekst, modellen læser. Derfor kan den svare "Du hedder Rene".
Det er ikke hukommelse — det er en indkøbsliste, vi lægger på bordet igen hver gang.

**Tre konsekvenser, der forklarer resten af koden:**

1. Vi skal selv gemme samtalen et sted → derfor `IConversationStore`.
2. Prompten vokser for hver tur → derfor `MaxHistoryMessages`, som beskærer den.
3. Rollerne (System/Bruger/Assistent) er en del af formatet → derfor `ChatRole` på hver besked.

De tre roller betyder:

| Rolle | Hvem "taler" | Bruges til |
| --- | --- | --- |
| `System` | Udvikleren (dig) | Instruktioner: hvem er botten, hvordan skal den opføre sig |
| `User` | Brugeren | Spørgsmålet |
| `Assistant` | Modellen | Modellens tidligere svar |

Modellen behandler system-beskeden med større vægt end brugerens. Det er dét, der gør
det muligt at styre botten — og i fase 5 er det også dét, prompt injection forsøger at
bryde ("glem dine instruktioner").

---

## 2. Rejsen gennem koden — én besked, fra ende til anden

Når du sender `POST /chat` med `{"message": "Hvad hedder jeg?"}`, sker der dette:

```
   curl / Swagger
        │
        ▼
   ChatEndpoints.cs            ← web-lag: tjekker input, oversætter fejl til HTTP-koder
        │  ChatTurnRequest
        ▼
   ChatService.cs              ← hjernen: bygger prompten, kalder modellen, gemmer svaret
        │        ├──────────────► InMemoryConversationStore   (hent + gem historik)
        │        └──────────────► IChatClient → OllamaSharp → Ollama på 127.0.0.1:11434
        │  ChatTurnResult
        ▼
   ChatEndpoints.cs → JSON tilbage til brugeren
```

`ChatService.SendAsync` er hele fase 1 samlet på ét sted, og det er værd at læse linje
for linje ([ChatService.cs](../src/Chatbot.Core/Chat/ChatService.cs)):

```csharp
// 1. Nyt id hvis samtalen er ny, ellers brug det brugeren sendte med
var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
    ? Guid.NewGuid().ToString("n")
    : request.ConversationId;

// 2. Hent hvad der tidligere er sagt i netop denne samtale
var history = await _conversations.GetAsync(conversationId, cancellationToken);
var userMessage = new ChatMessage(ChatRole.User, request.Message);

// 3. Byg prompten: systemprompt → historik → den nye besked
List<ChatMessage> prompt =
[
    new ChatMessage(ChatRole.System, _options.SystemPrompt),
    .. Trim(history),
    userMessage,
];

// 4. Spørg modellen
var response = await _chatClient.GetResponseAsync(prompt, cancellationToken: cancellationToken);

// 5. Gem både spørgsmål og svar, så næste tur kan se dem
await _conversations.AppendAsync(conversationId,
    [userMessage, new ChatMessage(ChatRole.Assistant, response.Text)], cancellationToken);
```

Fem trin. Det er faktisk hele chatbotten. Alt det andet i fase 1 er struktur omkring
de fem trin — og den struktur er der, fordi fase 2, 3 og 4 skal ind **mellem** trin 2 og 4
uden at vælte noget.

`..Trim(history)` er en *collection expression* med spread (C# 12): det pakker listens
elementer ud ind i den nye liste, i stedet for at lægge listen ind som ét element.
Ligesom `...` i JavaScript.

---

## 3. Hvorfor to projekter — `Chatbot.Api` og `Chatbot.Core`

Man *kunne* have skrevet det hele i `Program.cs`. Det virker. Så hvorfor ikke?

| Projekt | Indhold | Kender til |
| --- | --- | --- |
| `Chatbot.Core` | `ChatService`, `IConversationStore`, `ChatbotOptions` | Kun C# og modellen. **Ingenting om HTTP.** |
| `Chatbot.Api` | Endpoints, DI-opsætning, Swagger | Web + Core |

Pilen peger kun én vej: Api → Core. Core ved ikke, at der findes et web-API. Det er
ikke pænhed for pænhedens skyld, det køber tre konkrete ting:

1. **Testbarhed.** `ChatServiceTests` kan oprette en `ChatService` direkte med `new` og
   teste den på 27 millisekunder — uden at starte en webserver. Prøv at teste logik, der
   ligger inde i et endpoint; så skal du starte hele applikationen op.
2. **Genbrug.** Skal botten senere kunne bruges fra en konsol-app, et Teams-bot eller et
   baggrundsjob, kalder de samme `IChatService`. Web-laget er kun én af flere måder ind.
3. **Tvang til at tænke.** Når HTTP-ting ikke *kan* smutte ind i Core, bliver man nødt til
   at bestemme sig for, hvad der er domænelogik, og hvad der er transport.

Læg mærke til, at `ChatService` ikke ved, om svaret ender som JSON, i en terminal eller i
en SMS. Og at `ChatEndpoints` ikke ved, hvordan en prompt bygges. Hver fil har én opgave.

---

## 4. `IChatClient` — den vigtigste beslutning i fase 1

`ChatService` beder aldrig om "Ollama". Den beder om en `IChatClient`:

```csharp
public ChatService(IChatClient chatClient, IConversationStore conversations, ...)
```

`IChatClient` er et **interface** fra `Microsoft.Extensions.AI` — en aftale om, at
"noget kan modtage beskeder og give et svar". Hvem der reelt svarer, bestemmes ét sted,
i [Program.cs](../src/Chatbot.Api/Program.cs):

```csharp
builder.Services.AddChatClient(_ =>
        new OllamaApiClient(new Uri(chatbotOptions.Ollama.Endpoint), chatbotOptions.Ollama.Model))
    .UseLogging();
```

Skal vi senere til Azure OpenAI eller Claude, ændres **kun disse tre linjer**. `ChatService`,
endpointet og testene rører vi ikke. Det er hele pointen: planen siger, at vi starter på
Ollama og skifter til cloud senere — og den beslutning må ikke betyde en omskrivning.

Det er også derfor, testene kan snyde: `FakeChatClient` er *også* en `IChatClient`. Den
ringer ikke til nogen model, den svarer bare altid det samme og husker den prompt, den
fik. `ChatService` kan ikke se forskel — og skal ikke kunne.

> **Sidebemærkning om pakkevalget.** Projektplanen sagde `Microsoft.SemanticKernel`.
> Vi bruger `Microsoft.Extensions.AI` i stedet. Semantic Kernel er den store værktøjskasse
> (plugins, planners, hukommelse); `Microsoft.Extensions.AI` er det tynde fælles lag med
> `IChatClient`, som Semantic Kernel selv bygger ovenpå. Ollama-connectoren til Semantic Kernel
> er stadig alpha og kræver, at man slår compiler-advarsler fra. Vi vælger derfor det stabile
> lag nu — og Semantic Kernel kan lægges ovenpå i fase 3, hvis dets tool-features viser sig
> nødvendige. Interfacet er det samme.

`.UseLogging()` er et lille ekstra gratis lag: det logger hvert kald til modellen. Planen
siger "log alt fra start", og når botten svarer mærkeligt, er loggen den eneste vej til at
forstå hvorfor.

### Loggens niveauer — og hvorfor `Trace` er valgt i udvikling

`.UseLogging()` alene er ikke nok, og det er en fælde værd at kende. Laget logger på
**tre** forskellige niveauer:

| Niveau | Hvad du får |
| --- | --- |
| `Information` | Ingenting fra dette lag |
| `Debug` | "GetResponseAsync invoked" / "completed" — at der blev kaldt |
| `Trace` | **Hele prompten og hele svaret** som JSON |

Med standardopsætningen (`Information`) ser du altså ingenting. Derfor står der nu
`"Microsoft.Extensions.AI": "Trace"` i
[appsettings.Development.json](../src/Chatbot.Api/appsettings.Development.json) — så kan du
læse den præcise tekst, modellen fik. I fase 2 er det uundværligt: når botten svarer forkert
ud fra dokumentationen, er det første spørgsmål altid *hvilke chunks kom egentlig med i
prompten?*

Det står med vilje kun i `Development`-filen. Fuld prompt i loggen betyder, at alt hvad
brugerne skriver, ender i logfilen — det vil man ikke i produktion, og i fase 5.1 er det
en egentlig audit-log med bruger og tidspunkt, der skal løse den opgave.

---

## 5. Systemprompten — hvorfor den ligger i en konfigurationsfil

Systemprompten står i [appsettings.json](../src/Chatbot.Api/appsettings.json):

```json
"Chatbot": {
  "SystemPrompt": "Du er en hjælpsom assistent for medarbejdere, der har spørgsmål til ...",
  "MaxHistoryMessages": 20,
  "Ollama": { "Endpoint": "http://127.0.0.1:11434", "Model": "llama3.1" }
}
```

Grunden er simpel: **du kommer til at rette i den 50 gange.** Promptarbejde er
prøv-og-se-hvad-der-sker. Ligger teksten i konfigurationen, kan du rette den og genstarte;
ligger den i koden, skal den kompileres, committes og deployes for hver lille justering.
I fase 5.3 skal prompten finpudses igen — og i fase 2.4 skal den ændres til at bruge
dokumentationen. Den flytter sig meget.

Konfigurationen læses ind i en klasse, `ChatbotOptions`, via **options pattern**:

```csharp
builder.Services
    .AddOptions<ChatbotOptions>()
    .Bind(builder.Configuration.GetSection(ChatbotOptions.SectionName))
    .ValidateOnStart();
```

`ChatService` får så en `IOptions<ChatbotOptions>` ind og læser `.Value`. Fordelen frem for
at hive i `IConfiguration` overalt: du får **typede** værdier (`int` er en `int`, ikke en
streng, der måske kan parses), stavefejl fanges ét sted, og testene kan lave en
`ChatbotOptions` i hånden med `Options.Create(...)` — som `ChatServiceTests` gør.

### Validering: fejl i konfigurationen skal stoppe opstarten

`ValidateOnStart()` betyder "tjek konfigurationen, når applikationen starter — ikke først
når nogen bruger den". Men kaldet gør kun noget, hvis der findes regler at håndhæve. Dem
skriver vi som attributter på options-klassen:

```csharp
[Required(AllowEmptyStrings = false, ErrorMessage = "Chatbot:SystemPrompt mangler — ...")]
public string SystemPrompt { get; set; } = "Du er en hjælpsom assistent.";

[Range(0, 500)]
public int MaxHistoryMessages { get; set; } = 20;

[Required]
[ValidateObjectMembers]      // ← ellers springes de indlejrede Ollama-regler over
public OllamaOptions Ollama { get; set; } = new();
```

Selve valideringskoden skriver vi ikke. Den bliver **genereret** ud fra attributterne, og
det er hele indholdet af [ChatbotOptionsValidator.cs](../src/Chatbot.Core/Chat/ChatbotOptionsValidator.cs):

```csharp
[OptionsValidator]
public sealed partial class ChatbotOptionsValidator : IValidateOptions<ChatbotOptions>;
```

`partial` betyder "denne klasse har flere dele" — den anden del skriver en *source generator*
under kompileringen. Fordelen frem for validering, der først kører ved brug: forsøger du at
starte med en tom systemprompt, dør applikationen med det samme og siger hvorfor:

```
OptionsValidationException: SystemPrompt: Chatbot:SystemPrompt mangler — botten skal have en instruktion.
```

To detaljer, der kostede lidt tid at finde ud af, og som er værd at kende:

- **`[ValidateObjectMembers]` er nødvendig.** Uden den validerer generatoren kun de yderste
  felter og springer `Ollama`-objektets regler over — helt tavst. Compileren advarer
  faktisk om det (`SYSLIB1212`), hvilket er en god grund til at læse advarsler.
- **`[Range]` bruger rammeværkets egen engelske fejltekst.** En `ErrorMessage` på netop den
  attribut bliver ignoreret af generatoren, så den er fjernet igen frem for at stå og lyve.

Det bliver mere værd for hver fase: fase 2 tilføjer embedding-model, chunk-størrelse og
antal chunks til konfigurationen, og en tastefejl der skal fanges ved opstart — ikke opdages
som mystisk dårlige søgeresultater tre dage senere.

### Detaljen med at systemprompten ikke gemmes

Se på trin 3 igen: systemprompten sættes forrest, **hver gang**, men den gemmes aldrig
i historikken. Kun bruger- og assistent-beskeder gemmes.

Det er et bevidst valg. Havde vi gemt systemprompten i historikken, ville en igangværende
samtale blive ved med at bruge den *gamle* prompt, selv efter du havde rettet
`appsettings.json`. Ved at bygge den på forfra slår en rettelse igennem med det samme —
også midt i en samtale. Det gør promptarbejde markant hurtigere.

---

## 6. `MaxHistoryMessages` — hvorfor prompten skal beskæres

En model har et **kontekstvindue**: en øvre grænse for, hvor meget tekst den kan læse i
ét kald, målt i *tokens* (cirka ¾ ord pr. token). For llama3.1 er det stort, men ikke
uendeligt.

Da vi sender hele samtalen hver gang, vokser prompten hele tiden. En samtale på 100 ture
sender 200 beskeder. Så sker to ting: det bliver langsomt og dyrt (i cloud betaler man
pr. token), og til sidst afvises kaldet, fordi det ikke kan være der.

Derfor:

```csharp
private IEnumerable<ChatMessage> Trim(IReadOnlyList<ChatMessage> history)
{
    var max = _options.MaxHistoryMessages;
    if (max <= 0 || history.Count <= max)
    {
        return history;
    }
    return history.Skip(history.Count - max);
}
```

`Skip(history.Count - max)` springer det ældste over og beholder de **seneste** `max`
beskeder. Nyeste kontekst er næsten altid den vigtigste.

Prisen er ærlig at kende: fortæller du dit navn i besked 1 og først spørger i besked 30,
er navnet faldet ud af vinduet, og botten kan ikke svare. Det er en bevidst afvejning —
og der findes bedre metoder senere (opsummér den gamle del af samtalen med modellen selv,
eller slå op i historikken semantisk med de RAG-teknikker, du bygger i fase 2).

`max <= 0` betyder "ingen beskæring". Praktisk i test, hvor man vil se hele prompten.

---

## 7. Historik-lageret — interface først, simpel implementering bagefter

Der er to filer, og forskellen mellem dem er hele idéen:

- [IConversationStore.cs](../src/Chatbot.Core/Chat/IConversationStore.cs) — **aftalen**: man kan hente en samtales beskeder, og man kan tilføje til dem.
- [InMemoryConversationStore.cs](../src/Chatbot.Core/Chat/InMemoryConversationStore.cs) — **én måde** at opfylde aftalen: gem det i hukommelsen.

Hukommelse betyder, at samtalerne forsvinder, når du stopper applikationen. Det er helt
fint nu — planen siger eksplicit "i hukommelsen er fint til at starte med", og fase 5.2
flytter det til databasen. Når den dag kommer, skriver du en `DatabaseConversationStore`,
der opfylder samme interface, og ændrer én linje i `Program.cs`. `ChatService` mærker intet.

Det er den samme figur som med `IChatClient`. Læg mærke til mønsteret — det er hele
projektets rygrad: **alt, du ved du kommer til at skifte, skal ligge bag et interface.**

### Hvorfor `ConcurrentDictionary` *og* `lock`

```csharp
private readonly ConcurrentDictionary<string, List<ChatMessage>> _conversations = new();
```

En webserver håndterer flere forespørgsler **samtidig**, på forskellige tråde. To brugere
kan skrive i samme millisekund. Almindelig `Dictionary` kan gå i stykker på uforudsigelige
måder, hvis to tråde skriver i den på samme tid — ikke en pæn fejl, men ødelagte interne
data. `ConcurrentDictionary` er bygget til at tåle det.

Men det beskytter kun *ordbogen*, ikke de `List<ChatMessage>` der ligger inde i den. En
liste kan lige så godt gå i stykker, hvis to tråde tilføjer samtidig. Derfor `lock`:

```csharp
lock (conversation)
{
    conversation.AddRange(messages);
}
```

`lock` betyder "kun én tråd ad gangen herinde". Vi låser på selve samtalens liste, ikke
på hele ordbogen — så to brugere i to forskellige samtaler ikke venter på hinanden.

Bemærk også, at `GetAsync` returnerer `messages.ToArray()` inde i låsen: en **kopi**.
Ellers kunne kalderen sidde og læse listen, mens en anden tråd tilføjer til den — og så
brager `foreach`. At give en kopi ud er billigere end at fejlsøge det.

At metoderne er `async` (`Task<...>`), selvom der ikke ventes på noget, er med vilje: en
database-implementering *skal* være asynkron, og ved at have det i interfacet fra starten
skal ingen kaldere laves om senere. `Task.FromResult(...)` er "her er et svar, der allerede
er klart".

---

## 8. DI: hvorfor `Singleton` for lageret og `Scoped` for servicen

```csharp
builder.Services.AddSingleton<IConversationStore, InMemoryConversationStore>();
builder.Services.AddScoped<IChatService, ChatService>();
```

**Dependency Injection** vil sige, at klasser beder om det, de har brug for, i
konstruktøren, og ASP.NET Core leverer det. Ingen klasse opretter selv sine afhængigheder —
det er dét, der gør, at man kan skifte Ollama ud med en fake i testene.

Livstiden bestemmer, *hvor længe* et objekt lever:

| Livstid | Betydning | Hvorfor her |
| --- | --- | --- |
| `Singleton` | Ét objekt for hele applikationen | Historikken **skal** overleve mellem forespørgsler. Var lageret `Scoped`, ville hver forespørgsel få en ny, tom ordbog — og botten ville miste hukommelsen fuldstændigt. |
| `Scoped` | Ét objekt pr. HTTP-forespørgsel | `ChatService` har ingen data, der skal overleve. Den samler ting og kaster dem væk. |

Det er værd at forstå, for det er en klassisk fejl: gør man ved et uheld lageret `Scoped`,
er fejlen ikke et crash, men en bot der har glemt alt ved hvert kald — og det er svært at
gennemskue, hvis man ikke kender de tre livstider.

---

## 9. Web-laget — og hvorfor der er to slags fejl

[ChatEndpoints.cs](../src/Chatbot.Api/Endpoints/ChatEndpoints.cs) er tynd med vilje. Den
gør præcis tre ting: tjekker input, kalder `IChatService`, oversætter fejl til HTTP-koder.

```csharp
if (string.IsNullOrWhiteSpace(request.Message))
{
    return Results.BadRequest(new { error = "Feltet 'message' må ikke være tomt." });
}
```

**400 Bad Request** betyder "du gjorde noget forkert". En tom besked er ikke noget, der
skal ud og genere modellen.

At `ChatService` *også* selv tjekker for tom besked, er ikke dobbeltarbejde. Endpointet
giver brugeren en pæn besked; `ChatService` beskytter sig selv, fordi den også kaldes fra
testene og senere fra andre steder end web-laget. En klasse skal kunne stole på sit eget
input uanset hvem der kalder.

```csharp
catch (HttpRequestException ex)
{
    return Results.Problem(
        title: "Modellen kunne ikke kontaktes",
        detail: $"Kunne ikke nå Ollama. Kører 'ollama serve', og er modellen hentet? ({ex.Message})",
        statusCode: StatusCodes.Status503ServiceUnavailable);
}
```

**503 Service Unavailable** betyder "min fejl, prøv igen senere". Uden dette får man 500
med 40 linjer stack trace i browseren, hver gang Ollama ikke kører — og det *kommer* til
at ske ofte i udviklingen. Den her fangede faktisk fejlen med de to Ollama-servere under
afprøvningen: den sagde `model not found` i stedet for at drukne pointen i en stak.

`Results.Problem` følger i øvrigt en standard (RFC 9457, "problem details"), så fejl har
samme form hver gang. Nyttigt, når en frontend senere skal vise dem.

### Records som datakontrakter

```csharp
public sealed record ChatRequest(string Message, string? ConversationId);
public sealed record ChatResponse(string Reply, string ConversationId);
```

En `record` er en kort måde at skrive en klasse, der kun bærer data: uforanderlig
(kan ikke ændres efter oprettelsen) og med indbygget sammenligning på indhold.
Én linje i stedet for femten.

Bemærk `string?` på `ConversationId` i request'en, men `string` uden `?` i response'en.
Spørgsmålstegnet siger "må mangle". Ind: du behøver ikke sende et id — så laver vi en ny
samtale. Ud: du får **altid** et id tilbage, som du kan sende med næste gang. Det står
altså i typerne, hvordan API'et skal bruges — det er ikke bare pynt.

Og hvorfor findes både `ChatRequest` (i Api) og `ChatTurnRequest` (i Core)? Fordi det er
to forskellige aftaler: den ene med omverdenen over HTTP, den anden internt i koden.
Skal API'et senere have et felt mere — `userId`, når rettighedsstyringen kommer i fase 4 —
kan det ændres, uden at Core skal følge trop. De må gerne se ens ud lige nu.

---

## 10. Testene — hvorfor de aldrig taler med en rigtig model

De fem tests i [ChatServiceTests.cs](../tests/Chatbot.Tests/ChatServiceTests.cs) kører på
27 millisekunder, fordi de bruger `FakeChatClient` i stedet for Ollama. Tre grunde:

1. **Hastighed.** Et rigtigt modelkald tager sekunder. Ingen kører tests, der tager
   minutter.
2. **Forudsigelighed.** En sprogmodel svarer ikke det samme to gange. Man kan ikke skrive
   en test, hvis facit skifter.
3. **CI'en har ingen Ollama.** GitHub Actions-maskinen har ingen model installeret. Tests,
   der kræver Ollama, ville altid fejle dér.

Tricket er, at vi ikke tester **modellens svar** — det kan man ikke enhedsteste. Vi tester
**den prompt, vi sender**. Derfor gemmer `FakeChatClient` sit sidste input:

```csharp
public IReadOnlyList<ChatMessage> LastPrompt { get; private set; } = [];
```

Og så kan testene tjekke præcis det, der er vores ansvar:

| Test | Sikrer at |
| --- | --- |
| `sender_systemprompt_foerst` | Systemprompten står forrest — ellers ignorerer modellen den |
| `uden_conversationId_starter_ny_samtale` | To brugere ikke lander i samme samtale |
| `sender_tidligere_beskeder_med_i_samme_samtale` | Hukommelsen virker: 4 beskeder i prompten, i rigtig rækkefølge |
| `beskaerer_historikken_til_MaxHistoryMessages` | Den ældste besked faktisk falder ud |
| `afviser_tom_besked` | Vi ikke spilder et modelkald på ingenting (`CallCount == 0`) |

Det er det, man kalder en *fake* (eller *test double*): en stand-in, der opfylder samme
interface. Bemærk, at det kun er muligt, **fordi** `ChatService` beder om `IChatClient` og
ikke om `OllamaApiClient`. Abstraktionen fra afsnit 4 er dét, der gør koden testbar — de
to valg hænger sammen.

Til gengæld beviser disse tests ikke, at botten *virker*. Det gjorde den manuelle
afprøvning i fase 1.4 med fire rigtige ture. Man har brug for begge slags: hurtige tests
til logikken, rigtige kald til helheden. Fra fase 2.5 bliver det systematisk med
evalueringssættet.

---

## 11. Fælden med to Ollama-servere (nu ryddet af vejen)

Værd at kende, for den kostede tid — og for at forstå, hvorfor den gamle Docker-stak nu
er parkeret.

Der kørte **to** Ollama-servere på port 11434 samtidig:

| Server | Lytter på | Har modeller |
| --- | --- | --- |
| Docker-container fra et tidligere projekt | `[::]` (IPv6) | `mistral:7b`, `nomic-embed-text` |
| Windows-installationen | `127.0.0.1` (IPv4) | `llama3.1`, `nomic-embed-text` |

De kolliderer ikke, fordi IPv4 og IPv6 er to forskellige adresserum — samme portnummer,
to forskellige "døre". Og `localhost` kan slå op som **begge**; på Windows foretrækkes
typisk IPv6. Så vores app ramte Docker-containeren, som ikke har `llama3.1`, og svarede
`model 'llama3.1' not found`.

Derfor står der `127.0.0.1` og ikke `localhost` i konfigurationen. Skal du fejlsøge noget
lignende, er det her, du ser hvem der egentlig svarer:

```bash
curl http://127.0.0.1:11434/api/tags     # IPv4 — Windows-installationen
curl http://[::1]:11434/api/tags         # IPv6 — Docker-containeren
```

### Hvorfor det ikke kunne blive stående til fase 2

`127.0.0.1` løste chatten, men problemet var værre for RAG. Begge servere havde
`nomic-embed-text` liggende — samme navn, to forskellige udgaver. Rammer indekseringen
den ene server og søgningen den anden, får du **ingen fejl**. Du får bare stille og roligt
dårligere søgeresultater, uden at noget crasher. Det er den dyreste slags fejl at finde.

Derfor er den gamle stak stoppet (`ollama`, `qdrant`, `chatbot-backend`,
`chatbot-frontend`). Containerne er *stoppet*, ikke slettet, og dataen ligger i
navngivne volumes — det gamle projekt kan hentes tilbage med:

```bash
docker start ollama qdrant chatbot-backend chatbot-frontend
```

At de kører med `restart=unless-stopped` er præcis den politik, man vil have her: et
eksplicit `docker stop` holder også, når Docker Desktop genstarter. Havde der stået
`restart=always`, var de kommet tilbage af sig selv, og fælden med dem.

Sidegevinsten er, at port 6333/6334 nu er ledige til fase 2's egen Qdrant, uden risiko
for at rode i det gamle projekts collections.

---

## 12. Hvad der bevidst *ikke* er lavet endnu

Fase 1 er et skelet, og det er meningen. Hvad der mangler, og hvor det hooker ind:

| Mangler | Hvorfor det er okay nu | Hvornår |
| --- | --- |  --- |
| Ingen viden om jeres dokumentation | Botten skal først kunne snakke, før den kan slå op | Fase 2 |
| Kan ikke *gøre* noget | Function calling kræver, at grundflowet står | Fase 3 |
| Samtaler forsvinder ved genstart | `IConversationStore` er klar til en database-udgave | Fase 5.2 |
| Ingen bruger, ingen rettigheder | Der er endnu ingen tools at give rettigheder til | Fase 4-5 |
| Historik beskæres groft | Simpelt slår smart, indtil man har målt | Senere |

Det tilfredsstillende er, hvor lidt der skal ændres. Fase 2 tilføjer et opslag i
dokumentationen **mellem trin 2 og 3** i `SendAsync`. Fase 3 giver `ChatOptions` med tools
til `GetResponseAsync` i trin 4. De fem trin bliver ikke revet ned — de får selskab.
Det er hele udbyttet af at have lagt `IChatClient` og `IConversationStore` bag interfaces
fra starten.

---

## Ordliste

| Ord | Betydning |
| --- | --- |
| **Token** | Tekststump, ca. ¾ ord. Modeller regner og betaler i tokens. |
| **Kontekstvindue** | Maks antal tokens modellen kan læse i ét kald. |
| **Prompt** | Alt det, vi sender til modellen: systemprompt + historik + nyt spørgsmål. |
| **Systemprompt** | Instruktionen til botten om, hvem den er, og hvordan den skal svare. |
| **Embedding** | En tekst omsat til en talrække, så man kan måle *betydningsmæssig* lighed. Fase 2. |
| **RAG** | Retrieval-Augmented Generation: find relevant dokumentation, læg den i prompten, lad modellen svare ud fra den. |
| **Function calling / tools** | Modellen svarer "kald funktion X med disse parametre" i stedet for tekst. Fase 3. |
| **MCP** | Model Context Protocol: standard, hvor en server selv fortæller, hvilke tools den har. Fase 4. |
| **DI** | Dependency Injection: klasser beder om deres afhængigheder i konstruktøren. |
| **Interface** | En aftale om hvad noget kan, uden at fastlægge hvordan. |
| **Fake / test double** | Stand-in for en rigtig afhængighed i tests. |

---

## Filoversigt

| Fil | Rolle |
| --- | --- |
| [Program.cs](../src/Chatbot.Api/Program.cs) | Startopsætning: DI, hvem er `IChatClient`, hvilke endpoints, Swagger |
| [Endpoints/ChatEndpoints.cs](../src/Chatbot.Api/Endpoints/ChatEndpoints.cs) | `POST /chat`: validering, kald, HTTP-fejlkoder |
| [appsettings.json](../src/Chatbot.Api/appsettings.json) | Systemprompt, model, endpoint, historik-længde |
| [appsettings.Development.json](../src/Chatbot.Api/appsettings.Development.json) | Logniveauer — `Trace` på AI-laget, så hele prompten kan læses |
| [Chat/ChatService.cs](../src/Chatbot.Core/Chat/ChatService.cs) | De fem trin: id → historik → prompt → model → gem |
| [Chat/IChatService.cs](../src/Chatbot.Core/Chat/IChatService.cs) | Aftalen om en samtaletur + `ChatTurnRequest`/`ChatTurnResult` |
| [Chat/IConversationStore.cs](../src/Chatbot.Core/Chat/IConversationStore.cs) | Aftalen om historik-lagring |
| [Chat/InMemoryConversationStore.cs](../src/Chatbot.Core/Chat/InMemoryConversationStore.cs) | Historik i hukommelsen, trådsikkert |
| [Chat/ChatbotOptions.cs](../src/Chatbot.Core/Chat/ChatbotOptions.cs) | Typet udgave af `Chatbot`-sektionen, med valideringsregler |
| [Chat/ChatbotOptionsValidator.cs](../src/Chatbot.Core/Chat/ChatbotOptionsValidator.cs) | Genereret validering, så fejl i konfigurationen stopper opstarten |
| [FakeChatClient.cs](../tests/Chatbot.Tests/FakeChatClient.cs) | Stand-in for Ollama i tests, husker sidste prompt |
| [ChatServiceTests.cs](../tests/Chatbot.Tests/ChatServiceTests.cs) | Fem tests af prompten, historikken og valideringen |
