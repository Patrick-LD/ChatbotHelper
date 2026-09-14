# Fase 6 forklaret — Vue-frontend: en brugerflade i IST's udtryk

Denne fil er gennemgangen af fase 6 i samme ånd som [FASE-1](FASE-1-FORKLARET.md) til
[FASE-5](FASE-5-FORKLARET.md): **hvad** koden gør, men mest **hvorfor**. Fase 6 tilføjer ingen
nye evner i botten — den gør dem tilgængelige for mennesker, der ikke skriver `curl`.

Læs den med `frontend/` åben ved siden af.

---

## 1. Den vigtigste indsigt: frontend'en er tynd, fordi API'et allerede afgør alt

Alt det svære ligger i API'et: hvem brugeren er (nøglen), hvad der må ske (roller), om et "ja" udfører
noget (`ConfirmationParser`), hvad der er kilder. Frontend'en gør derfor kun tre ting:

1. Sender tekst til `POST /chat` med `conversationId` og viser, hvad der kommer tilbage.
2. Viser `pendingAction` som et kort med to knapper, der sender ordret `ja` eller `nej` — de to ord,
   API'ets parser genkender. Knapteksten ("Ja, udfør") er noget andet end det, der sendes.
3. Oversætter API'ets fejl til noget, et menneske kan handle på.

Der er bevidst **ingen** logik i frontend'en om, hvad der er tilladt. Rettigheder håndhæves i API'et;
klienten ved kun, hvad `/me` fortæller, og bruger det til at vise navn og roller.

---

## 2. Opbygning

```
frontend/src
  api/        client.ts (fetch + X-Api-Key + timeout), errors.ts (ApiError), chat.ts, me.ts
  stores/     auth.ts (nøgle i sessionStorage, /me), chat.ts (beskeder, conversationId, pendingAction)
  views/      LoginView.vue, ChatView.vue
  components/ AppHeader, MessageList, MessageBubble, SourcesList, PendingActionCard, MessageComposer,
              TypingIndicator, BaseButton
  styles/     tokens.css (design), base.css (reset/typografi), fonts.css (klar til PP-fonte)
  router/     / (kræver nøgle) og /login
```

**Én fejltype.** API'et svarer i to former: `{ "error": "…" }` fra autentificeringen og fra tomme
beskeder, og ASP.NET's ProblemDetails (`title`, `detail`, `status`) fra ejerskab (403), Ollama/DB nede
(503) og timeout (504). `errors.ts` gør begge — og netværksfejl uden body — til `ApiError` med
`status`, `title`, `detail` og tre spørgsmål, UI'et stiller: `isUnauthorized` (log ud), `isForbidden`
(start ny samtale), `isRetryable` (vis "Prøv igen"). Komponenterne ser aldrig en rå `Response`.

**Chat-store'ns flow.** `send(tekst)` lægger brugerboblen ind, kalder API'et, lægger svaret ind med
kilder og `pendingAction`, og husker `conversationId`. `confirm()`/`reject()` er bare `send('ja')`/
`send('nej')`. `retry()` fjerner fejlboblen og den fejlede besked og sender igen — så der ikke står
to ens spørgsmål. `reset()` ("Ny samtale") tømmer alt; API'et starter en ny samtale, når id'et udelades.
Ved 401 logges der ud, og `App.vue` sender brugeren til login, når nøglen forsvinder — uanset hvor
401'en kom fra.

**Nøglen i `sessionStorage`.** Den overlever en reload, men ikke at fanen lukkes, og den deles ikke
mellem faner. Det er bevidst ét niveau under `localStorage`: en API-nøgle er et password. Efter reload
kaldes `/me` igen (`auth.restore()`); en 401 logger ud, en netværksfejl beholder nøglen, så et
midlertidigt udfald ikke smider brugeren ud.

**Timeout.** Klienten venter 320 s. API'ets egen Ollama-timeout er 300 s, så det er API'ets 504 med
dansk forklaring, der når frem først, når modellen er langsom — ikke en anonym netværksfejl.

---

## 3. Designet: læst fra ist.com, ikke gættet

ist.com kører på HubSpot. Farver, radius, skygger og knapper står i temaets CSS-variabler, og
`tokens.css` er en oversættelse af dem:

| ist.com | Frontend | Brug |
| --- | --- | --- |
| `--primary-color: #c9f6dc` | `--color-primary` | primær knap, fokus-ring (3px, som deres) |
| `--secondary1-color: #174655` | `--color-secondary-1` | brugerens bobler, outline-knapper, kant på bekræftelses-kort |
| `--secondary2/3-color` | `--color-secondary-2/3` | kilder (varm gul) og fejl (fersken) |
| `--navigation-bg: #000` | `--nav-bg` | headeren, logo inverteret til hvidt |
| `.btn-primary { border-radius: 500px; border: 2px; font-weight: 700; padding: 18px 30px }` | `BaseButton` | alle knapper er pills |
| `.ist-text-meetings { border-radius: 20px }` + `--shadow-color` | `.card` | chat-panel, login, bekræftelses-kort |
| `--gap: 24px`, `--max-width: 1200px` | samme | layout |

