# 13. Lokalne środowisko i debugowanie

**Czego się nauczysz.** Co dokładnie uruchamia `deploy/local/docker-compose.yml` i dlaczego jest tak zbudowany (aliasy
`*.svc.cluster.local`, jeden issuer Keycloaka, loginy bazy, Migrator, certyfikat); jak pracować w trybie hybrydowym (jeden
element z IDE, reszta w kontenerach); jak postawić breakpoint w handlerze i dojść do niego przez API serwisu, przez BFF
experience i przez bramę; jak zajrzeć
do sesji, outboxa i inboxa w MSSQL, do kolejek RabbitMQ i do Redis; jak czytać logi i jak zresetować stan; czym lokalne środowisko
różni się od klastra (lokalna brama jako zamiennik wspólnej bramy, brak NetworkPolicy).

**Wymagania wstępne.** [Start](01-start.md) (narzędzia, pierwsze uruchomienie), [Architektura w praktyce](02-architektura-w-praktyce.md)
(jakie procesy istnieją i jak rozmawiają). Decyzje: ADR-0031 (lokalny CIAM), ADR-0034 (lokalne środowisko), ADR-0037 (lokalna brama
jako zamiennik wspólnej), ADR-0041 (NetworkPolicy).

> **W skrócie**
>
> - `docker compose -f deploy/local/docker-compose.yml up -d`: sama infrastruktura (Keycloak, MSSQL, Redis, RabbitMQ, bootstrap bazy).
>   Z `--profile app` dodatkowo Migrator, oba API, oba Workery, BFF experience `example-bff`, obie bramy i forwarder analityki,
>   zbudowane z tych samych
>   Dockerfile'i co na klastrze. Analityka (PostHog) jest lokalnie wyłączona, dopóki nie ustawisz `ANALYTICS_*` w `.env`.
> - Kontenery API i BFF mają aliasy z adresami Service w klastrze (`{serwis}-api.{serwis}.svc.cluster.local`,
>   `example-bff.example.svc.cluster.local`): brama przyjmuje tylko takie adresy (ograniczenie `CHECK` w bazie), a BFF w kontenerze
>   woła serwisy pod tymi samymi adresami co na klastrze.
> - Keycloak ma `KC_HOSTNAME=http://localhost:8081`: issuer tokenów jest ten sam dla przeglądarki, IDE i kontenerów.
> - Serwisy łączą się loginami `{serwis}_app` jak na klastrze; `sa` używają tylko Migrator i brama uruchamiane z IDE.
> - Tryb hybrydowy: zatrzymaj kontener elementu, który debugujesz, i uruchom go z IDE (te same porty, `appsettings.Development.json`).
> - Najprostsza droga do breakpointu w handlerze: API z IDE + token `dev-cli` + `curl` na port 5101/5102. To droga wyłącznie do
>   debugowania: moduł woła tylko BFF przez bramę (`https://localhost:5001/api/example/v1/...`).
> - `docker compose ... down -v` przywraca stan początkowy (baza w wolumenie `mssql-data`).
> - Kontenery `bff-web` i `gateway-mobile` to **lokalny zamiennik wspólnej bramy brzegowej** super appki. Mają jedną trasę:
>   `/api/example/v{n}/**` do BFF experience; do serwisów domenowych i do `/internal` brama nie kieruje ruchu. Compose **nie
>   egzekwuje NetworkPolicy** (rozdział 12).

---

## Szybki start: `dotnet superapp env`

```bash
dotnet superapp env up [--build] [--infra]   # start (--infra: tylko MSSQL, RabbitMQ, Redis, Keycloak) i czekanie na wszystkie komponenty
dotnet superapp env status                   # kontenery (stan, health, jednorazowe z kodem wyjścia) i sondy HTTP; kod 2, gdy coś nie działa
dotnet superapp e2e                          # scenariusz end-to-end: brama, BFF, API wewnętrzne, kody błędów, zapis przez bramę, logowanie bff-web
dotnet superapp env token reader --scope knowledge.catalog.read   # token użytkownika lokalnego realmu do curl
dotnet superapp env down [--reset]           # stop; --reset usuwa wolumeny (baza od zera)
```

Polecenia `docker compose` poniżej robią to samo ręcznie. [22 Narzędzie](22-narzedzie-superapp.md).

## 1. Dwa tryby pracy

| | Wszystko w kontenerach | Tryb hybrydowy |
|---|---|---|
| Polecenie | `docker compose -f deploy/local/docker-compose.yml --profile app up -d --build` | `docker compose -f deploy/local/docker-compose.yml up -d`, potem Migrator i wybrane projekty z IDE |
| Kiedy | testy end-to-end, sprawdzenie zmiany przekrojowej przed PR, praca nad frontendem | debugowanie serwisu, Workera, BFF lub bramy, szybkie iteracje bez budowania obrazów |
| Konfiguracja procesów | zmienne środowiskowe w `docker-compose.yml` | `appsettings.Development.json` + profil z `Properties/launchSettings.json` |
| Login do bazy | `{serwis}_app`, `gateway_app`, `superapp_migrator` | serwisy: `{serwis}_app`; brama i Migrator z IDE: `sa` |
| Migracje | kontener `migrator` przy każdym `up` | ręcznie: `dotnet run --project src/Migrator/SuperApp.Migrator --launch-profile local` |

Można je łączyć: całość w kontenerach, a jeden kontener zatrzymany i zastąpiony procesem z IDE (rozdział 9).

---

## 2. Kontenery po kolei

Plik: `deploy/local/docker-compose.yml`, projekt compose `superapp-local` (sieć `superapp-local_default`, wolumen `superapp-local_mssql-data`).

```mermaid
flowchart TD
    mssql[mssql<br/>healthcheck: SELECT 1] --> dbb[db-bootstrap<br/>baza, loginy, schematy]
    dbb --> mig[migrator<br/>profil app]
    mig --> perm[db-gateway-permissions<br/>profil app]
    mig --> api[knowledge-api, sleepdiary-api<br/>profil app]
    mig --> wrk[knowledge-worker, sleepdiary-worker<br/>profil app]
    api -.-> bff[example-bff<br/>profil app]
    kc -.-> bff
    rmq[rabbitmq<br/>healthcheck] --> api
    rmq --> wrk
    redis[redis<br/>healthcheck] --> api
    redis --> wrk
    kc[keycloak<br/>bez healthchecku] -.-> api
    kc -.-> wrk
    perm --> gw[bff-web, gateway-mobile<br/>profil app]
    rmq --> fwd[analytics-forwarder<br/>profil app]
    redis --> gw
    kc -.-> gw
```

Linie ciągłe to `depends_on` z warunkiem (`service_healthy` albo `service_completed_successfully`); przerywane to
`service_started` (Keycloak nie ma healthchecku, więc kontenery startują, zanim realm jest gotowy; pierwsze żądania mogą przez
kilka sekund kończyć się `401`, bo serwis nie pobrał jeszcze metadanych OIDC).

### 2.1 Infrastruktura (zawsze)

