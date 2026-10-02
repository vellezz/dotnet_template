# ADR-0032: Jeden typ na plik, katalogi według odpowiedzialności

- **Status:** Zaakceptowany
- **Data:** 2026-09-30
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §15; ADR-0024, ADR-0026, ADR-0030

## Kontekst

Część plików zawierała kilka typów (np. komenda, walidator i handler w jednym pliku wycinka, wszystkie
ID kontekstu w `Ids.cs`, wszystkie read modele w `ReadModels.cs`). Utrudnia to wyszukiwanie typu po nazwie
pliku, przeglądy zmian i nawigację po repozytorium.

## Decyzja

- **Każdy typ najwyższego poziomu** (klasa, rekord, struktura, interfejs, enum, delegat) **leży we własnym pliku.**
- **Nazwa pliku = nazwa typu:** `Order.cs`; typ generyczny: `Result{T}.cs`, `ICommandHandler{TCommand,TResponse}.cs`
  (gdy obok istnieje typ niegeneryczny o tej samej nazwie lub dla czytelności); dodatkowa część typu `partial`:
  `Order.Log.cs`.
- Typy zagnieżdżone są częścią typu zewnętrznego i mogą zostać w jego pliku.
- Pionowy wycinek Application zachowuje folder `Features/{Agregat}/{PrzypadekUzycia}/`, a w nim osobne pliki:
  `{PrzypadekUzycia}.cs` (komenda lub zapytanie), `{PrzypadekUzycia}Validator.cs`, `{PrzypadekUzycia}Handler.cs`, DTO.
- Wymuszenie w `SuperApp.Analyzers` (błąd kompilacji, testy w `SuperApp.Analyzers.Tests`):
  - **APP004:** więcej niż jeden typ najwyższego poziomu w pliku;
  - **APP005:** nazwa pliku niezgodna z nazwą typu.
- Kod generowany (migracje EF, source generatory) jest wyłączony z analizy.

### Katalogi i przestrzenie nazw

- **Przestrzeń nazw = ścieżka katalogu** w projekcie; wymusza to IDE0130 (`dotnet_style_namespace_match_folder`) jako błąd kompilacji.
- **Katalog grupuje typy według tego, czego dotyczą**, nie według rodzaju pliku w płaskim worku:
  - Framework: `SuperApp.Framework.Domain.{Aggregates, Events, Results, ValueObjects}`,
    `SuperApp.Framework.Application.{Messaging, Events, Persistence, Security, Pagination, Time, Telemetry, Behaviors}`,
    `SuperApp.Framework.Infrastructure.{Api, Caching, Events, HealthChecks, Hosting, Http/ClientCredentials, Messaging, OpenApi,
    Persistence, Persistence/Conventions, Security, Telemetry, Time, ValueObjects}`;
  - Domain serwisu: katalog per agregat (`Materials`, `Collections`, …) z silnym ID, błędami, repozytorium i podkatalogiem `Events`;
    `Common` tylko dla pojęć współdzielonych przez agregaty kontekstu;
  - Application serwisu: `Features/{Agregat}/{PrzypadekUzycia}`, `IntegrationEvents`, modele wymiany w katalogu pojęcia (np. `Content/Blocks`);
  - Infrastructure serwisu: `Features/{Agregat}` (handlery zapytań), `Caching`, `Persistence/Write/{Configurations, Repositories}`,
    `Persistence/Read/{Models, Configurations}`, `Migrations`;
  - testy: `Fakes` (testy Application), `Infrastructure` (fixture i porty testowe testów integracyjnych).
- Konteksty EF stosują konfiguracje encji z własnej przestrzeni nazw **i przestrzeni podrzędnych**, dzięki czemu konfiguracje
  leżą w `Persistence/Write/Configurations` i `Persistence/Read/Configurations`.

## Konsekwencje

- Więcej plików, ale każdy typ znajduje się po nazwie pliku; diff zmiany jednego typu dotyczy jednego pliku.
- Szablon serwisu (`src/Tools/SuperApp.Cli/Templates/superapp-service`, ADR-0030) spełnia regułę, więc nowe serwisy startują zgodne z nią.
