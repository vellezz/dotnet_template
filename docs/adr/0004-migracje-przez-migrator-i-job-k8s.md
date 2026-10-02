# ADR-0004: Migracje bazy przez jeden Migrator aplikacji i Job Kubernetes

- **Status:** Zaakceptowany
- **Data:** 2026-09-29
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §8, §12, §13; ADR-0003, ADR-0013, ADR-0021

## Kontekst

Wiele replik API/Workera uruchamiających migracje przy starcie prowadzi do wyścigu.
Produkcja wymaga możliwości przeglądu skryptu przez DBA. System jest jedną aplikacją
wydawaną razem, ze wspólną bazą i schematem per serwis (ADR-0021).

## Decyzja

- **Jeden projekt `SuperApp.Migrator`** (aplikacja konsolowa, jeden obraz, jeden Job) migruje
  wszystkie schematy: `WriteDbContext` każdego serwisu oraz kontekst bramy (`gateway`).
- **Migracje zostają przy serwisie:** w `{Serwis}.Infrastructure` (folder `Migrations/`),
  domyślny `MigrationsAssembly`. Serwis jest właścicielem swoich migracji, a Migrator tylko je
  uruchamia.
- `SuperApp.Migrator` referuje projekty Infrastructure wszystkich serwisów i bramy, rejestruje ich
  `WriteDbContext` i wywołuje `Database.MigrateAsync()` w ustalonej kolejności. Każdy kontekst
  ma własną tabelę historii migracji w swoim schemacie (ADR-0021).
- **Dev i test: wszystkie migracje przez `SuperApp.Migrator`.** Job k8s `superapp-migrator` uruchamiany
  przed rolloutem serwisów (hook `PreSync` / sync-wave w ArgoCD). Rollout rusza dopiero po
  sukcesie Joba.
- **Prod: wszystkie migracje przez DBA.** Pipeline wydania generuje
  `dotnet ef migrations script --idempotent` dla każdego kontekstu i łączy je w jeden artefakt
  wydania. DBA przegląda i uruchamia skrypt **przed** wdrożeniem nowej wersji aplikacji.
  Na prod Job `superapp-migrator` nie jest wdrażany.
- **Kontrola na starcie (wszystkie środowiska):** API, Worker i brama przy starcie sprawdzają
  `GetPendingMigrationsAsync()` swojego kontekstu. Brak zastosowanych migracji = pod nie
  przechodzi startupProbe (ADR-0018), rollout się zatrzymuje, a poprzednia wersja dalej obsługuje ruch.
  Serwis tylko sprawdza, nigdy nie migruje.
- Migracje w stylu **expand/contract**; operacje destrukcyjne tylko w fazie „contract”.
- Nigdy migracji przy starcie API/Workera.

## Konsekwencje / wymagania techniczne

- **Wspólny cykl wydań:** wersja Migratora obejmuje migracje wszystkich serwisów. Wdrożenie
  dowolnego serwisu wymaga wcześniejszego uruchomienia Migratora z tej samej wersji aplikacji.
  Już zastosowane migracje są pomijane, więc Job jest idempotentny.
- Błąd migracji jednego schematu zatrzymuje Job i rollout całej aplikacji; kolejność kontekstów
  i miejsce przerwania widoczne w logach (`[LoggerMessage]`).
- Tworzenie migracji: `dotnet ef migrations add <Nazwa> -p src/Services/{Serwis}/{Serwis}.Infrastructure
  -s src/Services/{Serwis}/{Serwis}.Infrastructure --context {Serwis}WriteDbContext -o Migrations`
  (kontekst tworzy `IDesignTimeDbContextFactory` w Infrastructure; dla bramy `-p src/Gateway/SuperApp.Gateway`).
- `SuperApp.Migrator` łączy się connection stringiem `Migrator` (użytkownik `superapp_migrator`).
- `SuperApp.Migrator` jest jedynym miejscem referującym Infrastructure wielu serwisów; nie zawiera
  logiki i nie może być referowany przez inne projekty (test architektury).
- **Sekrety a hooki:** przy pierwszej instalacji Job `PreSync` startuje przed zasobami fazy Sync,
  więc Secret z connection stringiem Migratora musi powstać wcześniej (osobna sync-wave).
- Obraz: non-root, read-only root filesystem.
- Jeden login migracyjny `superapp_migrator` z uprawnieniami DDL i DML (migracje danych, ADR-0021),
  **tylko na dev i test**. Na prod w klastrze nie ma żadnego konta z uprawnieniami DDL;
  DBA używa własnych uprawnień. Loginy serwisów tylko DML na własnym schemacie.
- Kolejność wdrożenia na prod: skrypt DBA → wdrożenie aplikacji. Dzięki expand/contract
  poprzednia wersja działa poprawnie na schemacie po skrypcie.
- Artefakt skryptu jest wersjonowany razem z wydaniem; skrypt jest idempotentny, więc
  ponowne uruchomienie jest bezpieczne.
