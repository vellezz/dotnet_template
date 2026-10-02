# 22. Narzędzie `dotnet superapp`

**Czego się nauczysz:** jak zainstalować i aktualizować narzędzie deweloperskie repozytorium; jak sprawdzić spójność repozytorium
(`doctor`) i naprawić to, co narzędzie umie naprawić samo; jak szybko znaleźć wolny port, zakres EventId albo listę scope
(`list`, `info`); jak jednym poleceniem dodać lub usunąć serwis, BFF i klienta serwisu (`add`, `remove`); jak dodać regułę
`doctor`; jak korzysta z narzędzia Copilot.

**Wymagania wstępne:** [01 Start](01-start.md). Decyzja: [ADR-0046](../adr/0046-narzedzie-deweloperskie-superapp.md).

> **W skrócie**
>
> - Instalacja raz po sklonowaniu i po każdej zmianie narzędzia: `tools/bootstrap.ps1` (Windows) albo `tools/bootstrap.sh`.
> - `dotnet superapp doctor` przed każdym pull requestem: sprawdza solucje, rejestrację serwisów i BFF-ów, porty, EventId,
>   wersję narzędzia, linki i ścieżki w dokumentacji. Każde znalezisko ma plik i gotową naprawę.
> - `dotnet superapp doctor --fix` naprawia to, co da się naprawić mechanicznie (dziś: generuje `SuperApp.sln` ze `SuperApp.slnx`).
> - `dotnet superapp list ports|eventids|scopes|services|bffs|experiences` i `dotnet superapp info <nazwa>`: przegląd repozytorium.
> - `dotnet superapp add service|bff|client` tworzy element i rejestruje go wszędzie, gdzie trzeba; `remove … --yes` cofa to
>   i usuwa kod (nigdy nie zmienia bazy ani CIAM). Oba są idempotentne, a `--dry-run` / brak `--yes` pokazuje tylko plan.
> - Na co dzień: `add usecase` (szkielet komendy lub zapytania z akcją), `migration add|list|script`, `contracts [--check]`,
>   `contracts snapshot` + `contracts diff` (zmiany łamiące dla konsumentów, bez git albo względem rewizji git).
> - Elementy: `add|remove aggregate|event|consumer|scope|flag|product-event|usecase`; środowisko: `env up|down|status|token`, `e2e`.
> - Każde polecenie przyjmuje `--json` i zwraca stałe kody wyjścia (0, 1, 2, 3), więc nadaje się do skryptów, CI i dla Copilota.
> - Repozytorium samo przechodzi `doctor` bez błędów: pilnuje tego test `RepositoryDoctorTests` w `dotnet test`.

## 22.1 Instalacja i aktualizacja

Narzędzie to projekt `src/Tools/SuperApp.Cli`, dystrybuowany jako **lokalne narzędzie .NET** (jak `dotnet-ef`):

```powershell
./tools/bootstrap.ps1      # Windows (PowerShell)
./tools/bootstrap.sh       # Linux, macOS, Git Bash
dotnet superapp --help
```

Skrypt:

1. pakuje projekt do `tools/packages` (lokalne źródło pakietów z `nuget.config`);
2. usuwa z pamięci podręcznej NuGet i z pamięci narzędzi lokalnych .NET poprzednią kopię tej samej wersji, żeby przebudowane
   narzędzie zostało użyte nawet bez zmiany wersji;
3. uruchamia `dotnet tool restore` (manifest `.config/dotnet-tools.json`, wersja przypięta).

`nuget.config` bierze pakiet `SuperApp.Cli` **wyłącznie** z `tools/packages` (package source mapping). Pakiet o tej samej nazwie
w nuget.org nigdy go nie zastąpi, a pozostałe pakiety nie są szukane w lokalnym katalogu.

Podczas pracy nad samym narzędziem nie trzeba pakować po każdej zmianie:

```powershell
dotnet run --project src/Tools/SuperApp.Cli -- doctor
```

Przed oddaniem zmiany narzędzia podnieś `<Version>` w `SuperApp.Cli.csproj` i tę samą wersję `superapp.cli` w manifeście.
`doctor` (reguła `tool-version`) zgłosi rozbieżność.

