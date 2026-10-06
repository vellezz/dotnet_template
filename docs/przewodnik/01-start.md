# 1. Start: uruchomienie, mapa repozytorium, pierwsze żądanie

Wymagania środowiskowe, procedura budowania i testowania rozwiązania, konfiguracja lokalnego środowiska deweloperskiego oraz struktura repozytorium.

> **W skrócie**
> - `dotnet build SuperApp.slnx` musi dać 0 błędów i 0 ostrzeżeń: ostrzeżenia są błędami, a reguły architektury są regułami kompilatora.
> - `dotnet test --solution SuperApp.slnx` wymaga Dockera (testy integracyjne uruchamiają MSSQL w kontenerze).
> - Lokalne środowisko: `docker compose -f deploy/local/docker-compose.yml --profile app up -d --build`, potem https://localhost:5001.
> - Serwis, nad którym pracujesz, uruchamiasz z IDE po zatrzymaniu jego kontenera; porty są te same.

## 1.1 Wymagania

| Narzędzie | Wersja | Uwagi |
|---|---|---|
| .NET SDK | 10.0.301 lub nowszy w linii 10.0.3xx | wersja z `global.json` (`rollForward: latestFeature`) |
| Docker Desktop | aktualny | testy integracyjne (Testcontainers) i lokalne środowisko; ok. 6 GB RAM dla Dockera |
| IDE | Rider lub Visual Studio 2026 | oba uruchamiają analizatory projektu i pokazują błędy APP00x w edytorze |
| Python 3 (opcjonalnie) | | tylko do własnych skryptów pomocniczych |

Po sklonowaniu repozytorium:

```bash
./tools/bootstrap.ps1   # (Linux/macOS/Git Bash: ./tools/bootstrap.sh) buduje narzędzie dotnet superapp i instaluje narzędzia
                        # z .config/dotnet-tools.json (dotnet-ef 10.0.12, refitter 2.3.0, superapp)
dotnet superapp doctor  # spójność repozytorium: powinno zakończyć się "0 error(s)" (rozdział 22)
dotnet --version        # 10.0.3xx
docker version          # serwer Dockera działa
```

### Ustawienia IDE

- **Rider:** Settings → Editor → Inspection Settings → włączone „Roslyn analyzers” i „Read settings from editorconfig”.
  Analizatory projektu (`SuperApp.Analyzers`) są referencjonowane przez `Directory.Build.props`, więc działają bez instalacji.
- **Visual Studio:** Tools → Options → Text Editor → C# → Advanced → „Run code analysis in background” na „Entire solution”.
- Formatowanie i styl definiuje `.editorconfig` w katalogu głównym (LF jako koniec linii, przestrzeń nazw zgodna z katalogiem).
  Nie zmieniaj ustawień IDE tak, żeby nadpisywały `.editorconfig`.
- GitHub Copilot czyta `.github/copilot-instructions.md` i reguły per warstwa automatycznie.

## 1.2 Build i testy

```bash
dotnet build SuperApp.slnx
```

Czego się spodziewać: na końcu `Ostrzeżenia: 0`, `Liczba błędów: 0` (lub angielskie odpowiedniki). Build robi więcej
niż kompilację:

- uruchamia analizatory .NET i `SuperApp.Analyzers` (APP001–APP006), sprawdza dokumentację XML (CS1591) i zgodność przestrzeni nazw (IDE0130),
- audytuje pakiety NuGet pod kątem znanych podatności (NU1901–NU1904),
- **generuje kontrakty OpenAPI** serwisów do `src/Services/*/*.Api/openapi/*.json` i dwa kontrakty BFF experience do
  `src/Bff/Example.Bff/openapi/` (`Example.Bff_public.json`, `Example.Bff_internal.json`); po zmianie API plik się zmienia i trafia
  do review.

```bash
dotnet test --solution SuperApp.slnx
```

