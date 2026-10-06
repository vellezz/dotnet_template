# Podręcznik programisty

Podręcznik dla osoby, która dołącza do zespołu. Zakłada znajomość C# i ASP.NET Core, ale **nie** zakłada znajomości tego
repozytorium, jego wewnętrznego frameworku (`SuperApp.Framework.*`) ani przyjętych konwencji. Po lekturze potrafisz samodzielnie:

- uruchomić cały system lokalnie i debugować dowolny jego fragment,
- zdecydować, gdzie umieścić nową funkcję (który kontekst, który agregat, który projekt),
- dodać endpoint (w serwisie i w BFF), nowy zbiór danych, migrację, zdarzenie, uprawnienie, nowy serwis albo nową experience
  zgodnie z zasadami,
- dodać zdarzenie analityczne albo feature flag bez naruszania prywatności użytkowników,
- napisać właściwe testy i przejść przez checklistę przed pull requestem,
- rozpoznać i naprawić typowe błędy kompilacji, testów i działania.

Każdy przykład pochodzi z kodu repozytorium (serwisy **Knowledge** i **SleepDiary**, BFF `Example.Bff`, framework, brama) albo, w samouczku,
został zaimplementowany, zbudowany i przetestowany przed opisaniem. Zasady wiążące są w [ADR](../adr/README.md)
i w rozdziale [3 Zasady](03-zasady.md); całość architektury w [`docs/architektura.md`](../architektura.md), a szczegóły każdego projektu i interfejsu w
[`docs/dokumentacja-rozwiazania.md`](../dokumentacja-rozwiazania.md). W razie rozbieżności między
podręcznikiem a ADR wiąże ADR, a podręcznik trzeba poprawić.

## Model experience w skrócie

Rozwiązanie organizacji to **super app**: natywny shell innego zespołu (tożsamość, flagi, push, nawigacja) i moduły wielu zespołów.
Nasz zespół buduje **jedną experience**, czyli jedną funkcjonalność: moduł klienta, **BFF experience** i 1..n serwisów domenowych
(ADR-0038). Moduł woła wyłącznie API publiczne BFF przez **wspólną bramę brzegową**, która leży poza naszym zakresem; `SuperApp.Gateway`
w repozytorium jest jej lokalnym zamiennikiem i specyfikacją wymagań (ADR-0037). BFF-y innych experience wołają tylko nasze API
wewnętrzne BFF, nigdy nasze serwisy domenowe (ADR-0039, ADR-0041), a wywołania w kontekście użytkownika przekazują jego token bez
zmian (ADR-0040).

