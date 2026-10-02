# ADR-0029: Serwis SleepDiary: model domeny

- **Status:** Zaakceptowany
- **Data:** 2026-09-30
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §6; ADR-0002, ADR-0012, ADR-0015, ADR-0024

## Kontekst

Użytkownik prowadzi dziennik snu: jeden wpis na dzień z informacjami o śnie.

## Decyzja

- Agregat **`SleepEntry`**: użytkownik (`sub`), data (dzień przebudzenia), godzina położenia się,
  godzina wstania, czas zasypiania (minuty), liczba przebudzeń, jakość snu 1–5, notatka.
- Jeden wpis na użytkownika na dzień (unikalność `UserId` + `Date`).
- Reguły: wstanie po położeniu się, czas w łóżku najwyżej 24 h, dzień wstania równy dacie wpisu,
  czas zasypiania nie dłuższy niż czas w łóżku, przebudzenia 0–50, notatka do 2000 znaków,
  brak wpisów z przyszłą datą.
- Wyliczane: czas w łóżku i czas snu (czas w łóżku minus czas zasypiania).
- Przypadki użycia: zapis, edycja, usunięcie wpisu; wpis z danego dnia; lista wpisów z zakresu
  dat ze średnim czasem i jakością snu.
- Zdarzenie integracyjne `SleepEntryRecordedV1` przy zapisie nowego wpisu.
- Scope: `sleepdiary.entry.read`, `sleepdiary.entry.write`; użytkownik ma dostęp wyłącznie
  do własnych wpisów (handler zawsze działa w zakresie bieżącego użytkownika).

## Konsekwencje

- Godziny przechowywane jako czas lokalny użytkownika (bez strefy); raporty międzystrefowe
  wymagałyby dodatkowej informacji o strefie czasowej.