## 22.2 `doctor`: spójność repozytorium

```text
$ dotnet superapp doctor
ok    solution-files  Every project is in SuperApp.slnx; SuperApp.sln has the same projects in the same folders.
ok    service-registration  Every service is in the Migrator, the SQL bootstrap, docker compose, Helm, ...
ok    bff-registration  Every BFF is in docker compose, Helm and the gateway; every service client of a BFF ...
ok    ports  Launch profiles and docker compose do not give one local port to two components.
ok    event-ids  Log event IDs are unique, lie in their component's range and are listed in docs/logowanie-eventid.md.
ok    tool-version  SuperApp.Cli has the same version in its project and in .config/dotnet-tools.json.
ok    doc-links  Relative links and #anchors in docs/, .github/ and the Markdown files of the root resolve.
ok    doc-paths  Repository paths quoted in the documentation exist (placeholders and configured exceptions are skipped).

0 error(s), 0 warning(s).
```

| Reguła | Co sprawdza | Typowa naprawa |
|---|---|---|
| `solution-files` | każdy `.csproj` z `src` i `tests` jest w `SuperApp.slnx`; `SuperApp.sln` ma te same projekty w tych samych folderach | `dotnet sln SuperApp.slnx add …`, potem `doctor --fix` |
| `service-registration` | serwis jest w Migratorze (referencja i `{Serwis}WriteDbContext`), w `deploy/sql/01-bootstrap.sql`, w compose (`{serwis}-api`, `{serwis}-worker`), ma `values-{serwis}.yaml` z `experience`, referencje w testach architektury, prefiks scope w polityce bramy i zakres w rejestrze EventId | dopisać brakujący wpis wskazany w znalezisku (przepis 07) |
| `bff-registration` | BFF jest w compose (`{experience}-bff`), ma `values-{experience}.yaml`, politykę i trasę w bramie; każdy klient `Clients/{Serwis}` jest zarejestrowany (`AddDownstreamApi`) i ma adres w `appsettings*.json`, Helm i compose | jak wyżej (przepisy 08 i 11) |
| `ports` | ten sam port nie należy do dwóch komponentów (profil uruchomieniowy i kontener jednego komponentu mogą go dzielić) | kolejny wolny port z `list ports` |
| `event-ids` | `[LoggerMessage]` ma ID z zakresu swojego komponentu, bez duplikatów; kolumna „Użyte” rejestru zgadza się z kodem (rozbieżność to ostrzeżenie) | ID z `list eventids` (kolumna „Next free”), aktualizacja `docs/logowanie-eventid.md` |
| `tool-version` | wersja narzędzia w projekcie = wersja w manifeście | podnieść obie, `tools/bootstrap.ps1` |
| `doc-links` | względne linki i kotwice `#…` w `docs/`, `.github/`, pliki Markdown w katalogu głównym | poprawić ścieżkę albo kotwicę po zmianie nagłówka |
| `doc-paths` | ścieżki repozytorium w kodzie Markdown (`` `src/…` ``) istnieją; pomija symbole zastępcze (`{Serwis}`, `*`, `…`) i wyjątki z `.config/superapp-doctor.json` | poprawić ścieżkę; plik, który przepis każe utworzyć, dopisać do wyjątków |

Opcje:

- `--rule <id> …`: tylko wybrane reguły, np. `dotnet superapp doctor --rule doc-links doc-paths` po zmianie dokumentacji;
- `--fix`: najpierw naprawy mechaniczne, potem sprawdzenie; wypisuje zmienione pliki;
- `--json`: jeden dokument JSON (`errors`, `warnings`, `findings` z `rule`, `severity`, `message`, `file`, `line`, `fix`).

Kod wyjścia: `0` bez błędów (ostrzeżenia nie przerywają), `2` co najmniej jeden błąd.

### Wyjątki: `.config/superapp-doctor.json`

Dokumentacja celowo wymienia pliki, których jeszcze nie ma: przepis 07 tworzy serwis `Billing`, przepis 11 experience `Coaching`,
samouczek funkcję ocen. Fragmenty takich ścieżek są w `ignoredPathFragments`, każdy z komentarzem, dlaczego tam jest. Nie dopisuj
tam ścieżek, które powinny istnieć: popraw dokumentację.

