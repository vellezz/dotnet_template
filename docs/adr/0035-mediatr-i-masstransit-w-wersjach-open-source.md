# ADR-0035: MediatR i MassTransit w ostatnich wersjach open source

- **Status:** Zaakceptowany (zastępuje ADR-0010)
- **Data:** 2026-09-30
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §2, §9, §14; ADR-0002, ADR-0005, ADR-0010, ADR-0017, ADR-0027, ADR-0034

## Kontekst

ADR-0010 zakładał komercyjne licencje MediatR (v13+) i MassTransit (v9+). Właściciel projektu zdecydował o przejściu na
ostatnie wersje dostępne na licencji open source, bez kluczy licencyjnych.

Stan zweryfikowany 2026-09-30 w nuget.org (metadane pakietów):

| Pakiet | Wersja | Licencja | Uwagi |
|---|---|---|---|
| `MediatR` | 12.5.0 | Apache-2.0 | ostatnia wersja przed zmianą licencji w 13.0; `netstandard2.0` / `net6.0` |
| `MassTransit`, `MassTransit.RabbitMQ`, `MassTransit.EntityFrameworkCore` | 8.5.11 | Apache-2.0 | ostatnia linia 8.x; target `net10.0`, zależność od EF Core 10 |

## Decyzja

- Używamy **MediatR 12.5.0** i **MassTransit 8.5.11** (Central Package Management). Aktualizacja do 13+ / 9+ jest zabroniona bez
  nowego ADR, bo oznacza powrót do licencji komercyjnej.
- Kod nie konfiguruje kluczy licencyjnych; znikają klucze konfiguracji `MediatR:License` i `MassTransit:License`
  (sekrety, Helm, lokalne `.env`), a `AddAppApplication` nie przyjmuje klucza.
- Różnice API uwzględnione w `SuperApp.Framework`:
  - MediatR 12: `RequestHandlerDelegate<T>` bez `CancellationToken` (behaviors wywołują `next()`); `Microsoft.Extensions.Logging.Abstractions`
    referencjonowany jawnie przez `SuperApp.Framework.Application` (MediatR 12 nie dostarcza go przechodnio);
  - MassTransit 8: bez `SetLicense`; inne mapowanie tabel outboxa.
- **Migracja schematu outboxa w stylu expand/contract:** migracja `MassTransit8OutboxModel` w każdym serwisie dodaje indeksy używane
  przez MassTransit 8 i **pozostawia** kolumnę `OutboxState.BusName` (nullable) oraz jej indeks z MassTransit 9, żeby instancje
  w starej wersji działały podczas rolling update. Usunięcie ich to osobna migracja „contract” po zakończeniu wdrożenia.

## Konsekwencje

- Brak kosztów licencji i sekretów licencyjnych; lokalne środowisko (ADR-0034) działa bez kluczy.
- **Ryzyko utrzymania:** MediatR 12.x nie otrzymuje nowych wersji, a MassTransit 8 ma wsparcie bezpieczeństwa ograniczone w czasie
  (planowane do końca 2026). Przed końcem 2026 właściciel projektu decyduje o dalszej drodze (np. wymiana na alternatywę open source).
  Koszt wymiany ogranicza abstrakcja: domena i Application widzą własne porty (`ICommand`, `IQuery`, handlery, behaviors w
  `SuperApp.Framework`, `IIntegrationEventPublisher`); bezpośrednio z bibliotek korzystają kontrolery (`ISender`), konsumenci i
  `SuperApp.Framework.Infrastructure`.
- Monitorowanie podatności pakietów (NuGet audit w buildzie) jest obowiązkowe; podatność bez poprawki w 8.x / 12.x wymaga ADR.
