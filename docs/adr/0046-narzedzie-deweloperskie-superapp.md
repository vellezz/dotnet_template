# ADR-0046: Narzędzie deweloperskie `dotnet superapp`

- **Status:** Zaakceptowany
- **Data:** 2026-10-01
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §13, §15; ADR-0008, ADR-0016, ADR-0030, ADR-0034, ADR-0038

## Kontekst

Szablony `dotnet new superapp-service` i `superapp-bff` (ADR-0030) generują tylko projekty. Nowy serwis lub BFF wymaga potem
kilkunastu ręcznych zmian w kilkunastu plikach: solucje, Migrator, bootstrap SQL, realm, docker compose, Helm, polityka bramy, testy
architektury, porty, zakres EventId, dokumentacja (przepisy 07 i 11). Te kroki są powtarzalne, łatwo w nich o pominięcie, a błędy
wychodzą dopiero przy uruchomieniu albo wdrożeniu. Te same kroki wykonuje też asystent (GitHub Copilot), który przy długich listach
myli się częściej niż przy jednym poleceniu.

Spójność repozytorium sprawdzały dotąd jednorazowe skrypty, poza repozytorium.

## Decyzja

- Repozytorium ma własne narzędzie wiersza poleceń **`dotnet superapp`**: projekt `src/Tools/SuperApp.Cli` (pakiet `SuperApp.Cli`,
  polecenie `superapp`), parsowanie argumentów przez **`System.CommandLine`** (biblioteka zespołu .NET, MIT).
- **Dystrybucja jako narzędzie lokalne .NET z lokalnego źródła pakietów:**
  - `tools/bootstrap.ps1` / `tools/bootstrap.sh` pakują narzędzie do `tools/packages` i uruchamiają `dotnet tool restore`;
  - manifest `.config/dotnet-tools.json` przypina dokładną wersję;
  - `nuget.config` przez *package source mapping* bierze `SuperApp.Cli` wyłącznie z `tools/packages`, więc pakiet o tej samej nazwie
    w nuget.org nigdy go nie zastąpi;
  - każda zmiana narzędzia podnosi `<Version>` w projekcie i w manifeście; `doctor` sprawdza ich zgodność.
- **Zasady poleceń** (dla ludzi, CI i asystentów):
  - bez pytań interaktywnych, wszystko w argumentach;
  - `--json` dla wyniku maszynowego;
  - stałe kody wyjścia: 0 sukces, 1 błędne argumenty, 2 `doctor` znalazł błędy, 3 brak repozytorium lub elementu;
  - każde znalezisko podaje plik i gotową naprawę;
  - operacje zmieniające są idempotentne; usuwające (etap 2) pokazują plan i wymagają `--yes`; `remove` nigdy nie usuwa danych
    (schemat bazy zostaje, wymagania dla DBA są wypisywane).
- **Etap 1 (ten ADR):** polecenia tylko do odczytu:
  - `doctor [--fix] [--rule …]`: spójność solucji (`SuperApp.slnx` i `SuperApp.sln`), rejestracja serwisów i BFF-ów, porty,
    EventId względem rejestru, wersja narzędzia, linki i ścieżki w dokumentacji; `--fix` generuje `SuperApp.sln` ze `SuperApp.slnx`;
  - `list services|bffs|experiences|scopes|ports|eventids`;
  - `info <serwis|BFF>`.
- Wyjątki reguł `doctor` właściwe dla repozytorium (np. pliki, które przepis każe dopiero utworzyć) są w
  `.config/superapp-doctor.json`, nie w kodzie narzędzia.
- Repozytorium samo przechodzi `doctor` bez błędów: pilnuje tego test `RepositoryDoctorTests`, więc każde `dotnet test` (lokalnie
  i w CI) sprawdza spójność.
- **Kolejne etapy** (osobne zmiany, ten sam ADR jako podstawa):
  - `add`/`remove` dla `service`, `bff`, `client`;
  - `add usecase`, `migration add|list|script`, `contracts`;
  - `aggregate`, `event`, `consumer`, `scope`, `flag`, `product-event`;
  - `env`, `e2e`.

## Konsekwencje

- Po sklonowaniu repozytorium trzeba raz uruchomić `tools/bootstrap.ps1` (lub `.sh`); robi to też `copilot-setup-steps.yml` dla
  agenta Copilota.
- Przepisy 07 i 11 oraz checklista kończą się na `dotnet superapp doctor`; po wejściu etapu 2 przepisy zaczną się od polecenia
  `add`, a kroki ręczne zostaną opisem tego, co robi narzędzie.
- Konwencje, na których opiera się narzędzie (nazwy katalogów, profili uruchomieniowych, plików wartości Helm, wierszy rejestru
  EventId), stają się kontraktem: ich zmiana wymaga zmiany narzędzia w tej samej zmianie.
- Nowy pakiet `System.CommandLine` w Central Package Management; używa go tylko narzędzie.