## 22.3 `list` i `info`: przegląd

```text
$ dotnet superapp list eventids
Range      Used  Next free  Component
---------  ----  ---------  ------------------------------------------------
1000–1999  0     1001       Knowledge (Api, Application, Infrastructure)
2000–2999  2     2003       Knowledge (Worker)
6000–6999  1     6002       `Example.Bff` (BFF experience Example): ...

$ dotnet superapp info knowledge
Service      Knowledge (src/Services/Knowledge)
Experience   example
Ports        api 5101, worker 5111
Schema       knowledge (login knowledge_app)
Event IDs    1000–1999, 2000–2999
Scopes       knowledge.catalog.read, knowledge.catalog.write, knowledge.library.read, knowledge.library.write
Events       CollectionArchivedV1, MaterialArchivedV1, MaterialPublishedV1
Consumers    CollectionArchivedConsumer, MaterialArchivedConsumer
Used by BFF  example
```

| Polecenie | Kiedy |
|---|---|
| `list ports` | przed nadaniem portu nowemu komponentowi |
| `list eventids` | przed dodaniem logu (`Next free`) albo zakresu nowego komponentu |
| `list scopes` | przed dodaniem scope (unikalność, konwencja `{serwis}.{zasób}.{akcja}`) |
| `list services`, `list bffs`, `list experiences` | mapa experience: które serwisy należą do której experience, kto kogo woła |
| `info <serwis\|experience\|BFF>` | wszystko o jednym elemencie przed zmianą, np. jakie zdarzenia publikuje serwis i kto go woła |

`--root <katalog>` pozwala uruchomić narzędzie spoza repozytorium; domyślnie korzeniem jest najbliższy katalog nadrzędny z
`SuperApp.slnx`.

## 22.4 `add` i `remove`: serwis, BFF, klient

| Polecenie | Co robi | Czego nie robi (następne kroki w wyniku) |
|---|---|---|
| `add service <Nazwa> --experience <exp> [--scope zasób.akcja …]` | szablon `superapp-service` z wolnymi portami; `SuperApp.slnx` i `.sln`; Migrator (`.csproj` i `Program.cs`); `deploy/sql/01-bootstrap.sql`; stałe w `{Nazwa}Scopes`; realm (`{serwis}-api`, `{serwis}-client`, scope z audience, opcjonalne w `bff-web`, `mobile-*`, `dev-cli`); compose (`-api`, `-worker`, scope w `bff-web`); `values-{serwis}.yaml`; prefiks scope w polityce bramy; testy architektury; zakres EventId | agregat i migracja `Initial`, klient w BFF, ADR, opisy scope, wymagania dla CIAM |
| `add bff <Nazwa> [--skip-migration]` | szablon `superapp-bff`; solucje; compose; `values-{exp}.yaml`; `GatewayPolicies` (stała i wpis w `ServiceScopePrefixes`), trasa w `ProxyConfigurationSeed` i migracja bramy (`dotnet ef`); realm (`{exp}-bff-audience`, `{exp}.internal.read`); testy architektury; zakres EventId | serwisy experience, akcje API, ADR, NetworkPolicy i CIAM |
| `add client --bff <exp> --service <Nazwa>` | `Clients/{Serwis}/{serwis}.refitter` i wygenerowany klient (`dotnet refitter`); rejestracja `AddDownstreamApi<I{Serwis}Api>(…).AddUserTokenForwarding()`; `Downstream:{Serwis}:BaseAddress` w obu `appsettings*.json`, compose (z `depends_on`) i Helm | akcje kontrolerów; `dateTimeType` dla serwisu z datami bez strefy |
| `remove service <Nazwa> --yes` | wszystko z `add service` odwrotnie i usunięcie kodu; odmawia, dopóki BFF ma klienta serwisu | baza (wypisuje kroki dla DBA), CIAM |
| `remove bff <Nazwa> --yes [--skip-migration]` | odwrotnie, z migracją bramy usuwającą trasę; odmawia, dopóki do experience należą serwisy | CIAM, konsumenci API wewnętrznego |
| `remove client --bff <exp> --service <Nazwa> --yes` | odwrotnie; odmawia, dopóki kontrolery używają klienta (wypisuje pliki) | zmiana kontraktu BFF dla modułu |

