# ADR-0034: Lokalne środowisko deweloperskie w docker compose

- **Status:** Zaakceptowany
- **Data:** 2026-09-30
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §12, §13; ADR-0004, ADR-0016, ADR-0035, ADR-0021, ADR-0022, ADR-0031

## Kontekst

Na środowiskach dev/test/prod infrastrukturę i CIAM dostarczają inne działy (ADR-0016). Zespół potrzebuje na własnej maszynie
kompletnego zestawu: wszystkich serwisów, obu bram, Migratora oraz infrastruktury, żeby pracować nad dowolnym fragmentem systemu
i uruchamiać testy end-to-end bez zależności od środowisk współdzielonych.

## Decyzja

- Jeden plik `deploy/local/docker-compose.yml` z dwoma poziomami:
  - **infrastruktura** (zawsze): Keycloak (lokalny CIAM, ADR-0031), MSSQL, Redis, RabbitMQ i jednorazowy `db-bootstrap`;
  - **profil `app`**: `migrator` (SuperApp.Migrator), `db-gateway-permissions` (skrypt `02-gateway-config-permissions.sql`), API i Workery
    wszystkich serwisów, `bff-web` i `gateway-mobile`.
- **Dwa tryby pracy:** wszystko w kontenerach (`--profile app`) albo tryb hybrydowy: sama infrastruktura w kontenerach, a serwis lub brama
  z IDE (`appsettings.Development.json`, profile `launchSettings`). Porty bram w kontenerach są takie jak w `launchSettings`
  (5000/5001, 5002), więc realm Keycloaka i back-channel logout działają w obu trybach bez zmian.
- **Uprawnienia jak na dev/test:** `db-bootstrap` zakłada bazę i loginy (`superapp_migrator`, `{serwis}_app`) i uruchamia wspólny
  `deploy/sql/01-bootstrap.sql`; serwisy łączą się własnymi loginami, więc izolacja schematów (ADR-0021) działa lokalnie.
- **Trasy bramy bez zmian:** kontenery API mają aliasy sieciowe równe adresom Service w klastrze
  (`knowledge-api.knowledge.svc.cluster.local`), więc konfiguracja tras z migracji (ADR-0022) działa lokalnie tak samo.
- **Jeden issuer tokenów:** Keycloak ma `KC_HOSTNAME=http://localhost:8081` i `KC_HOSTNAME_BACKCHANNEL_DYNAMIC=true`: przeglądarka
  loguje się przez `localhost:8081`, kontenery pobierają metadane, klucze i tokeny przez `keycloak:8080`, a issuer jest zawsze ten sam.
- **Sekrety lokalne:** hasła w `deploy/local/.env` (w `.gitignore`;
  wzór `.env.example`). Brama w kontenerze używa certyfikatu deweloperskiego HTTPS (`deploy/local/dev-cert.ps1` / `dev-cert.sh`),
  bo ciasteczko `__Host-bff` wymaga HTTPS.
- **Bez stosu obserwowalności** lokalnie: eksport OTLP jest wyłączony, logi w konsoli kontenerów.
- Redis jest wystawiony na porcie hosta 6380 (kontenery używają `redis:6379`), żeby nie kolidować z innymi lokalnymi instancjami.

## Konsekwencje

- Pierwsze uruchomienie profilu `app` buduje obrazy wszystkich hostów z Dockerfile'i projektów (te same obrazy co na środowiskach).
- Nowy serwis wymaga dopisania jego kontenerów (API z aliasem, Worker) do pliku compose i loginu do `db/bootstrap.sh`
  (lista kroków w README).
- Dane MSSQL są trwałe w wolumenie `mssql-data`; `docker compose down -v` przywraca stan początkowy bazy.
- Keycloak (`start-dev`) nie ma wolumenu: realm jest importowany z pliku przy każdym utworzeniu kontenera, więc zmianę
  `realm-superapp.json` ładuje `docker compose up -d --force-recreate keycloak` (bez utraty danych MSSQL).
