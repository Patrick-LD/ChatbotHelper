# Oprettelse af medarbejdere i PersonaleNet

PersonaleNet er virksomhedens HR-system, hvor alle medarbejdere oprettes, vedligeholdes og
fratrædes. Denne vejledning beskriver, hvordan en ny medarbejder oprettes korrekt, så løn,
IT-adgange og udstyr bliver bestilt automatisk.

## Hvem må oprette medarbejdere?

Kun brugere med rollen **HR-administrator** i PersonaleNet kan oprette medarbejdere. Ledere
kan se deres egne medarbejdere, men ikke oprette nye. Har du brug for at få oprettet en
medarbejder, sender du en anmodning til HR via formularen "Ny medarbejder" på intranettet
senest 10 arbejdsdage før første arbejdsdag.

## Oplysninger der skal være klar

Før du opretter medarbejderen, skal du have følgende oplysninger:

- Fulde navn som det står i passet
- CPR-nummer
- Privat e-mailadresse (bruges til at sende velkomstbrev og første login)
- Mobilnummer
- Startdato
- Stillingsbetegnelse
- Afdeling og nærmeste leder
- Ansættelsestype: fastansat, tidsbegrænset, studentermedhjælper eller konsulent
- Ugentligt timetal
- Lønramme (angives af den ansættende leder i anmodningen)

Mangler CPR-nummer eller startdato, kan medarbejderen ikke oprettes — systemet afviser
oprettelsen med en fejl.

## Trin for trin

1. Log ind på PersonaleNet på personalenet.firma.internal med dit almindelige login og MFA.
2. Vælg **Medarbejdere** i venstremenuen og klik på **Opret ny**.
3. Udfyld fanen **Stamdata**: navn, CPR-nummer, privat e-mail og mobilnummer. Systemet
   slår adressen op automatisk ud fra CPR-nummeret.
4. Udfyld fanen **Ansættelse**: startdato, stillingsbetegnelse, afdeling, nærmeste leder,
   ansættelsestype og timetal. Vælg den rigtige overenskomst i rullelisten — for
   funktionærer er det altid "Funktionær - standard".
5. Udfyld fanen **Løn**: månedsløn, pensionsprocent (standard 10 % arbejdsgiver / 5 % egen)
   og eventuelle tillæg.
6. Klik på **Gem og send til godkendelse**. Den ansættende leder får en mail og skal godkende
   inden for 3 arbejdsdage.
7. Når lederen har godkendt, skifter medarbejderens status til **Aktiv fra startdato**, og
   følgende sker automatisk natten efter:
   - Der oprettes en brugerkonto og e-mail i IT-systemet (se vejledningen om IT-adgange).
   - Der bestilles standardudstyr (bærbar og mobil) hos IT-support.
   - Medarbejderen modtager et velkomstbrev på sin private e-mail med første login.

## Typiske fejl

- **"CPR-nummer findes allerede"**: Medarbejderen har været ansat før. Brug i stedet
  funktionen **Genansæt** under den gamle medarbejderpost, så historikken bevares.
- **Lederen kan ikke godkende**: Lederen mangler rollen "Leder" i PersonaleNet. Kontakt HR.
- **Ingen e-mail oprettet dagen efter**: Tjek at startdatoen ikke ligger mere end 30 dage
  ude i fremtiden — kontoen oprettes først 30 dage før startdato.

## Rettelser efter oprettelse

Stamdata og ansættelsesoplysninger kan rettes af HR-administratorer frem til startdatoen uden
ny godkendelse. Efter startdatoen kræver ændringer i løn eller timetal en ny godkendelse fra
lederen. CPR-nummer kan aldrig rettes — er det tastet forkert, skal medarbejderposten slettes
og oprettes igen, hvilket kun HR-chefen kan gøre.
