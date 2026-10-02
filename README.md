# App

Aplikacja webowo-mobilna: mikroserwisy .NET 10 (Clean DDD + CQRS), bramy YARP, Kubernetes on-premise.

- Architektura: [`docs/architektura.md`](docs/architektura.md)
- Dokumentacja rozwiązania (projekty, interfejsy, konfiguracja, kody błędów): [`docs/dokumentacja-rozwiazania.md`](docs/dokumentacja-rozwiazania.md)
- Decyzje: [`docs/adr/`](docs/adr/README.md)
- **Nowa osoba w zespole: zacznij od [przewodnika programisty](docs/przewodnik/README.md)** (uruchomienie, zasady, przepisy na typowe zadania)
- Asystent AI (GitHub Copilot): reguły repozytorium w `.github/copilot-instructions.md`, `.github/instructions/`, polecenia w `.github/prompts/`

## Wymagania

- .NET SDK 10.0.301 (`global.json`), Docker (testy integracyjne), Helm 3 (weryfikacja chartów).
- Narzędzia lokalne: `./tools/bootstrap.ps1` (albo `./tools/bootstrap.sh`): buduje narzędzie `dotnet superapp` i instaluje
  narzędzia z manifestu (dotnet-ef, refitter, superapp). Potem `dotnet superapp doctor` sprawdza spójność repozytorium (ADR-0046).

## Lokalne środowisko (docker compose, ADR-0034)

Jeden plik `deploy/local/docker-compose.yml`: infrastruktura zawsze, cała aplikacja w profilu `app`.

```bash
cp deploy/local/.env.example deploy/local/.env      # lokalne hasła (wartości domyślne działają)
./deploy/local/dev-cert.sh                          # albo deploy/local/dev-cert.ps1: certyfikat HTTPS dla bram w kontenerach

# Tryb hybrydowy: infrastruktura w kontenerach, serwisy i bramy z IDE (appsettings.Development.json, launchSettings)
docker compose -f deploy/local/docker-compose.yml up -d
dotnet run --project src/Migrator/SuperApp.Migrator --launch-profile local

# Wszystko w kontenerach
docker compose -f deploy/local/docker-compose.yml --profile app up -d --build
```

| Usługa | Adres |
|---|---|
| BFF web | https://localhost:5001 (`/bff/login`, `/bff/user`, `/api/...`) |
| Gateway mobile | https://localhost:5002 |
| Knowledge API / SleepDiary API (bezpośrednio, do debugowania) | http://localhost:5101 / http://localhost:5102 |
| Keycloak (lokalny CIAM, ADR-0031) | http://localhost:8081 (admin / admin), realm `app` |
| MSSQL | localhost,1433 (`sa` / `DB_SA_PASSWORD`), serwisy łączą się loginami `{serwis}_app` |
| RabbitMQ | localhost:5672, panel http://localhost:15672 (guest / guest) |
| Redis | localhost:6380 |

- Użytkownicy: `reader` / `reader`, `editor` / `editor` (redaktor katalogu Knowledge).
- Brama i serwis z IDE zajmują te same porty co kontenery; przy pracy nad jednym elementem zatrzymaj jego kontener
  (`docker compose -f deploy/local/docker-compose.yml stop bff-web`).
- Token do testów API (klient `dev-cli`, wyłącznie lokalnie):

```bash
curl -s http://localhost:8081/realms/superapp/protocol/openid-connect/token   -d grant_type=password -d client_id=dev-cli -d username=editor -d password=editor   -d "scope=openid knowledge.catalog.read knowledge.catalog.write"
```

- Nowy serwis: dopisz jego kontenery (API z aliasem `{serwis}-api.{serwis}.svc.cluster.local`, Worker) do pliku compose
  i login `{serwis}_app` do `deploy/local/db/bootstrap.sh`.
- Reset danych: `docker compose -f deploy/local/docker-compose.yml down -v`.

## Build i testy

```bash
dotnet build SuperApp.slnx
dotnet test --solution SuperApp.slnx
```

Testy integracyjne uruchamiają MSSQL w kontenerze (Testcontainers). Testy nie uruchamiają busa MassTransit
(wymaga RabbitMQ); publikacja zdarzeń jest weryfikowana na poziomie outboxa przez fake.

## Nowy serwis

```bash
dotnet superapp add service Billing --experience example --scope invoice.read   # szablon + wszystkie rejestracje (ADR-0046)
```

Ręcznie, bez narzędzia:

```bash
dotnet new install ./src/Tools/SuperApp.Cli/Templates/superapp-service
dotnet new superapp-service -n Billing -o src/Services/Billing
```

Następnie (ADR-0030):

1. dodaj projekty do `SuperApp.slnx` i `SuperApp.sln` (`dotnet sln SuperApp.slnx add ...`, to samo dla `SuperApp.sln`),
2. utwórz pierwszą migrację:
   `dotnet ef migrations add Initial -p src/Services/Billing/Billing.Infrastructure -s src/Services/Billing/Billing.Infrastructure --context BillingWriteDbContext -o Migrations`,
3. zarejestruj kontekst w `src/Migrator/SuperApp.Migrator/Program.cs`,
4. dodaj schemat do `deploy/sql/01-bootstrap.sql`,
5. dopisz prefiks scope serwisu do polityki jego experience w `src/Gateway/SuperApp.Gateway/Security/GatewayPolicies.cs` (brama nie ma
   tras do serwisów domenowych; serwis jest dostępny przez BFF experience, ADR-0039),
6. dodaj referencje w `tests/SuperApp.ArchitectureTests` oraz plik `deploy/helm/superapp-service/values-billing.yaml`,
7. uruchom `dotnet superapp doctor`: wskaże każde miejsce, którego brakuje (pełna lista kroków: przepis 07 w `docs/przewodnik/przepisy`).

## Migracje

- Nowa migracja: `dotnet superapp migration add <Serwis|Gateway> <Nazwa>`; skrypt dla DBA: `dotnet superapp migration script`.

- dev/test: Job `superapp-migrator` (`deploy/helm/superapp-migrator`) przed rolloutem.
- prod: DBA uruchamia skrypty idempotentne:
  `dotnet ef migrations script --idempotent -p <Infrastructure> -s <Infrastructure> --context <Context> -o <plik>.sql`,
  potem `deploy/sql/02-gateway-config-permissions.sql`.

## Konfiguracja (zmienne środowiskowe, sekrety z Vault)

| Klucz | Użycie |
|---|---|
| `ConnectionStrings__Write`, `ConnectionStrings__Read` | Serwis: kontekst zapisu i odczytu |
| `ConnectionStrings__RabbitMq` | Serwis: `amqps://użytkownik:hasło@host/vhost` |
| `ConnectionStrings__Redis` | Cache L2 (bez niego tylko L1) |
| `ConnectionStrings__Gateway` | Brama: schemat `gateway` |
| `ConnectionStrings__Migrator` | Migrator: użytkownik `superapp_migrator` |
| `Authentication__Authority`, `Authentication__Audience` | Walidacja JWT (CIAM) |
| `Gateway__Profile` | `bff-web` lub `gateway-mobile` |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Eksport telemetrii |

## Obrazy i wdrożenie

Dockerfile w projektach hostów (`*.Api`, `*.Worker`, `SuperApp.Gateway`, `SuperApp.Migrator`), budowane z katalogu
głównego repozytorium. Chart Helm: `deploy/helm` (wymaga Kubernetes ≥ 1.30 dla `preStop.sleep`).