| Usługa | Obraz | Porty hosta | Rola | Uwagi |
|---|---|---|---|---|
| `keycloak` | `quay.io/keycloak/keycloak:26.7` | `8081` → 8080 | lokalny CIAM, realm `app` z `keycloak/realm-superapp.json` | `start-dev --import-realm`; konsola `admin` / `admin`; `extra_hosts: host.docker.internal` dla back-channel logout |
| `mssql` | `mcr.microsoft.com/mssql/server:2022-latest` | `1433` | baza `SuperApp` | hasło `sa` z `DB_SA_PASSWORD` (domyślnie `Dev!Passw0rd1`); dane w wolumenie `mssql-data` |
| `db-bootstrap` | jw. (tylko `sqlcmd`) | | jednorazowo: baza, loginy, schematy, role | kończy się `Exited (0)`; uruchamia się przy każdym `up` (jest idempotentny) |
| `redis` | `redis:7.4-alpine` | `6380` → 6379 | L2 cache | port hosta 6380, żeby nie kolidować z innym lokalnym Redisem |
| `rabbitmq` | `rabbitmq:4-management` | `5672`, `15672` | broker + panel | `guest` / `guest` |

### 2.2 Aplikacja (profil `app`)

| Usługa | Dockerfile | Porty hosta | Zależy od | Zmienne kluczowe |
|---|---|---|---|---|
| `migrator` | `src/Migrator/SuperApp.Migrator/Dockerfile` | | `db-bootstrap` zakończony | `ConnectionStrings__Migrator` z loginem `superapp_migrator` |
| `db-gateway-permissions` | obraz MSSQL, `sqlcmd -i /sql/02-gateway-config-permissions.sql` | | `migrator` zakończony | `DENY INSERT, UPDATE, DELETE` na tabelach tras dla `gateway_role` |
| `knowledge-api` | `src/Services/Knowledge/Knowledge.Api/Dockerfile` | `5101` → 8080 | migrator, rabbitmq, redis, keycloak | `Write`/`Read` z `knowledge_app`; alias `knowledge-api.knowledge.svc.cluster.local` |
| `knowledge-worker` | `.../Knowledge.Worker/Dockerfile` | brak | jw. | jw., bez aliasu |
| `sleepdiary-api` | `.../SleepDiary.Api/Dockerfile` | `5102` → 8080 | jw. | `sleepdiary_app`; alias `sleepdiary-api.sleepdiary.svc.cluster.local` |
| `sleepdiary-worker` | `.../SleepDiary.Worker/Dockerfile` | brak | jw. | |
| `example-bff` | `src/Bff/Example.Bff/Dockerfile` | `5120` → 8080 | `knowledge-api`, `sleepdiary-api`, keycloak (`service_started`) | alias `example-bff.example.svc.cluster.local`; `Downstream__Knowledge__BaseAddress` i `Downstream__SleepDiary__BaseAddress` na aliasy serwisów; bez bazy, Redisa i RabbitMQ |
| `bff-web` | `src/Gateway/SuperApp.Gateway/Dockerfile` | `5001` → 8443 (HTTPS), `5000` → 8080 (HTTP) | `db-gateway-permissions`, redis, keycloak | `Gateway__Profile: bff-web`, sekret klienta `dev-bff-web-secret`, lista scope'ów |
| `gateway-mobile` | jw. | `5002` → 8443 | jw. | `Gateway__Profile: gateway-mobile`, `Authentication__Audience: gateway-mobile` |
| `analytics-forwarder` | `src/Analytics/SuperApp.AnalyticsForwarder/Dockerfile` | brak | rabbitmq | `ConnectionStrings__RabbitMq`, `Analytics__ProjectToken`, `Analytics__IdKey`; bez tokenu tylko loguje zdarzenia (EventId 9001) |

Wspólne zmienne serwisów są w kotwicy YAML na początku pliku:

```yaml
x-service-env: &service-env
  ASPNETCORE_ENVIRONMENT: Development
  Authentication__Authority: http://keycloak:8080/realms/superapp
  Authentication__RequireHttpsMetadata: "false"
  ConnectionStrings__RabbitMq: amqp://guest:guest@rabbitmq:5672/
  ConnectionStrings__Redis: redis:6379
```

