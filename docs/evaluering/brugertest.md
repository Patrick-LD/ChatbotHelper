# Brugertest — skabelon (fase 5.3)

Projektplanen: *"Test med 2-3 rigtige brugere og saml deres spørgsmål ind — de spørger anderledes,
end du forventer."* Det kan ikke automatiseres. Skabelonen her er til at gøre det systematisk, så
spørgsmålene ender i evalueringssættet i stedet for i hukommelsen.

## Sådan gør du

1. Giv brugeren en API-nøgle med den rolle, vedkommende har i virkeligheden (`medarbejder` eller `hr`)
   — ikke admin-nøglen. Så tester de også, hvad de *ikke* kan.
2. Bed dem løse 3-4 opgaver med deres egne ord, uden at vise dem evalueringssættet. F.eks.:
   "Find ud af, hvad du gør, hvis du bliver syg", "Få botten til at oprette en ny kollega",
   "Spørg om noget, du tror, den ikke ved".
3. Skriv **det præcise spørgsmål** ned, som de stillede — ikke det, du ville have stillet.
4. Notér efter hver tur: Fik de svar? Var kilden rigtig? Gjorde botten noget, de ikke bad om?
5. Kig i audit-loggen bagefter (`GET /audit?userId=<bruger>`) — den viser, hvad botten faktisk kaldte.

## Log

| Dato | Bruger (rolle) | Spørgsmål (ordret) | Svar korrekt? | Kilde rigtig? | Uventet adfærd | Til evalueringssættet? |
| --- | --- | --- | --- | --- | --- | --- |
|  |  |  |  |  |  |  |
|  |  |  |  |  |  |  |
|  |  |  |  |  |  |  |

## Efter testen

- Spørgsmål, botten svarede forkert på, tilføjes til [evalueringssaet.json](evalueringssaet.json) med facit —
  **før** noget rettes. Så måles rettelsen.
- Formuleringer, der overraskede ("jeg skal have en ny mand ind i Salg" i stedet for "opret en medarbejder"),
  er guld til tool-beskrivelserne i registret (`PUT /tools/{navn}`), ikke til systemprompten.
- Gentagne "det kan jeg ikke finde i dokumentationen" på spørgsmål, dokumentationen faktisk dækker, er
  retrieval eller modelvalg — se `GET /search?q=` først.