Ok. 240 testów w kilka minut przy pierwszym uruchomieniu (pobranie obrazu MSSQL), potem ok. 1 minuty. Jeśli testy
integracyjne zgłaszają błąd połączenia z Dockerem, uruchom Docker Desktop. Pojedynczy projekt:

```bash
dotnet test --project src/Services/SleepDiary/tests/SleepDiary.Domain.Tests
```

Szczegóły: [12 Testy](12-testy.md).

## 1.3 Lokalne środowisko

Pełny opis i debugowanie: [13 Lokalne środowisko i debugowanie](13-lokalne-srodowisko-i-debugowanie.md). Najkrótsza droga:

```bash
# 1. Certyfikat HTTPS dla bram w kontenerach (ciasteczko __Host-bff wymaga HTTPS) – jednorazowo
./deploy/local/dev-cert.sh                  # Windows: pwsh deploy/local/dev-cert.ps1

# 2. Cała aplikacja w kontenerach (pierwszy raz kilka minut: budowanie obrazów)
docker compose -f deploy/local/docker-compose.yml --profile app up -d --build

# 3. Stan kontenerów
docker compose -f deploy/local/docker-compose.yml --profile app ps
```

Oczekiwany stan: `db-bootstrap`, `migrator`, `db-gateway-permissions` zakończone z kodem 0 (zadania jednorazowe),
pozostałe `Up`, a `mssql`, `redis`, `rabbitmq` jako `healthy`.

| Usługa | Adres | Dane logowania |
|---|---|---|
| Aplikacja web przez bramę (profil `bff-web`) | https://localhost:5001, API experience pod `/api/example/v1/...` | użytkownicy realmu |
| Gateway mobile | https://localhost:5002, API experience pod `/api/example/v1/...` | token Bearer |
| BFF experience `example-bff` (bezpośrednio) | http://localhost:5120, kontrakty `/openapi/public.json` i `/openapi/internal.json` | token Bearer (audience `example-bff`) |
| Knowledge API (bezpośrednio, tylko debug) | http://localhost:5101, kontrakt `/openapi/v1.json` | token Bearer |
| SleepDiary API (bezpośrednio, tylko debug) | http://localhost:5102 | token Bearer |
| Keycloak (lokalny CIAM) | http://localhost:8081 | admin / admin |
| RabbitMQ (panel) | http://localhost:15672 | guest / guest |
| MSSQL | `localhost,1433`, baza `SuperApp` | `sa` / `Dev!Passw0rd1`; serwisy: `knowledge_app`, `sleepdiary_app`, `gateway_app` |
| Redis | `localhost:6380` | |

Użytkownicy realmu `app`: `reader` / `reader` (czytelnik: odczyt katalogu, własna biblioteka, własny dziennik snu) i
`editor` / `editor` (dodatkowo redaktor katalogu Knowledge, scope `knowledge.catalog.write`).

### Tryb hybrydowy: serwis z IDE

Pracujesz nad Knowledge i chcesz debugować? Zatrzymaj jego kontenery i uruchom projekty z IDE. Profile w
`Properties/launchSettings.json` używają tych samych portów co kontenery, a `appsettings.Development.json` wskazuje lokalną
infrastrukturę i login serwisu:

```bash
docker compose -f deploy/local/docker-compose.yml stop knowledge-api knowledge-worker
dotnet run --project src/Services/Knowledge/Knowledge.Api --launch-profile knowledge-api        # http://localhost:5101
dotnet run --project src/Services/Knowledge/Knowledge.Worker --launch-profile knowledge-worker  # http://localhost:5111 (health)
```

| Projekt | Profil | Port |
|---|---|---|
| Knowledge.Api / Knowledge.Worker | `knowledge-api` / `knowledge-worker` | 5101 / 5111 |
| SleepDiary.Api / SleepDiary.Worker | `sleepdiary-api` / `sleepdiary-worker` | 5102 / 5112 |
| Example.Bff | `example-bff` | 5120 |
| SuperApp.Gateway | `bff-web` / `gateway-mobile` | 5001 (+5000) / 5002 |
| SuperApp.Migrator | `local` | (zadanie jednorazowe) |

