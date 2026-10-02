# ADR-0022: Konfiguracja YARP w MSSQL (tabele relacyjne) z cache

- **Status:** Zaakceptowany (doprecyzowany przez ADR-0037: trasy do BFF experience w lokalnej bramie)
- **Data:** 2026-09-29
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §4, §14; ADR-0004, ADR-0006, ADR-0013, ADR-0020, ADR-0021

## Kontekst

Trasy i klastry bram (`bff-web`, `gateway-mobile`) mają być zmieniane bez wdrażania nowej
wersji bramy i restartu jej podów. Konfiguracja musi być spójna we wszystkich replikach, poprawność ma być
wymuszana możliwie wcześnie (już przy zapisie), a niedostępność bazy nie może zatrzymać ruchu.

## Decyzja

### Model danych

Znormalizowane tabele w schemacie `gateway` (ADR-0013, ADR-0021). Wspierany jest **świadomie
wybrany podzbiór** opcji YARP; każda opcja ma własną kolumnę z typem i ograniczeniem.

| Tabela | Kolumny (klucz **pogrubiony**) | Ograniczenia |
|---|---|---|
| `Clusters` | **`Profile`, `ClusterId`**, `LoadBalancingPolicy`, `ActivityTimeoutSeconds`, `HttpVersion`, `HealthCheckEnabled`, `HealthCheckPath`, `HealthCheckIntervalSeconds`, `HealthCheckTimeoutSeconds` | `CHECK` na dozwolone polityki LB i wersje HTTP, czasy > 0 |
| `Destinations` | **`Profile`, `ClusterId`, `DestinationId`**, `Address` | FK → `Clusters`; `CHECK` (`CK_Destinations_ClusterAddress`): adres wyłącznie w postaci `http://{serwis}.{namespace}.svc.cluster.local:{port}` z opcjonalnym końcowym `/`, bez user info, ścieżki, query i fragmentu; port 1–65535 |
| `Routes` | **`Profile`, `RouteId`**, `ClusterId`, `Order`, `Path`, `AuthorizationPolicy`, `RateLimiterPolicy`, `TimeoutSeconds`, `MaxRequestBodySize` | FK → `Clusters` (ten sam profil); `AuthorizationPolicy` **NOT NULL** (trasa publiczna musi jawnie wskazać `anonymous`); `UNIQUE (Profile, Path, Order)` |
| `RouteMethods` | **`Profile`, `RouteId`, `Method`** | FK → `Routes`; `CHECK` na metody HTTP |
| `RouteHosts` | **`Profile`, `RouteId`, `Host`** | FK → `Routes` |
| `RouteTransforms` | **`Profile`, `RouteId`, `Order`**, `Kind`, `Name`, `Value` | FK → `Routes`; `CHECK` na `Kind`: `PathRemovePrefix`, `PathPrefix`, `PathPattern`, `RequestHeaderSet`, `RequestHeaderRemove`, `ResponseHeaderSet`, `ResponseHeaderRemove` |

- `Profile`: `CHECK` na `bff-web` / `gateway-mobile`.
- Opcja YARP spoza podzbioru = nowa kolumna/tabela w migracji + obsługa w kodzie mapowania.
  Rozszerzenie modelu jest zawsze jawne i przechodzi review.
- Encje konfiguracji w `DbContext` bramy (`GatewayDbContext`); mapowanie na `RouteConfig`/`ClusterConfig`
  w jednym miejscu: `ProxyConfigMapper` w `SuperApp.Gateway`.
- Adres docelowy decyduje, dokąd trafia żądanie razem z tokenem użytkownika, dlatego reguła adresu jest
  sprawdzana dwukrotnie: ograniczeniem `CHECK` w bazie (MSSQL nie ma wyrażeń regularnych, więc warunek
  składa się z `LIKE` w kolacji binarnej, liczby kropek, dwukropków i ukośników oraz `TRY_CAST` portu)
  i w kodzie (`ProxyDestinationAddress`, wyrażenie regularne, dodatkowo limit 63 znaków etykiety DNS)
  przed zastosowaniem konfiguracji. Kontrola w kodzie chroni też przed wpisem z cache L2 i przed
  wyłączonym ograniczeniem.

### Zarządzanie zmianami

- **Konfiguracja zmieniana wyłącznie migracjami kontekstu bramy**, z review w repo.
  Dane tras i klastrów zdefiniowane deklaratywnie przez `HasData` w jednej klasie
  `ProxyConfigurationSeed` (`SuperApp.Gateway/Persistence/Seed`), wywoływanej z `OnModelCreating`
  `GatewayDbContext`; struktura tabel i ograniczenia są w `IEntityTypeConfiguration<T>`
  (`Persistence/Configurations`). EF generuje w migracji odpowiednie `INSERT/UPDATE/DELETE`,
  więc każda zmiana tras jest widoczna w diffie.
- Dev i test: migrację stosuje `SuperApp.Migrator`. Prod: DBA uruchamia skrypt idempotentny
  wygenerowany z tych samych migracji (ADR-0004).
- **Wersja konfiguracji = ostatnia migracja w `gateway.__EFMigrationsHistory`.** Każda
  migracja (również skrypt DBA) dopisuje tam wiersz w tej samej transakcji co zmiana danych.