**Fontene** er det ene sted, der ikke er 1:1. ist.com bruger PP Object Sans (brød og overskrifter) og
PP Editorial New (serif til display). Begge er kommercielle fra Pangram Pangram og må ikke ligge i et
offentligt repo. Løsningen: `tokens.css` nævner dem forrest i font-stacken, `fonts.css` har
`@font-face`-blokkene klar (udkommenteret), og `frontend/public/fonts/` er gitignored. Har man licens,
lægger man filerne ind og fjerner kommentaren — intet andet ændres. Indtil da: system-sans og
Instrument Serif (Google Fonts) til display-overskrifterne. Logoet er en lokal kopi i `public/`,
ikke hot-linket.

Ingen dark mode: ist.com har ingen, og chatten skal ligne en del af sitet.

---

## 4. Backend-ændringerne (små, men nødvendige)

- **`GET /me`** — returnerer `userId`, `name`, `roles` for nøglen. Frontend'en har brug for et
  endpoint, der *kun* validerer nøglen, og `/health` er anonym. Endpointet slår intet op; det
  gengiver, hvad autentificeringen allerede afgjorde.
- **CORS** — frontend'en hostes som statisk site (Azure Blob `$web`, jf. `deploy-frontend.yml`) på en
  anden origin end API'et. `Cors:AllowedOrigins` styrer, hvem der må; kun `X-Api-Key` og `Content-Type`
  tillades som headers (den første er en custom header og udløser preflight). Tom liste = ingen
  CORS-headers overhovedet — i udvikling behøves de ikke, fordi Vite proxyer `/api` til API'et, så
  browseren ser samme origin.

Hvorfor ikke lade API'et servere frontend'en? Det havde sparet CORS, men repoet havde allerede et
statisk-site-deploy klar, og adskillelsen gør det muligt at rulle frontend og API uafhængigt.

---

## 5. Tilgængelighed og robusthed i UI'et

- Nye svar annonceres med `aria-live="polite"` på beskedlisten; fejlbobler har `role="alert"`.
- "Skriver…"-indikatoren har skjult tekst til skærmlæsere; alle knapper har rigtig fokus-ring.
- Skip-link til indholdet; alle felter har labels; kontrast: sort tekst på mint, hvid på teal.
- Enter sender, Shift+Enter giver linjeskift; feltet er låst, mens der sendes, men **ikke** mens en
  handling venter — brugeren kan skrive en rettelse i stedet for ja/nej (API'et håndterer "uklart").
- Mobil: panelet fylder skærmen uden kort-kanter; desktop: 880px bredt kort midt på siden.

---

## 6. Tests og CI

24 Vitest-tests, der dækker det, der kan gå galt uden at nogen ser det: fejlnormalisering af
begge API-formater, auth-store (login, tom nøgle, restore ved 401 vs. netværk), chat-store (ja/nej
sendes ordret, `conversationId` genbruges, 401 logger ud, 403 nulstiller, retry uden dubletter,
dobbeltsend blokeres) og komponenterne PendingActionCard, MessageComposer og SourcesList.

CI har fået et `frontend`-job (node 24, `npm ci`, lint, typecheck, test, build) ved siden af
backend-jobbet. `deploy-frontend.yml` er opdateret til node 24 og `npm ci` og bager API-adressen ind
via repo-variablen `API_BASE_URL`.

---

## 7. Hvad der bevidst *ikke* er gjort

- **Ingen admin-sider** (tools, roller, audit). API'et har dem; UI'et er næste skridt, når registret
  skal vedligeholdes af andre end udviklere. `auth.isAdmin` er klar til at vise menupunktet.
- **Ingen liste over tidligere samtaler.** API'et har ikke et "mine samtaler"-endpoint endnu.
- **Ingen streaming.** API'et svarer samlet; med en cloud-model og streaming bliver det relevant.
- **Ingen PP-fonte** (licens) og ingen i18n — alt er dansk, som resten af projektet.

---

## 8. Sådan afprøver du det selv

```bash
docker compose up -d
dotnet run --project src/Chatbot.DummyHr
dotnet run --project src/Chatbot.Api
cd frontend && npm install && npm run dev      # http://localhost:5173
```

Log ind med "Hanne (HR)" (dev-genvej). Skriv "Hvor mange feriedage har jeg?" → svar med kilder.
Skriv "Opret Mette Nielsen, mette@firma.dk, Konsulent i Salg, start 2026-11-01" → bekræftelses-kort →
"Ja, udfør" → "Mette Nielsen er nu oprettet …". Klik "Log ud", log ind som "Anna (medarbejder)" og
prøv det samme: botten har ikke toolet og siger det.

Fejlsøgning: browserens netværksfane viser kaldene til `/api/...`; 401 betyder forkert nøgle, 403 en
andens samtale, 503/504 at Ollama eller databasen ikke svarer — teksten i boblen er API'ets egen.