Uwaga: brama w kontenerze kieruje ruch `/api/example/v1/...` na adres z klastra (`example-bff.example.svc.cluster.local`), czyli
do kontenera `example-bff`, a BFF w kontenerze woła serwisy pod ich aliasami klastrowymi (`knowledge-api.knowledge.svc.cluster.local`),
czyli też kontenery, a nie procesy z IDE. Serwis z IDE wołasz bezpośrednio (port 5101, tylko debug) albo uruchamiasz z IDE także
BFF: jego `appsettings.Development.json` wskazuje serwisy na `http://localhost:5101` i `http://localhost:5102`, a BFF z IDE wołasz
bezpośrednio na porcie 5120 (przed uruchomieniem zatrzymaj kontener `example-bff`).

Tylko infrastruktura (bez profilu `app`) i migracje z IDE:

```bash
docker compose -f deploy/local/docker-compose.yml up -d
dotnet run --project src/Migrator/SuperApp.Migrator --launch-profile local
```

## 1.4 Pierwsze żądania

### Przez przeglądarkę i bramę

1. Otwórz https://localhost:5001/bff/login i zaloguj się jako `editor` / `editor`.
2. Otwórz https://localhost:5001/bff/user: zobaczysz dane użytkownika, scope i `logoutUrl`. Tokenów w przeglądarce nie ma:
   jest tylko ciasteczko `__Host-bff` z identyfikatorem sesji (sesja leży w MSSQL, w `gateway.Sessions`).
3. Żądania do `/api/*` z przeglądarki wymagają nagłówka `X-CSRF: 1` (w aplikacji Angular dodaje go interceptor). Brama kieruje
   `/api/example/v1/...` do BFF experience, np. https://localhost:5001/api/example/v1/me/summary zwraca ekran startowy modułu
   złożony z Knowledge i SleepDiary (wywołanie z konsoli przeglądarki: `fetch('/api/example/v1/me/summary', {headers: {'X-CSRF': '1'}})`).

### Z tokenem: przez bramę, do BFF i do serwisu

Moduł woła wyłącznie API publiczne BFF przez bramę. Bezpośrednie wywołania BFF (5120) i serwisów (5101/5102) są tylko do
debugowania: w klastrze serwisy domenowe nie są osiągalne spoza experience (NetworkPolicy, ADR-0041).

```bash
TOKEN=$(curl -s http://localhost:8081/realms/superapp/protocol/openid-connect/token \
  -d grant_type=password -d client_id=dev-cli -d username=editor -d password=editor \
  -d "scope=openid knowledge.catalog.read knowledge.catalog.write" | jq -r .access_token)

# utworzenie kategorii
curl -s -X POST http://localhost:5101/v1/categories \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"name":"Zdrowy sen","slug":"zdrowy-sen"}'
# 201 {"id":"01a0f4..."}

# ta sama kategoria drugi raz, tym razem przez bramę mobile i BFF (ścieżka modułu)
curl -sk -i -X POST https://localhost:5002/api/example/v1/knowledge/categories \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"name":"Zdrowy sen","slug":"zdrowy-sen"}'
# HTTP/1.1 409 Conflict
# Content-Type: application/problem+json
# {"title":"Kategoria o tym slugu już istnieje.","status":409,"instance":"/v1/categories",
#  "code":"knowledge.category.slug_taken","traceId":"4bf92f35..."}

# lista kategorii bezpośrednio z BFF (debug BFF)
curl -s http://localhost:5120/v1/knowledge/categories -H "Authorization: Bearer $TOKEN"
```

Odpowiedź błędu pochodzi z serwisu Knowledge: BFF przekazuje ją bez zmian (status, `code`, `traceId`), a brama usuwa tylko prefiks
`/api/example`. Dlatego `instance` wskazuje ścieżkę serwisu (`/v1/categories`), a nie ścieżkę modułu.