Zasady:

- **Warunki wstępne przed pierwszą zmianą.** Błędna nazwa, brak BFF experience albo zależności przy usuwaniu kończą polecenie kodem
  `4` bez żadnej zmiany.
- **Idempotencja.** Każdy krok sprawdza, czy jego zmiana już jest. Ponowne uruchomienie po przerwanym poleceniu (np. błąd `dotnet ef`)
  dokańcza resztę. `doctor` wskaże, czego brakuje.
- **`add` + `remove` nie zostawiają śladu.** Zakres EventId wraca do puli, a pliki wracają do stanu sprzed `add` (sprawdzają to testy
  `EditorRoundTripTests` na prawdziwych plikach repozytorium).
- **Szablony.** Szablony `dotnet new` są częścią narzędzia (`src/Tools/SuperApp.Cli/Templates`; nie są kompilowane ani pakowane).
  Narzędzie instaluje je z repozytorium, w którym działa. Wcześniej usuwa rejestracje tych
  samych szablonów z innych klonów, więc `dotnet new` nigdy nie użyje starszej kopii.
- **Po poleceniu:** `dotnet build`, `dotnet test`, `dotnet superapp doctor`. Wynik zawiera listę następnych kroków; z `--json`
  pole `nextSteps`.

## 22.5 Praca codzienna: `add usecase`, `migration`, `contracts`

| Polecenie | Co robi |
|---|---|
| `add usecase <Serwis> <Feature> <Nazwa> --scope zasób.akcja [--query] [--dto Nazwa]` | wycinek pionowy (ADR-0032): komenda albo zapytanie z `[RequiresScope]`, walidator, DTO zapytania, handler (komendy w Application, zapytania w Infrastructure, ADR-0026) i akcja kontrolera `POST`/`GET {nazwa-kebab}` (kontroler `{Feature}Controller` powstaje, gdy go nie ma). Szkielet kompiluje się i przechodzi analizatory; wszystko do napisania jest oznaczone `TODO`, a handler rzuca `NotImplementedException`. Scope musi być zadeklarowany |
| `migration add <Serwis\|Gateway> <Nazwa>` | build projektu, `dotnet ef migrations add` z właściwym projektem, kontekstem i katalogiem, plik `{Nazwa}.cs` (ADR-0032) z opisem do uzupełnienia; odmawia dla istniejącej nazwy |
| `migration list [<Serwis\|Gateway>]` | migracje każdego kontekstu w kolejności Migratora, odczytane z plików (bez builda i bazy) |
| `migration script [--output plik]` | jeden idempotentny skrypt SQL wszystkich kontekstów w kolejności Migratora (domyślnie `artifacts/migrations/migrations.sql`, katalog ignorowany): artefakt dla DBA na produkcję (ADR-0004) |
| `contracts` | build (kontrakty OpenAPI serwisów i BFF), Refitter dla każdego `*.refitter`, ponowny build, gdy klient się zmienił; wypisuje zmienione pliki do przeglądu |
| `contracts --check` | to samo bez zostawiania zmian: wypisuje nieaktualne kontrakty i klientów, przywraca pliki i kończy się kodem `2`. Do CI i przed pull requestem |
| `contracts snapshot [--output katalog]` | zapisuje obecne kontrakty (`openapi/*.json` serwisów i BFF-ów) jako wersję bazową, domyślnie w `artifacts/contracts-baseline` (katalog ignorowany) |
| `contracts diff [--baseline katalog \| --git rewizja] [--all]` | porównuje obecne kontrakty z wersją bazową i klasyfikuje każdą różnicę; kod `2`, gdy jest zmiana łamiąca |