- Zmiana tras nie wymaga wdrożenia nowej wersji bramy: po zastosowaniu migracji repliki
  wykryją nową migrację i przeładują konfigurację.
- Ręczne zmiany w bazie są zabronione; temporal tables pozwalają je wykryć.
- Konfiguracja jest identyczna na wszystkich środowiskach (adresy usług w klastrze są takie same).

### Ładowanie i walidacja

- Własny `IProxyConfigProvider` w `SuperApp.Gateway` (`DatabaseProxyConfigProvider`) ładuje konfigurację
  profilu, mapuje ją na modele YARP (`ProxyConfigMapper`) i waliduje walidatorem YARP
  (`IConfigValidator`: trasy, klastry, istnienie polityk autoryzacji i rate limitingu). Dopiero wtedy
  publikuje nową konfigurację przez `IChangeToken`.
- Walidacja dwupoziomowa: ograniczenia bazy odrzucają błędne dane przy zapisie, a przed
  zastosowaniem kod sprawdza to, co zna tylko brama (nieobsługiwany rodzaj transformu, brak
  wymaganego pola transformu, adres spoza klastra, nieprawidłowa wersja HTTP), i walidator YARP
  spójność z kodem (np. czy wskazana polityka istnieje). Błędy mapowania nie są wyjątkami: są
  raportowane tak samo jak błędy walidatora, jako nieprawidłowa konfiguracja.
- **Nieprawidłowa konfiguracja nigdy nie zastępuje działającej**; błąd logowany
  (`[LoggerMessage]`, EventId 3002) i alertowany.
- Odrzucona wersja (identyfikator migracji) jest zapamiętywana przez replikę: kolejne odpytania jej
  nie czytają ponownie, więc zdarzenie 3002 i licznik nieudanych przeładowań są zapisywane raz na
  wersję. Następne przeładowanie wywołuje dopiero nowsza migracja (poprawka). Błędy techniczne
  (np. niedostępna baza, EventId 3003) nie są związane z wersją i są raportowane przy każdym odpytaniu.

### Cache i fail-safe

- Aktywna konfiguracja w pamięci procesu (model YARP).
- Ostatnia poprawnie zwalidowana konfiguracja zapisywana w `HybridCache` (L2 Redis, ADR-0020, wygaśnięcie
  po 30 dniach) pod kluczem `gateway:yarp-config:v1:{profil}` razem z identyfikatorem migracji. Segment
  `v1` to wersja formatu zapisanego snapshotu: zmiana kształtu `ProxyConfigSnapshot` lub encji
  konfiguracji wymaga jego podbicia, żeby nowa wersja bramy nie czytała niezgodnego wpisu.
- Każda replika co kilka sekund (konfigurowalne) odczytuje tylko ostatni `MigrationId`
  z `gateway.__EFMigrationsHistory`; pełne przeładowanie wyłącznie przy jego zmianie.
  Migracja niezmieniająca tras też powoduje przeładowanie, co jest nieszkodliwe
  (walidacja i podmiana tej samej konfiguracji).
- Baza niedostępna przy odpytywaniu: replika działa dalej na bieżącej konfiguracji.
- Baza niedostępna przy starcie: konfiguracja z L2 (ostatnia poprawna).
- Brak bazy i brak wpisu w L2: pod nie przechodzi startupProbe (ADR-0018).

### Co zostaje w kodzie

Elementy bezpieczeństwa wspólne dla wszystkich tras **nie są konfigurowalne z bazy**:
usuwanie nagłówków `X-User-*` i `Cookie`, dołączanie `Authorization: Bearer`, wymóg `X-CSRF`,
retry dla GET, definicje polityk autoryzacji i rate limitingu. Baza wskazuje polityki tylko po nazwie.

## Konsekwencje / wymagania techniczne

- **Zapis do tabel konfiguracji = możliwość przekierowania ruchu.** Użytkownik `gateway_app`
  ma do nich tylko `SELECT`; zapisuje wyłącznie `superapp_migrator` (dev/test) lub DBA (prod).
- Historia zmian: temporal tables na tabelach konfiguracji; autor i powód zmiany wynikają z historii repo.
- Tabele i dane początkowe tras tworzone migracjami kontekstu bramy, stosowanymi jak wszystkie
  migracje (ADR-0004).
- Propagacja zmiany do wszystkich replik trwa maksymalnie interwał odpytywania.
- Testy: ograniczenia bazy odrzucają błędne wiersze (w tym adresy spoza klastra); mapowanie odrzuca
  adresy spoza klastra i nieobsługiwane transformy; walidator odrzuca konfigurację z nieistniejącą
  polityką i zostawia poprzednią, a odrzucona wersja jest raportowana raz; start przy niedostępnej
  bazie korzysta z L2; nowa migracja przeładowuje trasy bez restartu.
- Metryki: identyfikator migracji aktywnej konfiguracji per replika, liczba nieudanych przeładowań
  (odrzucona wersja liczona raz).
- Zaostrzenie ograniczenia adresu (migracja `StrictDestinationAddress`: usunięcie i ponowne dodanie
  `CK_Destinations_ClusterAddress`) jest zgodne wstecz: dane początkowe (`http://{serwis}-api.{serwis}.svc.cluster.local:8080`)
  spełniają nową regułę.
