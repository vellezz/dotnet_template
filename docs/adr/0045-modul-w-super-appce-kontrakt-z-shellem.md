# ADR-0045: Moduł w super appce: kontrakt z shellem

- **Status:** Proponowany (doprecyzowuje ADR-0031 i ADR-0036 w części klienta; wymaga odpowiedzi zespołu shella, pytania P9, P10)
- **Data:** 2026-10-01
- **Decydenci:** właściciel projektu, zespół shella (do uzgodnienia)
- **Powiązane:** zasady architektury §1, §4, §5; ADR-0031, ADR-0036, ADR-0037, ADR-0038; raport
  `docs/analizy/2026-10-01-weryfikacja-wzgledem-notatki-ze-spotkania.md` (pakiet E1)

## Kontekst

Super app to całe rozwiązanie organizacji: natywny shell rozwijany przez inny zespół oraz moduły wielu zespołów. Nasz moduł to
jedna experience (ADR-0038). Według notatki ze spotkania shell jest hostem modułów i udostępnia wspólne funkcje: tożsamość,
feature flags, powiadomienia push i nawigację.

Repozytorium zakłada dziś, że moduł działa samodzielnie:

- aplikacje mobilne logują się same (AppAuth, klienci `mobile-*`, ADR-0031);
- klienci inicjalizują SDK PostHog, sami pytają o zgodę i oceniają flagi (ADR-0036).

W shellu dublowałoby to logowanie, flagi i zgodę analityczną. Z drugiej strony szablon ma działać także bez shella (portal web,
uruchomienie lokalne). API mostka shella nie jest jeszcze znane (P9), podobnie jak to, czy shell ma własnego dostawcę flag i analityki
(P10).

## Decyzja (proponowana)

- **Tożsamość należy do shella.** Moduł pobiera access token przez port klienta (`TokenProvider` w module). Ma on dwie implementacje:
  - mostek shella (token, odświeżanie, zdarzenie wylogowania);
  - własny AppAuth, gdy moduł działa samodzielnie.

  Backend się nie zmienia: brama, BFF i serwisy przyjmują access token z CIAM (ADR-0040), niezależnie od tego, która aplikacja go
  pobrała. Wymagane są audience i scope experience (ADR-0012).
- **Flagi:**
  - o widoczności modułu w shellu i wyłącznikach awaryjnych modułu decyduje shell;
  - flagi funkcji modułu ocenia nasz backend (`IFeatureFlags`, ADR-0036), a BFF zwraca modułowi ich wynik (np. w odpowiedzi
    endpointu konfiguracji modułu);
  - moduł nie inicjalizuje drugiego SDK flag.
- **Analityka:**
  - zgodę użytkownika dostarcza shell; moduł jej nie pyta i nie przechowuje;
  - zdarzenia klienta modułu trafiają do analityki przez mechanizm shella albo przez PostHog z pseudonimem `analyticsId`, jeśli shell
    to dopuszcza (P10);
  - zdarzenia backendowe (forwarder, ADR-0036) się nie zmieniają.
- **Push i nawigacja (deep linki)** należą do shella. Moduł deklaruje swoje ścieżki nawigacji i typy powiadomień jako wymagania.
- Bez shella (portal web, lokalnie) moduł używa implementacji samodzielnych: logowanie przez `bff-web` lub AppAuth, flagi z BFF,
  własna zgoda.

## Konsekwencje

- Po przyjęciu backend nie wymaga zmian. Zmiany dotyczą klientów: port tokenu w aplikacjach mobilnych, flagi funkcji z BFF zamiast SDK
  w module, zgoda z shella.
- ADR-0031 (lokalny CIAM) i ADR-0036 (PostHog) pozostają ważne dla trybu samodzielnego i dla backendu.
- **Do rozstrzygnięcia przed akceptacją:** API mostka shella (P9) i polityka flag oraz analityki w shellu (P10). Do tego czasu
  ADR nie jest rozstrzygnięciem (ADR-0001), a klienci mobilni zachowują obecne zachowanie.