`contracts diff` porównuje operacje, parametry, ciała żądań i odpowiedzi, idąc przez `$ref` (zmiana wspólnego schematu jest
zgłaszana przy każdej operacji, która go używa, ze ścieżką pól, np. `GET /v1/categories 200[] .slug`). Reguły odpowiadają tabeli
z [8.11](08-api-i-kontrakty.md#811-zgodność-wsteczna):

| Rodzaj | Przykłady |
|---|---|
| `Breaking` | usunięta operacja albo odpowiedź sukcesu; nowy wymagany parametr, ciało albo pole żądania; pole żądania, które stało się wymagane; usunięte pole odpowiedzi; zmiana typu lub formatu; usunięta wartość enuma albo wariant schematu polimorficznego w żądaniu; usunięty kontrakt |
| `Warning` | usunięty parametr lub pole żądania (klienci nadal je wysyłają); nowa wartość enuma, nowy wariant (`anyOf`/`oneOf`) albo `nullable` w odpowiedzi (klient musi to tolerować) |
| `Compatible` | nowa operacja, opcjonalny parametr lub pole żądania, nowe pole odpowiedzi, nowy kontrakt (widoczne z `--all`) |

Dwa sposoby pracy: bez git `contracts snapshot` przed zmianą, potem `contracts diff`; z git `contracts diff --git origin/main`
(w CI względem gałęzi docelowej pull requestu). Zmiana łamiąca to nowa wersja operacji (`/v{n+1}`), nie zmiana w miejscu.

## 22.6 Elementy domeny i przekrojowe

| Polecenie | Co tworzy (`remove` usuwa to samo) |
|---|---|
| `add aggregate <Serwis> <Folder> <Agregat> [--table …]` | agregat z fabryką `Create`, `{Agregat}Id` (ADR-0023), `{Agregat}Errors`, `I{Agregat}Repository`, konfiguracja EF z `rowversion`, repozytorium zarejestrowane w `Add{Serwis}Core`, test domeny |
| `add event <Serwis> <Agregat> <Nazwa> [--integration]` | zdarzenie domenowe w `Events/`; z `--integration` także kontrakt `{Nazwa}V1` w `Contracts` i translator publikujący przez outbox (ADR-0005, ADR-0027) |
| `add consumer <Serwis> --event <Kontrakt>` | konsument w `{Serwis}.Worker/Consumers` i referencja do `Contracts` wydawcy (jedyny dozwolony projekt innego serwisu) |
| `add scope <Serwis> <zasób.akcja>` | stała w `{Serwis}Scopes` (w stylu pliku), client scope z audience w realmie, scope żądany przez `bff-web` |
| `add flag <Serwis> <nazwa> [--default-on]` | `FeatureFlag` w `{Serwis}FeatureFlags` (klasa powstaje z pierwszą flagą, znika z ostatnią, ADR-0036) |
| `add product-event <Kontrakt> [--name …]` | konsument forwardera analityki mapujący kontrakt na zdarzenie produktowe (identyfikatory, czas, użytkownik), nazwa w `ProductEventNames`, referencja do `Contracts` |
| `remove usecase <Serwis> <Feature> <Nazwa>` | wycinek, handler zapytania i akcja kontrolera; kontroler bez akcji też znika |

`remove` sprawdza, czy element jest używany: przeszukuje kod (bez komentarzy) poza plikami samego elementu. Jeśli znajdzie użycie,
odmawia i wypisuje pliki. Agregat ze snapshotem migracji, scope wymagany przez komendę albo zdarzenie z konsumentem trzeba najpierw
odłączyć ręcznie. Puste katalogi po usuniętych plikach znikają. Pełny cykl wszystkich poleceń (dodanie, build, testy, `doctor`,
usunięcie) zostawia repozytorium identyczne.

## 22.7 Środowisko lokalne: `env` i `e2e`

| Polecenie | Co robi |
|---|---|
| `env up [--build] [--infra] [--timeout s]` | `docker compose` z profilem `app` (z `--infra` bez aplikacji), potem czeka, aż każdy komponent odpowie na `/health/live` (realm na discovery OIDC) |
| `env down [--reset]` | zatrzymuje środowisko; `--reset` usuwa wolumeny (baza i jej dane) |
| `env status` | kontenery ze stanem i health (jednorazowe `db-bootstrap`, `migrator`, `db-gateway-permissions` są poprawne po wyjściu z kodem 0) i sondy HTTP; kod `2`, gdy coś nie działa |
| `env token <użytkownik> [--scope …] [--password …]` | token użytkownika lokalnego realmu (klient `dev-cli`, hasło = nazwa użytkownika), domyślnie ze wszystkimi scope serwisów i BFF-ów |
| `e2e` | scenariusz end-to-end na działającym środowisku: sondy, tokeny, dla każdego BFF trasa przez bramę z `404 http.not_found` i 32-znakowym `traceId`, `401 auth.invalid_token` bez tokenu, brak trasy do `/internal`, API wewnętrzne `200`/`403 auth.missing_scope`, zapis, odczyt i usunięcie wpisu dziennika przez bramę, pełne logowanie do `bff-web` (opis niżej); kod `2`, gdy któreś sprawdzenie nie przejdzie |

Logowanie do `bff-web` w `e2e` przechodzi drogę przeglądarki bez przeglądarki: `/bff/login` → formularz logowania lokalnego realmu
(użytkownik `reader`) → odpowiedź `form_post` na callback bramy → przekierowanie do `/`. Sprawdza ciasteczko `__Host-bff` (`Secure`,
`HttpOnly`, `SameSite=Strict`), `/bff/user` z `scopes` i `logoutUrl`, `401 auth.csrf_header_missing` bez nagłówka `X-CSRF` i `200`
z nim (żądanie SPA przez bramę do BFF experience), `400` przy wylogowaniu z obcym `sid` oraz wylogowanie przez end-session CIAM, po
którym `/bff/user` zwraca `401`. Scenariusz nie zostawia sesji ani danych.

Adresy pochodzą z portów opublikowanych w `docker-compose.yml`, więc nowy serwis albo BFF dodany przez `add` jest od razu sondowany i
testowany. Certyfikat deweloperski bram jest akceptowany tylko dla `localhost`.

## 22.8 Narzędzie i Copilot

Instrukcje Copilota (`.github/copilot-instructions.md`) każą kończyć każdą zmianę poleceniem `dotnet superapp doctor` i poprawiać
zgłoszone błędy przed oddaniem pracy. Narzędzie jest do tego zaprojektowane: nie zadaje pytań, `--json` daje wynik do odczytu
maszynowego, a każde znalezisko zawiera naprawę. Agent kodujący Copilota na GitHubie instaluje narzędzie krokiem z
`.github/workflows/copilot-setup-steps.yml` (`tools/bootstrap.sh`).

## 22.9 Rozwijanie narzędzia

- **Nowa reguła `doctor`:** klasa implementująca `IDoctorRule` w `src/Tools/SuperApp.Cli/Doctor/Rules`, dopisana do `DoctorRules.All`,
  z testem na małym repozytorium tymczasowym (`TestRepository` w `SuperApp.Cli.Tests`). Reguła tylko czyta; jeśli jej znaleziska da
  się naprawić mechanicznie, implementuje też `IFixableDoctorRule` (naprawa idempotentna, dotyka tylko plików reguły).
- **Dane o repozytorium** odczytuje `RepositoryScanner` według konwencji (katalogi serwisów i BFF-ów, profile uruchomieniowe, wartości
  Helm, rejestr EventId). Zmiana tych konwencji wymaga zmiany narzędzia w tej samej zmianie (ADR-0046).
- **Nowe polecenie:** klasa w `Commands` (wzór: `ListCommand`), rejestracja w `CliApplication`, test w `CommandTests`.
- **Nowy krok `add`/`remove`:** edycja pliku jako statyczna metoda w `Scaffolding/Editors` (zmiana tekstu → nowy tekst, bez efektów
  ubocznych), krok w planie (`Scaffolding/Plans`) i test w `EditorRoundTripTests`: dodanie, ponowne dodanie bez zmian, usunięcie
  do oryginału.
- **Reguły `contracts diff`** są w `Contracts/OpenApiDiff` (jedna klasa porównująca dwa dokumenty), testy w `OpenApiDiffTests` na
  zmodyfikowanych kopiach prawdziwego kontraktu Knowledge. Nowa reguła = zmiana tabeli w 22.5 i w 8.11 w tej samej zmianie.
- **Plan z ADR-0046 jest zrealizowany.** Dystrybucja przez wewnętrzny feed NuGet (zamiast `tools/packages`) ma sens dopiero przy wielu
  repozytoriach korzystających z narzędzia.