Klient `dev-cli` (grant hasłem) istnieje wyłącznie lokalnie. Pole `code` jest stałym kodem błędu, po którym klienci
rozpoznają sytuację; `traceId` pozwala znaleźć żądanie w logach ([14 Logowanie i obserwowalność](14-logowanie-i-obserwowalnosc.md)).

### Co się stało w bazie

```sql
-- sqlcmd -S localhost,1433 -U sa -P "Dev!Passw0rd1" -d SuperApp -C
SELECT Id, Name, Slug FROM knowledge.Categories;
SELECT TOP 5 MigrationId FROM knowledge.__EFMigrationsHistory ORDER BY MigrationId DESC;
```

## 1.5 Mapa repozytorium

```
SuperApp.slnx                     solucja (CLI, Visual Studio, VS Code)
SuperApp.sln                      ta sama solucja w starym formacie (Rider); obie mają te same projekty i foldery
global.json                  wersja SDK, runner testów (Microsoft.Testing.Platform)
Directory.Build.props        wspólne ustawienia: nullable, warnings as errors, dokumentacja XML, analizatory, NuGet audit
Directory.Build.targets      kontrakty OpenAPI: opisy z projektów Application, etykieta wersji 3.0.3
Directory.Packages.props     wersje pakietów (Central Package Management)
.editorconfig                styl, reguły analizatorów, wymóg <remarks> w Frameworku
docs/
  architektura.md            dokument architektury (arc42)
  adr/                       decyzje architektoniczne z uzasadnieniem
  przewodnik/                ten podręcznik
  logowanie-eventid.md       rejestr zakresów EventId
src/
  Framework/                 SuperApp.Framework.Domain / .Application / .Infrastructure: kod techniczny bez pojęć biznesowych
  Analytics/SuperApp.AnalyticsForwarder  forwarder zdarzeń integracyjnych do PostHog (ADR-0036) + testy
  Bff/Example.Bff            BFF experience Example: API publiczne /v1 i wewnętrzne /internal/v1, klienci Refitter serwisów + testy
  Gateway/SuperApp.Gateway        lokalny zamiennik wspólnej bramy, YARP (bff-web, gateway-mobile) + testy
  Migrator/SuperApp.Migrator      migracje wszystkich schematów (dev/test)
  Services/Knowledge/        serwis: Domain, Application, Infrastructure, Api, Worker, Contracts, tests/
  Services/SleepDiary/       serwis o tej samej budowie, prostszy: najlepszy do nauki
  Tools/SuperApp.Analyzers        analizatory Roslyn (APP001–APP006) + testy
  Tools/SuperApp.Cli              narzędzie dotnet superapp (rozdział 22) + testy; Templates/: szablony superapp-service i superapp-bff,
                                  z których korzystają `dotnet superapp add service` i `add bff`
tests/SuperApp.ArchitectureTests  reguły zależności między warstwami i serwisami
tools/                            bootstrap.ps1/.sh (instalacja dotnet superapp), packages/ (lokalne źródło pakietu narzędzia)
deploy/
  helm/                      charty: superapp-service, superapp-bff, superapp-gateway, superapp-migrator, superapp-analytics-forwarder
  sql/                       bootstrap schematów i uprawnień (DBA)
  local/                     docker compose, realm Keycloak, bootstrap lokalnej bazy, certyfikat deweloperski
.github/                     instrukcje i polecenia dla GitHub Copilot
```

## 1.6 Jak czytać serwis: zacznij od SleepDiary

Każdy serwis ma tę samą budowę. SleepDiary ma jeden agregat i pięć przypadków użycia, więc w godzinę przeczytasz go w całości.
Czytaj od środka na zewnątrz:

| Kolejność | Plik | Na co zwrócić uwagę |
|---|---|---|
| 1 | `SleepDiary.Domain/Entries/SleepEntry.cs` | fabryka `Record`, metoda `Update`, niezmienniki, zdarzenie `SleepEntryRecorded` |
| 2 | `SleepDiary.Domain/Entries/SleepEntryErrors.cs`, `SleepQuality.cs`, `SleepEntryId.cs` | błędy z kodami, value object, silne ID |
| 3 | `SleepDiary.Application/Features/Entries/RecordSleepEntry/` | komenda, walidator, handler: wzorzec każdego przypadku użycia |
| 4 | `SleepDiary.Application/IntegrationEvents/SleepEntryRecordedTranslator.cs` | zdarzenie domenowe → integracyjne |
| 5 | `SleepDiary.Infrastructure/Persistence/Write/` | kontekst zapisu, konfiguracja EF, repozytorium, mapowanie konfliktu |
| 6 | `SleepDiary.Infrastructure/Features/Entries/` | handlery zapytań na kontekście odczytu |
| 7 | `SleepDiary.Api/Controllers/SleepEntriesController.cs` | cienki kontroler, metadane odpowiedzi, dokumentacja dla klientów API |
| 8 | `SleepDiary.Api/openapi/SleepDiary.Api.json` | wynikowy kontrakt |
| 9 | `tests/` | trzy poziomy testów |

Potem Knowledge: więcej agregatów, treść blokowa, cache, konsument zdarzeń w Workerze.

Każdy publiczny typ ma dokumentację XML pisaną dla osoby nowej w projekcie. Zanim użyjesz `ICommand`, `Result`,
`IUnitOfWork`, `FailSafeCache` czy `AggregateRoot`, najedź na nie kursorem w IDE: dokumentacja wyjaśnia rolę, regułę i przykład.

## 1.7 Pierwsze zadanie (ćwiczenie)

Dodaj do SleepDiary zapytanie „ostatni wpis użytkownika” (`GET /v1/entries/latest`) według
[przepisu 02](przepisy/02-endpoint-zapytania.md), z testem integracyjnym. Nie commituj go: to ćwiczenie, które przeprowadzi
Cię przez warstwy, kontrakt OpenAPI i testy. Pełną funkcję od zera buduje [samouczek](16-samouczek-pelna-funkcja.md).

## Typowe błędy

| Objaw | Przyczyna | Naprawa |
|---|---|---|
| `dotnet build`: błąd NETSDK1045 / brak SDK | inna wersja SDK | zainstaluj SDK 10.0.3xx (`global.json`) |
| Testy integracyjne: „Docker is not running” | Docker Desktop wyłączony | uruchom Dockera, powtórz testy |
| `docker compose up`: „port is already allocated” | port zajęty przez inny projekt | zatrzymaj tamten kontener albo zmień port hosta w compose (Redis jest już na 6380 z tego powodu) |
| BFF: ciasteczko nie zostaje ustawione | wejście przez `http://` | używaj https://localhost:5001 (prefiks `__Host-` wymaga HTTPS); wygeneruj certyfikat `dev-cert` |
| API z IDE nie startuje: „Brak connection stringu RabbitMq” | infrastruktura nie działa | `docker compose -f deploy/local/docker-compose.yml up -d` |
| `/health/startup` zwraca 503 | oczekujące migracje | uruchom Migrator (`--launch-profile local` albo kontener `migrator`) |

Więcej: [17 Rozwiązywanie problemów](17-rozwiazywanie-problemow.md).

## Do zapamiętania

- Build to też kontrola architektury, dokumentacji i kontraktu; czysty build jest warunkiem każdej zmiany.
- Lokalnie masz cały system: bramy, BFF experience, serwisy, Workery, CIAM, bazę, Redis i RabbitMQ.
- Moduł woła `/api/example/v1/...` (brama → BFF → serwis); porty 5120, 5101 i 5102 służą tylko do debugowania.
- Pracując nad serwisem, zatrzymaj jego kontener i uruchom go z IDE na tym samym porcie.
- Zacznij czytanie kodu od SleepDiary i dokumentacji XML typów frameworku.