Serwisy, obie bramy i forwarder dostają też kotwicę `analytics-env`: `Analytics__ProjectToken: ${ANALYTICS_PROJECT_TOKEN:-}`
i `Analytics__IdKey: ${ANALYTICS_ID_KEY:-}`. Bez `deploy/local/.env` obie są puste, więc analityka jest wyłączona: nic nie idzie do
PostHog, `/ingest` w `bff-web` nie istnieje, `analyticsId` jest `null`, a feature flags pochodzą z konfiguracji
(`FeatureFlags__{klucz}`) ([21.12](21-analityka-i-feature-flags.md#2112-lokalnie-krok-po-kroku)).

`Authentication__Audience` nie ma w compose: serwis bierze go z własnego `appsettings.json` (`knowledge-api`, `sleepdiary-api`,
w BFF `example-bff`).
Eksport OTLP jest lokalnie wyłączony (brak `OTEL_EXPORTER_OTLP_ENDPOINT`), więc logi są tylko w konsoli kontenerów.

---

## 3. Dlaczego aliasy `*.svc.cluster.local`

Trasy bramy są w bazie i trafiają tam migracją (ADR-0022). Adres klastra `knowledge` w danych początkowych to
`http://knowledge-api.knowledge.svc.cluster.local:8080`, a ograniczenie `CK_Destinations_ClusterAddress`
(`src/Gateway/SuperApp.Gateway/Persistence/Configurations/ProxyChecks.cs`) przyjmuje **wyłącznie** adresy w postaci
`http://{serwis}.{namespace}.svc.cluster.local:{port}`. Adresu `http://localhost:5101` nie da się więc wpisać nawet
migracją, i to celowo: brama nie może kierować ruchu poza klaster.

Ten sam adres w postaci Service ma BFF (`http://example-bff.example.svc.cluster.local:8080`, jedyny cel trasy bramy), a BFF
w kontenerze woła serwisy pod `http://knowledge-api.knowledge.svc.cluster.local:8080` i
`http://sleepdiary-api.sleepdiary.svc.cluster.local:8080` (`Downstream__{Serwis}__BaseAddress` w compose, jak w chartcie `superapp-bff`).

Rozwiązaniem jest alias w sieci Dockera:

```yaml
  knowledge-api:
    ports:
      - "5101:8080"
    networks:
      default:
        # Ten sam adres co Service w klastrze: trasy bramy z migracji działają lokalnie bez zmian (ADR-0022).
        aliases: [knowledge-api.knowledge.svc.cluster.local]
```

Konsekwencje, o których trzeba pamiętać:

- alias istnieje tylko **wewnątrz** sieci `superapp-local_default`; proces na hoście (np. brama z IDE) tej nazwy nie rozwiąże;
- alias istnieje tylko, gdy kontener działa; po `docker compose stop knowledge-api` BFF w kontenerze dostaje błąd DNS i
  zwraca `503 downstream.unavailable`, dopóki ktoś inny nie przejmie aliasu (przepis 9.2); po `docker compose stop example-bff`
  brama zwraca `502` (YARP);
- port `8080` w adresie to port kontenera, nie hosta.

---

## 4. Keycloak: jeden issuer dla wszystkich

```yaml
  keycloak:
    image: quay.io/keycloak/keycloak:26.7
    command: ["start-dev", "--import-realm"]
    environment:
      KC_BOOTSTRAP_ADMIN_USERNAME: admin
      KC_BOOTSTRAP_ADMIN_PASSWORD: admin
      # Jeden issuer dla przeglądarki i kontenerów: adresy frontowe (issuer, logowanie) wskazują localhost:8081,
      # a kontenery pobierają metadane, klucze i tokeny przez http://keycloak:8080.
      KC_HOSTNAME: http://localhost:8081
      KC_HOSTNAME_BACKCHANNEL_DYNAMIC: "true"
```

Problem, który to rozwiązuje: przeglądarka widzi Keycloaka pod `localhost:8081`, a kontenery pod `keycloak:8080`. Bez
`KC_HOSTNAME` token wydany przeglądarce miałby `iss = http://localhost:8081/realms/superapp`, a serwis w kontenerze, pytając
`http://keycloak:8080/...`, oczekiwałby `iss = http://keycloak:8080/realms/superapp`: każdy token byłby odrzucony.

```mermaid
sequenceDiagram
    participant B as Przeglądarka / curl na hoście
    participant KC as Keycloak
    participant S as knowledge-api (kontener)

    B->>KC: http://localhost:8081/realms/superapp/protocol/openid-connect/token
    KC-->>B: access token, iss = http://localhost:8081/realms/superapp
    S->>KC: http://keycloak:8080/realms/superapp/.well-known/openid-configuration
    KC-->>S: issuer = http://localhost:8081/realms/superapp (stały, KC_HOSTNAME)<br/>jwks_uri, token_endpoint = http://keycloak:8080/... (dynamiczne)
    B->>S: Bearer (iss zgodny z metadanymi) → 200
```

Zawartość realmu (szczegóły w ADR-0031):

| Element | Wartość |
|---|---|
| Użytkownicy | `reader` / `reader`; `editor` / `editor` (rola `knowledge-editor`, jedyna, która dostaje `knowledge.catalog.write`) |
| `bff-web` | confidential, sekret `dev-bff-web-secret`, redirect `https://localhost:5001/signin-oidc`, back-channel logout `http://host.docker.internal:5000/bff/backchannel-logout` |
| `dev-cli` | public, password grant: **tylko lokalnie**, do tokenów dla `curl` i narzędzi HTTP; domyślnie dostaje audience `example-bff`, a na żądanie scope `example.internal.read` |
| Client scope `example-bff-audience` | domyślny dla `bff-web`, `mobile-*` i `dev-cli`; dodaje audience `example-bff`, więc każdy token użytkownika jest ważny dla BFF (ADR-0040) |
| Client scope `example.internal.read` | opcjonalny tylko dla `dev-cli`; scope API wewnętrznego BFF (`/internal/v1/...`, ADR-0039), też z audience `example-bff`. Lokalnie zastępuje klienta BFF innej experience |
| Tokeny | access token 5 min (`accessTokenLifespan: 300`), sesja SSO do 8 h, bezczynność 30 min, rotacja refresh tokenów jednorazowych |

Uwaga: scope, którego użytkownik nie może dostać, Keycloak **po cichu pomija**. Token `reader` z żądanym
`knowledge.catalog.write` ma tylko `openid profile email`; wywołanie komendy zwróci wtedy `403 auth.missing_scope`, a nie błąd
logowania. Sprawdź zawartość tokenu (przepis 9.1, krok 2).

Stan Keycloaka (tryb `start-dev`) żyje w kontenerze, nie w wolumenie: po `docker compose down` (także bez `-v`) realm jest
importowany od nowa z pliku, a zmiany wyklikane w konsoli znikają. Trwała zmiana realmu = zmiana `realm-superapp.json`.

---

## 5. Baza: bootstrap, loginy, uprawnienia

`deploy/local/db/bootstrap.sh` (uruchamiany przez `db-bootstrap`):

```bash
sqlcmd -Q "IF DB_ID(N'SuperApp') IS NULL CREATE DATABASE [SuperApp];"

# Wait until SuperApp is ONLINE *and* started (Collation is NULL until then), checked from master.
ready=""
for attempt in $(seq 1 90); do
  state=$(sqlcmd -h -1 -W -Q "SET NOCOUNT ON; SELECT CONCAT(CONVERT(nvarchar(60), DATABASEPROPERTYEX(N'SuperApp', 'Status')), '|', IIF(DATABASEPROPERTYEX(N'SuperApp', 'Collation') IS NULL, 'not-started', 'started'));" | tr -d '[:space:]')
  if [ "$state" = "ONLINE|started" ]; then
    ready=1
    break
  fi
  sleep 2
done
# (gdy baza nie wystartuje w 180 s: komunikat i exit 1)

for login in superapp_migrator gateway_app knowledge_app sleepdiary_app; do
  sqlcmd -Q "IF SUSER_ID(N'${login}') IS NULL CREATE LOGIN [${login}] WITH PASSWORD = N'${DB_APP_PASSWORD}', CHECK_POLICY = OFF;"
done

sqlcmd -d SuperApp -i /sql/01-bootstrap.sql -v IncludeMigrator=1
```

**Dlaczego czekanie sprawdza dwie rzeczy.** Po restarcie kontenera SQL Server przyjmuje logowania `sa` (healthcheck
`SELECT 1` przechodzi), zanim uruchomi bazy użytkowników. W tym oknie `DATABASEPROPERTYEX(N'SuperApp', 'Status')` potrafi już
zwrócić `ONLINE`, ale baza nie jest jeszcze uruchomiona (`Collation` = `NULL`). Połączenie z `-d SuperApp` kończy się wtedy
`Login failed for user 'sa'. Reason: Failed to open the explicitly specified database 'SuperApp'` (błąd 18456, stan 38),
`db-bootstrap` wychodzi z kodem 1, a za nim nie startuje Migrator ani żaden serwis. Sprawdzanie z bazy `master` nie zostawia
w logu serwera nieudanych logowań.

Wspólny skrypt `deploy/sql/01-bootstrap.sql` (ten sam, którego używa DBA) zakłada dla każdego schematu z listy `@Services`
schemat, rolę `{schemat}_role` z `SELECT, INSERT, UPDATE, DELETE, EXECUTE` **tylko na tym schemacie** i użytkownika
`{schemat}_app` z domyślnym schematem. `superapp_migrator` dostaje `db_ddladmin`, `db_datareader`, `db_datawriter`
(`IncludeMigrator=1`; na prod `0`, bo tam migruje DBA).

| Login | Kto używa lokalnie | Może |
|---|---|---|
| `sa` | Migrator i brama z IDE (`launchSettings` / `appsettings.Development.json`), Ty w SSMS | wszystko |
| `superapp_migrator` | kontener `migrator` | DDL i dane we wszystkich schematach |
| `knowledge_app` | `knowledge-api`, `knowledge-worker` (kontener i IDE) | DML w `knowledge` |
| `sleepdiary_app` | `sleepdiary-*` | DML w `sleepdiary` |
| `gateway_app` | `bff-web`, `gateway-mobile` w kontenerach | DML w `gateway`, z wyjątkiem tabel tras (`DENY` z `db-gateway-permissions`) |

Dzięki temu lokalnie widać te same błędy uprawnień co na klastrze: zapytanie serwisu do cudzego schematu kończy się
`SqlException` „The SELECT permission was denied…”, a nie działa „przypadkiem”.

---

## 6. Migrator lokalnie

`SuperApp.Migrator` migruje po kolei `GatewayDbContext`, `KnowledgeWriteDbContext`, `SleepDiaryWriteDbContext`. Logi:

```text
info: SuperApp.Migrator[4001]
      Migrating KnowledgeWriteDbContext: 1 pending migration(s)
info: SuperApp.Migrator[4002]
      Migrated KnowledgeWriteDbContext
```

| Sytuacja | Polecenie |
|---|---|
| Tryb hybrydowy, po `up` albo po dodaniu migracji | `dotnet run --project src/Migrator/SuperApp.Migrator --launch-profile local` (login `sa`) |
| Profil `app`, nowa migracja w kodzie | `docker compose -f deploy/local/docker-compose.yml --profile app up -d --build migrator`, potem przebuduj i uruchom ponownie serwis, którego dotyczy zmiana |
| Sprawdzenie, które migracje są zastosowane | `dotnet ef migrations list -p src/Services/Knowledge/Knowledge.Infrastructure -s src/Services/Knowledge/Knowledge.Infrastructure --context KnowledgeWriteDbContext --connection "Server=localhost,1433;Database=SuperApp;User Id=sa;Password=Dev!Passw0rd1;TrustServerCertificate=True"` (oczekujące mają dopisek `(Pending)`) |

Lokalnie nic nie blokuje serwisu z oczekującymi migracjami: docker compose nie używa `/health/startup`. Serwis wystartuje, a
pierwsze zapytanie do nowej kolumny skończy się `500` z `SqlException` „Invalid column name”. Zawsze uruchom Migratora po
pobraniu cudzych zmian z migracjami.

---

## 7. Porty, `.env`, certyfikat

| Port hosta | Usługa | W trybie hybrydowym zajmuje go |
|---|---|---|
| 5000 / 5001 | `bff-web` HTTP / HTTPS | profil `bff-web` bramy z IDE (`https://localhost:5001;http://localhost:5000`) |
| 5002 | `gateway-mobile` | profil `gateway-mobile` z IDE |
| 5101 / 5102 | Knowledge API / SleepDiary API | profile `knowledge-api` / `sleepdiary-api` z IDE |
| 5120 | `example-bff` | profil `example-bff` z IDE (`http://localhost:5120`) |
| 5111 / 5112 | (brak w kontenerach) | Knowledge / SleepDiary Worker z IDE (tylko `/health/*`) |
| 5180 | (brak w kontenerach) | `SuperApp.AnalyticsForwarder` z IDE, profil `analytics-forwarder` (tylko `/health/*`) |
| 8081 | Keycloak | |
| 1433 | MSSQL | |
| 5672 / 15672 | RabbitMQ AMQP / panel | |
| 6380 | Redis | |

Porty bram w kontenerach są takie same jak w `launchSettings`, dlatego realm (redirect URI, back-channel logout) działa dla
obu wariantów, ale **nie jednocześnie**: przed uruchomieniem bramy z IDE zatrzymaj kontener.

**`.env`.** Opcjonalny `deploy/local/.env` (w `.gitignore`, wzór `.env.example`) nadpisuje `DB_SA_PASSWORD`, `DB_APP_PASSWORD`,
`DEV_CERT_PASSWORD`, a także `ANALYTICS_PROJECT_TOKEN` i `ANALYTICS_ID_KEY` (domyślnie puste = analityka wyłączona; ustawiaj
tylko wartości **deweloperskiego** projektu PostHog, klucz pseudonimu min. 32 znaki). Wartości domyślne działają. Jeśli zmienisz hasła, `appsettings.Development.json` projektów nadal mają
`Dev!Passw0rd1`: w trybie hybrydowym nadpisz je np. przez `dotnet user-secrets` albo zmienne środowiskowe
(`ConnectionStrings__Write=...`). Hasło `sa` musi spełniać politykę złożoności MSSQL, inaczej kontener `mssql` nie wstanie.

**Certyfikat.** Ciasteczko `__Host-bff` wymaga HTTPS (przeglądarka odrzuca ciasteczko z prefiksem `__Host-` bez `Secure`),
dlatego brama w kontenerze słucha na 8443 z certyfikatem deweloperskim:

```bash
./deploy/local/dev-cert.sh          # Windows: deploy/local/dev-cert.ps1
# dotnet dev-certs https --trust
# dotnet dev-certs https -ep deploy/local/certs/devcert.pfx -p dev-cert-password
```

Kontener czyta `/certs/devcert.pfx` (wolumen `./certs`) z hasłem `DEV_CERT_PASSWORD`. Brama z IDE używa certyfikatu
deweloperskiego Kestrela bezpośrednio.

---

## 8. Ustawienia procesów z IDE

| Projekt | Profil `launchSettings` | Adres | `appsettings.Development.json` |
|---|---|---|---|
| `Knowledge.Api` | `knowledge-api` | `http://localhost:5101` | `Authority=http://localhost:8081/realms/superapp`, `RequireHttpsMetadata=false`, `Write`/`Read` z `knowledge_app`, `RabbitMq=amqp://guest:guest@localhost:5672/`, `Redis=localhost:6380` |
| `Knowledge.Worker` | `knowledge-worker` | `http://localhost:5111` | jw. |
| `SleepDiary.Api` | `sleepdiary-api` | `http://localhost:5102` | jw. dla `sleepdiary_app` |
| `SleepDiary.Worker` | `sleepdiary-worker` | `http://localhost:5112` | jw. |
| `Example.Bff` | `example-bff` | `http://localhost:5120` | `Authority` jak wyżej; `Downstream:Knowledge:BaseAddress=http://localhost:5101`, `Downstream:SleepDiary:BaseAddress=http://localhost:5102` (działa z serwisami z IDE i z kontenerów, bo kontenery publikują te porty); bez bazy |
| `SuperApp.Gateway` | `bff-web` | `https://localhost:5001;http://localhost:5000` | `Authority` jak wyżej, `ConnectionStrings:Gateway` z `sa`; sekret i scope'y w zmiennych profilu |
| `SuperApp.Gateway` | `gateway-mobile` | `https://localhost:5002` | jw., `Authentication__Audience=gateway-mobile` |
| `SuperApp.Migrator` | `local` | | `ConnectionStrings__Migrator` z `sa` |
| `SuperApp.AnalyticsForwarder` | `analytics-forwarder` | `http://localhost:5180` | `RabbitMq=amqp://guest:guest@localhost:5672/`; bez `Analytics__*` (analityka wyłączona, log 9001) |

Procesy z IDE nie czytają `deploy/local/.env`: analitykę albo flagi (`FeatureFlags__{klucz}=true`) ustawiasz im zmiennymi profilu.

Profil ustawia `ASPNETCORE_ENVIRONMENT=Development`. Uruchomienie bez profilu (np. `dotnet run` z innym środowiskiem) wczyta
tylko `appsettings.json` z pustym `Authority`: serwis wystartuje, ale nie zna CIAM (brak kluczy podpisu), więc każde żądanie
z tokenem skończy się `401`.

---

## 9. Przepisy debugowania

### 9.1 Breakpoint w handlerze komendy, wywołanie bezpośrednio do API

Najkrótsza pętla: bez bramy, bez przeglądarki.

1. Infrastruktura i Migrator:
   ```bash
   docker compose -f deploy/local/docker-compose.yml up -d
   dotnet run --project src/Migrator/SuperApp.Migrator --launch-profile local
   ```
   Jeśli działa profil `app`, zatrzymaj tylko kontener API: `docker compose -f deploy/local/docker-compose.yml stop knowledge-api`.
2. Token (Git Bash):
   ```bash
   TOKEN=$(curl -s http://localhost:8081/realms/superapp/protocol/openid-connect/token \
     -d grant_type=password -d client_id=dev-cli -d username=editor -d password=editor \
     -d "scope=openid knowledge.catalog.read knowledge.catalog.write" \
     | python -c "import sys,json;print(json.load(sys.stdin)['access_token'])")
   ```
   Sprawdzenie zawartości (bez wysyłania tokenu na zewnątrz):
   ```bash
   echo $TOKEN | cut -d. -f2 | python -c "import sys,base64,json;s=sys.stdin.read().strip();s+='='*(-len(s)%4);d=json.loads(base64.urlsafe_b64decode(s));print(d['iss'],d['aud'],d['scope'])"
   # http://localhost:8081/realms/superapp ['knowledge-api', 'gateway-mobile', 'example-bff', 'account'] openid knowledge.catalog.read knowledge.catalog.write profile email
   ```
   `aud` musi zawierać `knowledge-api` (dla BFF: `example-bff`), a `scope` wymagany scope komendy. Token żyje 5 minut.
3. Uruchom `Knowledge.Api` z IDE w trybie debug (profil `knowledge-api`), postaw breakpoint w
   `CreateCategoryHandler.Handle` (albo w `TransactionBehavior.Handle`, żeby zobaczyć cały przebieg).
4. Wywołanie:
   ```bash
   curl -i -X POST http://localhost:5101/v1/categories \
     -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
     -d '{"name":"Higiena snu","slug":"higiena-snu"}'
   ```
5. Co zobaczysz w debuggerze, krok po kroku: `LoggingBehavior` → `AuthorizationBehavior` (scope z tokenu) → `ValidationBehavior`
   → `TransactionBehavior` (otwarta transakcja) → handler → po powrocie `SaveChangesAsync` w `WriteDbContextBase`
   (zdarzenia domenowe, potem `base.SaveChangesAsync`) → `CommitAsync` → `AfterCommitInterceptor`.

Dla SleepDiary: port 5102, scope `sleepdiary.entry.read sleepdiary.entry.write`, np.
`curl -i "http://localhost:5102/v1/entries?from=2026-09-01&to=2026-09-30" -H "Authorization: Bearer $TOKEN"`.

Wywołania na porty 5101/5102 służą **tylko do debugowania** serwisu. Moduł nigdy nie woła serwisu bezpośrednio: ta sama operacja
idzie przez bramę i BFF (`/api/example/v1/knowledge/categories`, 9.2), a na klastrze serwis przyjmuje ruch wyłącznie od BFF
(NetworkPolicy, rozdział 12).

Breakpoint w handlerze zapytania stawiasz w Infrastructure (`Knowledge.Infrastructure/Features/...`), nie w Application.
Pamiętaj o cache: drugi odczyt listy kategorii nie dojdzie do bazy, dopóki wpis jest świeży.

### 9.2 Breakpoint w API, wywołanie przez bramę i BFF z przeglądarki

Brama w kontenerze kieruje ruch do `example-bff`, a BFF w kontenerze woła alias `knowledge-api.knowledge.svc.cluster.local:8080`.
Gdy zatrzymasz kontener API, alias
znika. Można go przejąć małym kontenerem przekierowującym ruch do procesu w IDE (sprawdzone na Docker Desktop, gdzie
`host.docker.internal` sięga także usług nasłuchujących na `localhost` hosta):

```bash
docker compose -f deploy/local/docker-compose.yml stop knowledge-api
# uruchom Knowledge.Api z IDE (http://localhost:5101)
docker run --rm -d --name knowledge-api-forward \
  --network superapp-local_default --network-alias knowledge-api.knowledge.svc.cluster.local \
  alpine/socat tcp-listen:8080,fork,reuseaddr tcp-connect:host.docker.internal:5101
```

Po zakończeniu: `docker stop knowledge-api-forward` i `docker compose -f deploy/local/docker-compose.yml start knowledge-api`.
To narzędzie wyłącznie lokalne; niczego nie zmienia w repozytorium.

Następnie w przeglądarce:

1. `https://localhost:5001/bff/login` → strona logowania Keycloaka → `editor` / `editor` → powrót na `/` (brama nie ma
   strony głównej, więc zobaczysz `404`; ciasteczko `__Host-bff` jest już ustawione).
2. `https://localhost:5001/bff/user` → `{ sub, name, scopes, logoutUrl, analyticsId }` (`analyticsId` lokalnie `null`).
3. W konsoli narzędzi deweloperskich na karcie `https://localhost:5001` (ten sam origin, więc ciasteczko idzie samo):
   ```js
   await fetch('/api/example/v1/knowledge/categories', {
     method: 'POST',
     headers: { 'X-CSRF': '1', 'Content-Type': 'application/json' },
     body: JSON.stringify({ name: 'Higiena snu', slug: 'higiena-snu' })
   }).then(r => r.status)
   ```
   Bez `X-CSRF: 1` dostaniesz `401` z `code` `auth.csrf_header_missing`. Brama usuwa prefiks `/api/example` i dołącza token, BFF wywołuje
   `POST /v1/categories` w Knowledge z tym samym tokenem i przekazuje odpowiedź (`201` albo błąd z `code`) bez zmian.

Brama uruchomiona z IDE (zamiast kontenera) obsłuży `/bff/*`, logowanie, sesję i CSRF, ale nie rozwiąże nazw
`*.svc.cluster.local`, więc przekazanie do BFF skończy się `502`. Gdy pracujesz nad samą bramą i potrzebujesz też
przekazywania, nazwa `example-bff.example.svc.cluster.local` musi wskazywać z hosta na BFF nasłuchujący na porcie 8080
(np. wpis w pliku `hosts` na `127.0.0.1` i BFF z IDE uruchomiony z `--urls http://localhost:8080`; BFF z IDE woła serwisy na
`localhost:5101/5102`).

### 9.3 Breakpoint w konsumencie (Worker)

1. `docker compose -f deploy/local/docker-compose.yml stop knowledge-worker` (inaczej oba procesy konsumują z tej samej
   kolejki naprzemiennie i breakpoint trafia co drugi raz).
2. Uruchom `Knowledge.Worker` z IDE (profil `knowledge-worker`), breakpoint w `MaterialArchivedConsumer.Consume`.
3. Wywołaj archiwizację materiału przez API (`POST /v1/materials/{id}/archive` z tokenem redaktora). API zapisuje wiadomość do
   `knowledge.OutboxMessage`; Worker z IDE (ma `OutboxDelivery.Enabled`) wyśle ją do RabbitMQ i sam odbierze.

Retry działa także pod debuggerem: wyjątek w konsumencie oznacza kolejne wywołania po 100 ms, 500 ms, 1 s i 5 s, a potem
wiadomość w `knowledge-material-archived_error`. Długie zatrzymanie na breakpoincie nie przenosi wiadomości do kolejki błędów; po 30 minutach (domyślny `consumer_timeout`
RabbitMQ) broker zamknie jednak kanał i wiadomość wróci do kolejki.

### 9.4 MSSQL: sesje, outbox, inbox, migracje

Połączenie: SSMS / Azure Data Studio / Rider `localhost,1433`, `sa` / `Dev!Passw0rd1`, „Trust server certificate”. Z wiersza
poleceń przez `sqlcmd` w kontenerze (w Git Bash dodaj `MSYS_NO_PATHCONV=1`, inaczej ścieżka `/opt/...` zostanie przepisana):

```bash
MSYS_NO_PATHCONV=1 docker compose -f deploy/local/docker-compose.yml exec mssql \
  /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P 'Dev!Passw0rd1' -d SuperApp -W \
  -Q "SELECT MigrationId FROM knowledge.__EFMigrationsHistory"
```

Przydatne zapytania:

```sql
-- Sesje bramy (profil bff-web): kto jest zalogowany, do kiedy (ticket jest zaszyfrowany; czytelne są tylko kolumny techniczne)
SELECT Id, Subject, SessionId, ExpiresAt, UpdatedAt FROM gateway.Sessions ORDER BY UpdatedAt DESC;

-- Wylogowanie wszystkich lokalnie (następne żądanie SPA dostanie 401)
DELETE FROM gateway.Sessions;

-- Outbox: wiadomości zapisane, a jeszcze niewysłane. Wysłane wiersze MassTransit usuwa, więc pusta tabela to stan normalny.
SELECT SequenceNumber, MessageType, MessageId, SentTime, OutboxId, InboxMessageId, InboxConsumerId
FROM knowledge.OutboxMessage ORDER BY SequenceNumber;
SELECT OutboxId, Created, Delivered, LastSequenceNumber FROM knowledge.OutboxState;

-- Inbox: odebrane wiadomości (deduplikacja po MessageId + ConsumerId); ReceiveCount > 1 oznacza ponowienia
SELECT TOP 20 MessageId, ConsumerId, Received, ReceiveCount, Consumed, Delivered
FROM knowledge.InboxState ORDER BY Id DESC;

-- Konfiguracja tras bramy dla profilu (oczekiwane: po jednej trasie example-bff na profil, adres example-bff.example.svc.cluster.local)
SELECT r.Profile, r.RouteId, r.Path, r.AuthorizationPolicy, d.Address
FROM gateway.Routes r JOIN gateway.Destinations d ON d.Profile = r.Profile AND d.ClusterId = r.ClusterId
ORDER BY r.Profile, r.RouteId;

-- Ostatnia migracja bramy (od niej zależy przeładowanie tras)
SELECT TOP 1 MigrationId FROM gateway.__EFMigrationsHistory ORDER BY MigrationId DESC;
```

Jak czytać outbox:

| Kolumny | Pochodzenie wiersza |
|---|---|
| `OutboxId` wypełnione, `InboxMessageId` puste | wiadomość opublikowana przez komendę w API (bus outbox); wyśle ją delivery service Workera |
| `InboxMessageId` + `InboxConsumerId` wypełnione | wiadomość opublikowana przez konsumenta (outbox konsumenta), wysyłana po commicie jego transakcji |

Wiersze z `OutboxId`, które nie znikają przez dłuższy czas, oznaczają, że Worker nie działa albo nie może połączyć się z
RabbitMQ ([Rozwiązywanie problemów](17-rozwiazywanie-problemow.md)).

### 9.5 RabbitMQ: kolejki, błędy, przenoszenie wiadomości

Panel: `http://localhost:15672` (`guest` / `guest`).

- **Queues and Streams**: `knowledge-material-archived`, `knowledge-collection-archived` (typ `quorum`, `Consumers: 1` gdy
  Worker działa) oraz kolejki forwardera analityki `analytics-*` (`analytics-material-published`, `analytics-material-archived`,
  `analytics-collection-archived`, `analytics-sleep-entry-recorded`). `Consumers: 0` = Worker (albo forwarder) nie działa albo nie
  zarejestrował konsumenta.
- **Exchanges**: `Knowledge.Contracts:MaterialArchivedV1` (fanout) → `knowledge-material-archived` → kolejka.
- **Kolejki `_error`** (np. `knowledge-material-archived_error`) pojawiają się przy pierwszym błędzie trwałym. W szczegółach
  wiadomości (**Get messages**, tryb *Nack message requeue true*, żeby jej nie zabrać) nagłówki MassTransit opisują błąd:
  `MT-Fault-ExceptionType`, `MT-Fault-Message`, `MT-Fault-StackTrace`, `MT-Fault-RetryCount`, `MT-Reason`.

Z wiersza poleceń:

```bash
docker compose -f deploy/local/docker-compose.yml exec rabbitmq rabbitmqctl list_queues name type messages consumers
# knowledge-material-archived    quorum  0  1
# knowledge-collection-archived  quorum  0  1
# analytics-material-published   quorum  0  1
# ... (pozostałe kolejki analytics-*)
```

**Ponowne przetworzenie wiadomości z `_error`** (po naprawie błędu): panel ma przycisk *Move messages* dopiero po włączeniu
wtyczek shovel, które w obrazie `rabbitmq:4-management` są domyślnie wyłączone:

```bash
docker compose -f deploy/local/docker-compose.yml exec rabbitmq \
  rabbitmq-plugins enable rabbitmq_shovel rabbitmq_shovel_management
```

Potem na stronie kolejki `knowledge-material-archived_error` w sekcji *Move messages* wpisz kolejkę docelową
`knowledge-material-archived`. Konsument musi być idempotentny (inbox odrzuci tylko wiadomość, która została już
**poprawnie** przetworzona; wiadomość z `_error` nie została). Włączone wtyczki zostają do czasu odtworzenia kontenera.

Wyczyszczenie kolejki: `rabbitmqctl purge_queue knowledge-material-archived_error`.

### 9.6 Redis: co jest w cache

```bash
docker compose -f deploy/local/docker-compose.yml exec redis redis-cli --scan --pattern '*'
# knowledge:knowledge:categories:v1                     wpis listy kategorii (prefiks instancji + klucz)
# knowledge:__MSFT_HCT__knowledge:categories            znacznik unieważnienia tagu (HybridCache)
# knowledge:knowledge:material:v1:01a0f63c-...          widok materiału dla czytelnika
# gateway:gateway:yarp-config:v1:bff-web                ostatnia poprawna konfiguracja tras bramy
```

Pierwszy człon to `InstanceName` z `AddAppCaching` (`{prefiks}:`), drugi to klucz z `KnowledgeCache`. Unieważnienie tagiem
nie usuwa kluczy, tylko zapisuje znacznik `__MSFT_HCT__{tag}`; `HybridCache` traktuje starsze wpisy jako nieważne.
`redis-cli FLUSHALL` czyści tylko L2: każdy proces ma jeszcze kopię w pamięci (do 30 s), więc po wyczyszczeniu Redisa
zrestartuj proces, jeśli chcesz mieć pewność, że czyta z bazy.

### 9.7 Logi

```bash
docker compose -f deploy/local/docker-compose.yml logs -f knowledge-api knowledge-worker
docker compose -f deploy/local/docker-compose.yml logs --since 10m bff-web
docker compose -f deploy/local/docker-compose.yml logs example-bff | grep "\[220\]" -A1   # serwis niedostępny lub zbyt wolny (503/504 z BFF)
docker compose -f deploy/local/docker-compose.yml logs knowledge-api | grep "\[101\]"     # nieudane żądania (LoggingBehavior)
docker compose -f deploy/local/docker-compose.yml logs bff-web | grep "\[310[1-4]\]"      # problemy z odświeżaniem tokenów
docker compose -f deploy/local/docker-compose.yml logs analytics-forwarder | grep "\[9001\]" -A1   # zdarzenia analityczne (analityka wyłączona)
```

Format konsoli (`kategoria[EventId]`, potem komunikat):

```text
knowledge-api-1  | warn: SuperApp.Framework.Application.Behaviors.LoggingBehavior[101]
knowledge-api-1  |       Request CreateCategory failed with knowledge.category.slug_taken in 4.1815 ms
knowledge-api-1  | info: SuperApp.Framework.Application.Behaviors.LoggingBehavior[100]
knowledge-api-1  |       Request ListCategories handled in 3.9417 ms
```

Domyślny format konsoli nie pokazuje `TraceId`. Żeby powiązać log z `traceId` z `ProblemDetails`, włącz zakresy w procesie,
który debugujesz (zmienne środowiskowe w profilu `launchSettings` albo w `docker-compose.yml`, lokalnie):

```text
Logging__Console__FormatterName=json
Logging__Console__FormatterOptions__IncludeScopes=true
```

Każdy wpis będzie miał wtedy zakres z `TraceId` i `SpanId`. Na klastrze nie jest to potrzebne: logi idą przez OTLP z
identyfikatorami śladu ([Logowanie i obserwowalność](14-logowanie-i-obserwowalnosc.md)). Numery `EventId`:
[rejestr](../logowanie-eventid.md).

### 9.8 Breakpoint w BFF experience

BFF nie ma bazy ani kolejek, więc z IDE uruchamia się go najprościej:

1. `docker compose -f deploy/local/docker-compose.yml stop example-bff` (zwalnia port 5120).
2. Uruchom `Example.Bff` z IDE w trybie debug (profil `example-bff`, `http://localhost:5120`). Serwisy mogą działać w kontenerach
   albo z IDE: `appsettings.Development.json` wskazuje `localhost:5101` i `localhost:5102`.
3. Breakpoint w akcji kontrolera (np. `ExperienceSummaryController.Get`) albo w `PartialResponseFetcher.FetchAsync`.
4. Wywołaj BFF bezpośrednio z tokenem `dev-cli` (audience `example-bff` jest domyślne):
   ```bash
   TOKEN=$(curl -s http://localhost:8081/realms/superapp/protocol/openid-connect/token      -d grant_type=password -d client_id=dev-cli -d username=editor -d password=editor      -d "scope=openid knowledge.library.read sleepdiary.entry.read example.internal.read"      | python -c "import sys,json;print(json.load(sys.stdin)['access_token'])")
   curl -i http://localhost:5120/v1/me/summary -H "Authorization: Bearer $TOKEN"                        # 200, części odpowiedzi ze statusem
   curl -i http://localhost:5120/internal/v1/widgets/sleep-summary -H "Authorization: Bearer $TOKEN"    # 200 (scope example.internal.read)
   ```
   Bez `example.internal.read` w żądaniu tokenu API wewnętrzne odpowiada `403` (polityka BFF, `code` `auth.missing_scope`). Przez bramę
   `/api/example/internal/...` daje `404`: trasa pasuje tylko do `/api/example/v{n}/...`.

Brama w kontenerze nadal kieruje ruch do **kontenera** `example-bff` (alias), a nie do procesu z IDE, więc breakpoint w BFF
z IDE osiągniesz wywołaniem bezpośrednim na 5120 albo bramą uruchomioną z IDE z wpisem w `hosts` (9.2). Endpoint komponowany
daje każdej części odpowiedzi 2 sekundy: długie zatrzymanie na breakpoincie **w serwisie** kończy się częścią odpowiedzi `Timeout`, a nie błędem całej
odpowiedzi.

---

## 10. Zmiana kodu w trybie „wszystko w kontenerach”

Kontenery są budowane z kodu w chwili `--build`. Po zmianie:

```bash
docker compose -f deploy/local/docker-compose.yml --profile app up -d --build knowledge-api knowledge-worker
```

Zmiana migracji: dodaj `migrator` do listy (rozdział 6). Zmiana bramy: `bff-web gateway-mobile`. Zmiana BFF (także nowe akcje
po zmianie kontraktu serwisu): `example-bff`. Pierwsze zbudowanie wszystkich
obrazów trwa kilka minut; kolejne korzystają z cache warstw.

---

## 11. Reset stanu

| Co chcesz wyzerować | Polecenie | Skutek |
|---|---|---|
| Wszystko | `docker compose -f deploy/local/docker-compose.yml --profile app down -v` | usuwa kontenery i wolumen `mssql-data`; następne `up` zakłada bazę, loginy i migracje od zera |
| Kontenery bez danych bazy | `docker compose -f deploy/local/docker-compose.yml --profile app down` | baza zostaje; Keycloak, RabbitMQ i Redis startują puste (realm z pliku) |
| Sesje bramy (`bff-web`) | `DELETE FROM gateway.Sessions;` | wszyscy wylogowani |
| Cache | `redis-cli FLUSHALL` + restart procesów | |
| Kolejka | `rabbitmqctl purge_queue <nazwa>` | |
| Dane jednego serwisu | brak skryptu; najprościej `down -v` | |

Uwaga na `--profile app` przy `down`: bez niego compose nie zatrzyma kontenerów z profilu `app`.

---

## 12. Lokalne środowisko a klaster: czego compose nie odwzorowuje

Compose uruchamia całą aplikację, ale kilka rzeczy wygląda lokalnie inaczej niż na klastrze. Znaj te różnice, zanim uznasz, że
coś „działa”.

**Brama to lokalny zamiennik wspólnej bramy (ADR-0037).** Na środowiskach dev, test i prod ruch do experience przychodzi ze **wspólnej
bramy brzegowej** super appki, której nasz zespół nie wdraża. Kontenery `bff-web` i `gateway-mobile` (`SuperApp.Gateway`) istnieją po to, żeby
lokalnie, w testach E2E i przy pracy z IDE działał pełny przepływ bez infrastruktury organizacji. Ich zachowanie (sesja po stronie
serwera, `__Host-` cookie, CSRF, wylogowanie z kontrolą `sid`, back-channel logout, usuwanie `Cookie` i `X-User-*`, Bearer do backendu,
walidacja JWT z `ClockSkew` 30 s, limit żądań) jest **specyfikacją wymagań** wobec wspólnej bramy. Wynikają z tego dwie zasady:

- zmieniaj lokalną bramę tylko wtedy, gdy odwzorowuje wspólną bramę albo wymaganie wobec niej; **logika experience nie trafia do
  bramy**, tylko do BFF experience (ADR-0038);
- wspólna brama może się różnić w szczegółach (nagłówki, czasy, sposób CSRF, unieważnianie tokenów po wylogowaniu). Jeśli zachowanie
  modułu zależy od takiego szczegółu, potwierdź go z właścicielem wspólnej bramy, zamiast opierać się na lokalnym zamienniku.

**Brama zna tylko BFF.** Lokalna brama kieruje ruch modułu wyłącznie do API publicznego BFF experience: jedna trasa na experience,
`/api/example/v{version:int}/{**rest}` do `example-bff` z usunięciem prefiksu `/api/example` (`ProxyConfigurationSeed`, migracja
`RouteExperienceThroughBff`, ADR-0039). Tras do serwisów domenowych nie ma (dawne `/api/knowledge/...` daje `404`), a ścieżki
`/internal` nie pasują do trasy. To samo jest wymaganiem wobec wspólnej bramy. Nowa operacja dla modułu wymaga akcji w BFF; sam
endpoint w serwisie nie jest przez bramę osiągalny ([przepis 01](przepisy/01-endpoint-komendy.md)).

**Proxy `/ingest` i `analyticsId`** są lokalnie w bramie. Na klastrze to wymaganie wobec wspólnej bramy; jeśli ich nie zapewni,
trafią do BFF experience, który dziś takich endpointów nie ma ([21](21-analityka-i-feature-flags.md)).

**NetworkPolicy nie istnieje lokalnie (ADR-0041).** Wszystkie kontenery są w jednej sieci Dockera i każdy może połączyć się z każdym:
z bramą, z BFF, z API obu serwisów, z bazą. API wewnętrzne BFF (`/internal/v1/...`) i API serwisów wołasz lokalnie bezpośrednio
z hosta (5120, 5101, 5102), czego na klastrze nie zrobisz. Na klastrze serwisy domenowe przyjmują ruch tylko od BFF i serwisów tej samej experience, a API
wewnętrzne BFF tylko od BFF-ów innych experience ([9.5](09-bezpieczenstwo.md#networkpolicy-ruch-do-domeny-tylko-przez-bff-experience)).
Skutki:

- wywołanie, które działa lokalnie (np. jeden serwis domenowy woła serwis innej experience albo brama woła ścieżkę `/internal`), może
  zostać odrzucone przez sieć na klastrze;
- lokalnie nie przetestujesz polityk sieci; testuje się je na renderowanych manifestach w pipeline'ie wdrożeniowym, gdy dział
  infrastruktury przyjmie manifesty od zespołów;
- wartość `experience` w chartach (etykieta `app.kubernetes.io/part-of`) nie ma lokalnie odpowiednika.

---

## Typowe błędy

| Symptom | Przyczyna | Naprawa |
|---|---|---|
| `bind: address already in use` / `Failed to bind to address http://localhost:5101` | ten sam element działa w kontenerze i w IDE | `docker compose ... stop <usługa>` przed uruchomieniem z IDE |
| `bff-web` w kontenerze kończy się przy starcie błędem certyfikatu | brak `deploy/local/certs/devcert.pfx` albo inne hasło | uruchom `dev-cert.ps1` / `dev-cert.sh`; hasło = `DEV_CERT_PASSWORD` |
| Po zalogowaniu przez BFF ciasteczko nie jest ustawione | wejście przez `http://localhost:5000` | używaj `https://localhost:5001`; `__Host-` wymaga HTTPS |
| Wszystkie żądania z tokenem: `401`, `WWW-Authenticate: Bearer error="invalid_token", error_description="The audience '(null)' is invalid"` | token bez scope'u serwisu, więc bez jego audience | poproś o scope serwisu (`knowledge.catalog.read` itd.) |
| `401` przez kilka sekund po `up` | Keycloak jeszcze startuje (brak healthchecku) | odczekaj; sprawdź `docker compose logs keycloak` |
| `403 auth.missing_scope` mimo poprawnego `scope` w żądaniu tokenu | użytkownik nie może dostać scope'u (np. `reader` i `knowledge.catalog.write`), Keycloak go pominął | zaloguj się jako `editor`; sprawdź `scope` w tokenie |
| Breakpoint w konsumencie trafia co drugi raz | konsumują dwa procesy: kontener i IDE | zatrzymaj kontener Workera |
| `503 downstream.unavailable` z BFF po zatrzymaniu kontenera API | alias `*.svc.cluster.local` serwisu zniknął, BFF nie może się połączyć | przepis 9.2 albo uruchom kontener ponownie |
| `502 server.error` z bramy (`instance` ze ścieżką bramy) | kontener `example-bff` nie działa (albo brama z IDE nie rozwiązuje aliasu) | `docker compose ... up -d example-bff`; 9.2, 9.8 |
| `404` z bramy dla `/api/knowledge/...` lub `/api/sleepdiary/...` | stare ścieżki; brama zna tylko BFF | `/api/example/v1/knowledge/...`, `/api/example/v1/sleepdiary/...` |
| `403` z BFF na `/internal/v1/...` | token bez `example.internal.read` | dodaj scope do żądania tokenu `dev-cli` (9.8) |
| `500` „Invalid column name” po pobraniu zmian | nie uruchomiono Migratora | rozdział 6 |
| `db-bootstrap` kończy się błędem po restarcie Dockera („Database SuperApp is not online after 180 s”) | baza nie wystartowała w czasie (np. bardzo wolny dysk, uszkodzony wolumen) | `docker compose ... logs mssql`; ponów `up -d`; w ostateczności `down -v` (kasuje dane) |
| W logu MSSQL `Login failed for user 'sa'. Reason: Failed to open the explicitly specified database 'SuperApp'` | połączenie z bazą App, zanim serwer ją uruchomił (np. własny skrypt lub narzędzie tuż po starcie kontenera) | odczekaj na `Recovery is complete` w logu MSSQL; skrypty niech czekają jak `bootstrap.sh` |
| `analytics-forwarder` lub inny proces kończy się `OptionsValidationException: Analytics: ...` | w `.env` ustawiony `ANALYTICS_PROJECT_TOKEN` bez `ANALYTICS_ID_KEY` albo klucz krótszy niż 32 znaki | ustaw oba albo żadnego ([21.7](21-analityka-i-feature-flags.md#217-konfiguracja-i-sekrety)) |
| `docker compose exec ... /opt/mssql-tools18/...` w Git Bash: „no such file” | Git Bash przepisuje ścieżki | `MSYS_NO_PATHCONV=1` przed poleceniem |

## Do zapamiętania

- Dwa tryby: `up -d` (infrastruktura) i `--profile app up -d --build` (wszystko). Hybryda = zatrzymaj kontener, uruchom z IDE.
- Aliasy `*.svc.cluster.local` istnieją, bo brama przyjmuje tylko adresy w klastrze; działają tylko wewnątrz sieci Dockera.
- `KC_HOSTNAME` daje jeden issuer; token z `localhost:8081` jest ważny dla procesów w kontenerach i w IDE.
- Serwisy używają własnych loginów jak na klastrze; `sa` tylko Migrator i brama z IDE.
- Migracje uruchamiasz sam (hybryda) albo robi to kontener `migrator` (profil `app`); nic lokalnie nie pilnuje, że są aktualne.
- Do debugowania: token `dev-cli` + `curl` na port API (5101/5102) albo BFF (5120); Worker debuguj po zatrzymaniu jego kontenera.
- Moduł woła tylko `/api/example/v{n}/...` przez bramę; brama zna wyłącznie BFF, BFF woła serwisy z tokenem użytkownika.
- Pusty `OutboxMessage` to norma; niemalejący oznacza, że Worker nie wysyła.
- Lokalna brama to zamiennik wspólnej bramy i specyfikacja wymagań, bez logiki experience; jedyna trasa prowadzi do BFF.
- Lokalnie każdy kontener widzi każdy: brak NetworkPolicy, więc „działa lokalnie” nie znaczy „przejdzie przez sieć na klastrze”.

## Powiązane

- Rozdziały: [Start](01-start.md), [Architektura w praktyce](02-architektura-w-praktyce.md), [Bezpieczeństwo](09-bezpieczenstwo.md),
  [Zdarzenia i integracja](10-zdarzenia-i-integracja.md), [Cache](11-cache.md), [Testy](12-testy.md),
  [Logowanie i obserwowalność](14-logowanie-i-obserwowalnosc.md), [Rozwiązywanie problemów](17-rozwiazywanie-problemow.md), [FAQ](19-faq.md),
  [Analityka i feature flags](21-analityka-i-feature-flags.md).
- ADR: [0004](../adr/0004-migracje-przez-migrator-i-job-k8s.md), [0021](../adr/0021-schemat-bazy-per-mikroserwis.md),
  [0022](../adr/0022-konfiguracja-yarp-w-bazie.md), [0031](../adr/0031-lokalny-ciam-keycloak.md),
  [0034](../adr/0034-lokalne-srodowisko-docker-compose.md), [0037](../adr/0037-superapp-gateway-jako-lokalny-zamiennik-wspolnej-bramy.md),
  [0038](../adr/0038-experience-modul-bff-i-serwisy-domenowe.md), [0039](../adr/0039-api-publiczne-i-wewnetrzne-bff.md),
  [0040](../adr/0040-dostep-do-api-wewnetrznego-i-serwisow-domenowych.md), [0041](../adr/0041-networkpolicy-izolacja-experience.md).