**Stan obecny:** przykładowa experience `example` ma w repozytorium BFF `src/Bff/Example.Bff` (fasada serwisów Knowledge
i SleepDiary, endpoint komponowany `GET /v1/me/summary` i API wewnętrzne `/internal/v1/...`). Lokalna brama kieruje ruch modułu
wyłącznie na `/api/example/v{n}/**` do tego BFF; tras do serwisów domenowych nie ma. Przykłady w podręczniku pokazują ten przepływ
(np. `https://localhost:5001/api/example/v1/knowledge/materials`), a wywołania bezpośrednio na porty serwisów (5101, 5102) służą
tylko do debugowania. Szczegóły: [02 Architektura w praktyce](02-architektura-w-praktyce.md#experience-w-repozytorium).

## Plan na pierwszy tydzień

| Dzień | Cel | Rozdziały |
|---|---|---|
| 1 | Uruchomić system i zrozumieć, jak przechodzi przez niego żądanie | [01 Start](01-start.md), [02 Architektura w praktyce](02-architektura-w-praktyce.md), [13 Lokalne środowisko](13-lokalne-srodowisko-i-debugowanie.md) |
| 2 | Poznać zasady i model domeny | [03 Zasady](03-zasady.md), [04 Wybór kontekstu](04-wybor-kontekstu.md), [05 Model domeny](05-model-domeny.md) |
| 3 | Przypadki użycia, dane i API | [06 Warstwa aplikacji](06-warstwa-aplikacji.md), [07 Dane i EF Core](07-dane-i-ef-core.md), [08 API i kontrakty](08-api-i-kontrakty.md) |
| 4 | Bezpieczeństwo, zdarzenia, cache | [09 Bezpieczeństwo](09-bezpieczenstwo.md), [10 Zdarzenia i integracja](10-zdarzenia-i-integracja.md), [11 Cache](11-cache.md) |
| 5 | Zbudować pełną funkcję samodzielnie | [12 Testy](12-testy.md), [16 Samouczek: pełna funkcja](16-samouczek-pelna-funkcja.md) |

Na bieżąco: [15 Dokumentacja w kodzie](15-dokumentacja-w-kodzie.md), [14 Logowanie i obserwowalność](14-logowanie-i-obserwowalnosc.md),
[17 Rozwiązywanie problemów](17-rozwiazywanie-problemow.md), [22 Narzędzie `dotnet superapp`](22-narzedzie-superapp.md). Przed pierwszą zmianą dotyczącą analityki, session replay albo
feature flags: [21 Analityka i feature flags](21-analityka-i-feature-flags.md).

## Spis treści

### Podstawy

| Rozdział | Zakres |
|---|---|
| [01 Start](01-start.md) | instalacja, build, testy, lokalne środowisko, mapa repozytorium, pierwsze żądanie |
| [02 Architektura w praktyce](02-architektura-w-praktyce.md) | jak działa system od żądania HTTP do zdarzenia w innym serwisie; kod każdego kroku |
| [03 Zasady](03-zasady.md) | zasady rozwiązania z uzasadnieniem, przykłady „źle / dobrze”, co je wymusza |
| [04 Wybór kontekstu](04-wybor-kontekstu.md) | gdzie umieścić nową funkcję: kontekst, agregat, serwis; przykłady decyzji |

### Warstwy i mechanizmy

| Rozdział | Zakres |
|---|---|
| [05 Model domeny](05-model-domeny.md) | agregaty, silne ID, value objects, błędy, `Result`, zdarzenia domenowe |
| [06 Warstwa aplikacji](06-warstwa-aplikacji.md) | komendy, zapytania, pipeline MediatR, walidatory, handlery |
| [07 Dane i EF Core](07-dane-i-ef-core.md) | konteksty zapisu i odczytu, konfiguracje, repozytoria, konflikty, migracje |
| [08 API i kontrakty](08-api-i-kontrakty.md) | kontrolery, odpowiedzi i błędy, kontrakt OpenAPI, BFF (API publiczne i wewnętrzne), klienci |
| [09 Bezpieczeństwo](09-bezpieczenstwo.md) | brama (profil `bff-web`), tokeny, przekazywanie tokenu, scope, autoryzacja zasobów, NetworkPolicy, CSRF, wylogowanie |
| [10 Zdarzenia i integracja](10-zdarzenia-i-integracja.md) | zdarzenia domenowe i integracyjne, outbox, RabbitMQ, konsumenci |
| [11 Cache](11-cache.md) | HybridCache, fail-safe, klucze i tagi, unieważnianie po commicie |
| [12 Testy](12-testy.md) | piramida testów, fixture'y, Testcontainers, testy architektury |
| [13 Lokalne środowisko i debugowanie](13-lokalne-srodowisko-i-debugowanie.md) | docker compose, tryb hybrydowy, podgląd bazy i kolejek |
| [14 Logowanie i obserwowalność](14-logowanie-i-obserwowalnosc.md) | `[LoggerMessage]`, `EventId`, tracing, metryki, `traceId` |
| [15 Dokumentacja w kodzie](15-dokumentacja-w-kodzie.md) | jak pisać dokumentację XML, która uczy |
| [21 Analityka i feature flags](21-analityka-i-feature-flags.md) | PostHog: prywatność i zgoda, pseudonim, proxy `/ingest`, forwarder zdarzeń, `IFeatureFlags`, konfiguracja, wzorce klientów |
| [22 Narzędzie `dotnet superapp`](22-narzedzie-superapp.md) | instalacja, `doctor` (spójność repozytorium, `--fix`), `list` i `info`, współpraca z Copilotem, nowe reguły |

### Praktyka

| Rozdział | Zakres |
|---|---|
| [16 Samouczek: pełna funkcja](16-samouczek-pelna-funkcja.md) | krok po kroku nowa funkcja w Knowledge: domena, dane, migracja, API, testy |
| [17 Rozwiązywanie problemów](17-rozwiazywanie-problemow.md) | objaw → przyczyna → jak sprawdzić → naprawa |
| [18 Checklista](18-checklista.md) | co sprawdzić przed pull requestem |
| [19 FAQ](19-faq.md) | pytania, które zadaje każdy na początku |
| [20 Słownik](20-slownik.md) | pojęcia domenowe i techniczne używane w projekcie |

### Przepisy na typowe zadania

Krótkie instrukcje „krok po kroku”, odsyłające do rozdziałów po wyjaśnienia.

| Zadanie | Przepis |
|---|---|
| Nowa operacja zmieniająca stan (POST/PUT/DELETE) | [01 Endpoint komendy](przepisy/01-endpoint-komendy.md) |
| Nowy odczyt danych (GET, lista ze stronicowaniem) | [02 Endpoint zapytania](przepisy/02-endpoint-zapytania.md) |
| Nowy zbiór danych w kontekście (nowy agregat i tabela) | [03 Nowy zbiór danych](przepisy/03-nowy-zbior-danych.md) |
| Nowe pole lub zmiana modelu, migracja | [04 Zmiana modelu i migracja](przepisy/04-zmiana-modelu-i-migracja.md) |
| Reakcja na zmianę: zdarzenia, cache | [05 Zdarzenia i cache](przepisy/05-zdarzenia-i-cache.md) |
| Nowe uprawnienie (scope), reguła dostępu do zasobu | [06 Uprawnienia](przepisy/06-uprawnienia.md) |
| Nowy serwis (nowy bounded context) | [07 Nowy serwis](przepisy/07-nowy-serwis.md) |
| Nowa experience (moduł + BFF + serwisy), nowy BFF | [11 Nowa experience i BFF](przepisy/11-nowa-experience-i-bff.md) |
| Synchroniczne wywołanie innego serwisu lub API wewnętrznego | [08 Wywołanie innego serwisu](przepisy/08-wywolanie-innego-serwisu.md) |
| Jakie testy napisać i gdzie | [09 Testy](przepisy/09-testy.md) |
| Nowe zdarzenie w analityce produktowej, nowa feature flag | [10 Zdarzenie analityczne i feature flag](przepisy/10-zdarzenie-analityczne-i-feature-flag.md) |

## Asystent AI w projekcie

Repozytorium zawiera instrukcje dla GitHub Copilot: ogólne (`.github/copilot-instructions.md`), per warstwa
(`.github/instructions/*.instructions.md`, stosowane automatycznie do pasujących plików) i gotowe polecenia
(`.github/prompts/`, w czacie przez `/`, np. `/new-command-endpoint`, `/review-against-rules`). Asystent zna reguły, ale
odpowiedzialność za zgodność jest Twoja: build, testy, `dotnet superapp doctor` i review są ostatecznym sprawdzianem. Zmiana zasad wymaga aktualizacji
tych instrukcji i podręcznika w tej samej zmianie.
