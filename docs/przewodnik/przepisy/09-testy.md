# Przepis 09: jakie testy napisać, gdzie i jak je uruchomić

**Kiedy:** każda zmiana kodu produkcyjnego: nowa reguła, przypadek użycia, zapytanie, zmiana modelu, migracja, zdarzenie,
cache. Test powstaje w tym samym pull requeście co zmiana.
**Przykład prowadzący:** funkcja „Ocena materiału” z [samouczka 16](../16-samouczek-pelna-funkcja.md) (pełny kod testów na
wszystkich poziomach) oraz istniejące testy Knowledge i SleepDiary.
**Wyjaśnienia:** [12 Testy](../12-testy.md) (mechanizmy, fixture, kolekcje, debugowanie, niestabilne testy).

Fragmenty kodu w krokach 2–4 pochodzą z samouczka: typy `MaterialRating`, `RateMaterial`, `GetMaterialRating` nie istnieją
w repozytorium (kod został tam zbudowany i przetestowany, a potem usunięty). Wzorce w kolumnie „Wzór w repozytorium” są
istniejącymi plikami.

## Zanim zaczniesz

1. Wypisz reguły, które zmiana wprowadza lub zmienia, i dla każdej ustal **najniższy** poziom, na którym da się ją sprawdzić
   (tabela w kroku 1). Reguła agregatu nie potrzebuje bazy; zapytanie SQL nie da się sprawdzić bez bazy.
2. Docker Desktop musi działać dla projektów `*.IntegrationTests` i `SuperApp.Gateway.Tests` (MSSQL w Testcontainers). Pozostałe
   projekty działają bez Dockera.
3. Nie dodawaj bibliotek do mocków ani asercji: repozytorium używa fake'ów w pamięci i asercji xUnit.

## Krok 1: wybierz projekt

| Co zmieniasz | Projekt testów | Styl | Wzór w repozytorium |
|---|---|---|---|
| Reguła agregatu, value object, silne ID, zdarzenie domenowe | `{Serwis}.Domain.Tests` | czyste wywołania, **bez mocków**; asercje na `Error`, stanie i zdarzeniach | `CategoryTests`, `MaterialTests`, `SleepEntryTests` |
| Handler komendy (orkiestracja) | `{Serwis}.Application.Tests` | handler tworzony ręcznie z fake'ami z `Fakes/` | `LibraryHandlerTests`, `RecordSleepEntryTests` |
| Walidator | `{Serwis}.Application.Tests` | `new XValidator().Validate(...)`, granice ze stałych domeny | `TextValidationTests`, `SleepEntryInputValidationTests` |
| Translator zdarzenia integracyjnego | `{Serwis}.Application.Tests` | prawdziwe zdarzenie z agregatu + fake publikatora | `IntegrationEventTranslatorTests`, `SleepEntryRecordedTranslatorTests` |
| Zachowanie pipeline (autoryzacja, walidacja, transakcja) | `{Serwis}.Application.Tests` | `AddAppApplication` + `ISender` + `FakeUnitOfWork` | `PipelineTests` |
| Handler zapytania, repozytorium, konfiguracja EF, migracja, `CHECK`, unikalny indeks, cache | `{Serwis}.IntegrationTests` | MSSQL w Testcontainers, `fixture.SendAsync` albo scope DI | `KnowledgeFlowTests`, `UniqueConstraintTests`, `CacheInvalidationTests`, `SchemaTests` |
| Brama (trasy z bazy, sesje, tokeny, wylogowanie, retry) | `SuperApp.Gateway.Tests` | jednostkowe albo Testcontainers | `GatewayDatabaseTests`, `BffLogoutTests`, `IdempotentRetryHandlerTests` |
| BFF experience (podział kontraktów, klient `AddDownstreamApi`, przekazanie odpowiedzi serwisu, przekazanie tokenu, częściowe renderowanie) | `{Experience}.Bff.Tests` | jednostkowe: odpowiedzi Refit z `Responses`, nagrywający `HttpMessageHandler`, bez sieci | `ContractSplitTests`, `DownstreamClientTests`, `DownstreamResponseTests`, `UserTokenForwardingTests`, `PartialResponseFetcherTests` |
| Nowa reguła granic (nowy ADR) | `SuperApp.ArchitectureTests` | ArchUnitNET albo refleksja | `ArchitectureRules` (m.in. reguły 12–14 dla BFF) |
| Nowa reguła analizatora | `SuperApp.Analyzers.Tests` | źródło z markerami `{|APP00x:...|}` | `AnalyzerTests` |

Kontrolery, JWT, RabbitMQ i Redis nie mają testów automatycznych: sprawdzasz je `curl` w lokalnym środowisku
([13](../13-lokalne-srodowisko-i-debugowanie.md), krok 12 [samouczka](../16-samouczek-pelna-funkcja.md)).

## Krok 2: test domeny

```csharp
public sealed class MaterialRatingTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(RatingScore.Min - 1)]
    [InlineData(RatingScore.Max + 1)]
    [InlineData(-3)]
    public void Score_outside_the_scale_is_rejected(int value) =>
        Assert.Equal(LibraryErrors.InvalidRatingScore, RatingScore.Create(value).Error);
}
```

- Nazwa testu = reguła zdaniem. Granice ze stałych domeny (`RatingScore.Min`), czas jako stała `Now`.
- Asercja na konkretnym `Error` (`Assert.Equal(XxxErrors.Y, result.Error)`), nigdy tylko `IsFailure` i nigdy na komunikacie.
- Po `Create` wywołaj `ClearDomainEvents()`, jeśli test dotyczy zdarzeń kolejnej operacji.

## Krok 3: test handlera komendy

1. Brakujący fake portu dodaj do `tests/{Serwis}.Application.Tests/Fakes/` (`internal sealed`, lista `Items`, semantyka jak
   w prawdziwym repozytorium, np. `IsPublishedAsync` sprawdza status).
2. Stan przygotowuj prawdziwymi metodami agregatów, każdy krok z asercją sukcesu (`Assert.True(material.Publish(now).IsSuccess)`).
3. Handler twórz ręcznie i wywołuj `Handle(command, TestContext.Current.CancellationToken)`.

```csharp
[Fact]
public async Task Rating_unpublished_material_is_rejected()
{
    var draft = ResultAssert.Success(Material.Create(MaterialType.Article, "Szkic", null, null, null, _clock.UtcNow));
    _materials.Add(draft);

    var result = await Handler("user-1").Handle(new RateMaterial(draft.Id.Value, 4), TestContext.Current.CancellationToken);

    Assert.Equal(LibraryErrors.ItemNotAvailable, result.Error);
    Assert.Empty(_ratings.Items);
}

private RateMaterialHandler Handler(string? subject) =>
    new(_ratings, _materials, new FakeCurrentUser(subject), _clock);
```

Wywołany bezpośrednio handler **nie** przechodzi przez autoryzację, walidację ani transakcję. Scope'y i zapis sprawdzasz
w `PipelineTests` (wzór z `FakeUnitOfWork.SaveResult`) albo w teście integracyjnym. `PipelineRegistrationTests` obejmie nową
komendę automatycznie.

## Krok 4: test integracyjny

```csharp
[Collection(PipelineCollection.Name)]
public sealed class MaterialRatingTests(ServiceFixture fixture)
{
    [Fact]
    public async Task Draft_cannot_be_rated_and_has_no_summary()
    {
        fixture.ActWith(KnowledgeScopes.CatalogWrite);
        var draftId = ResultAssert.Success(await fixture.SendAsync(new CreateMaterial(MaterialType.Article, "Szkic", null, null, null)));

        fixture.ActWith(KnowledgeScopes.LibraryWrite);
        Assert.Equal(LibraryErrors.ItemNotAvailable, (await fixture.SendAsync(new RateMaterial(draftId, 4))).Error);

        fixture.ActWith(KnowledgeScopes.CatalogRead);
        Assert.Equal(MaterialErrors.NotFound, (await fixture.SendAsync(new GetMaterialRating(draftId))).Error);
    }
}
```

Reguły:

- `[Collection(PipelineCollection.Name)]` na każdej klasie, która wysyła żądania przez pipeline albo czyta cache (Knowledge).
  W SleepDiary kolekcji jeszcze nie ma: przy drugiej klasie wysyłającej żądania utwórz ją według wzoru z Knowledge.
- Każdy test zaczyna od `fixture.ActWith(...)`; zmianę użytkownika (`fixture.CurrentUser.Subject`) przywracasz w `finally`.
- Dane unikalne: `$"sen-{Guid.NewGuid():N}"`, `$"rater-{Guid.NewGuid():N}"`. Asercje tylko na własnych danych
  (`Assert.Contains(list.Items, item => item.Id == id)`), bo baza jest wspólna dla całego przebiegu.
- Zdarzenia integracyjne sprawdzasz w `fixture.Publisher.Published` po identyfikatorze; to dowód translacji, nie dostarczenia.
- Wyścig na unikalnym indeksie odtwarzasz deterministycznie: dwie sprzeczne encje w jednym scope + `IUnitOfWork.SaveChangesAsync`
  (`UniqueConstraintTests`).
- `CHECK` sprawdzasz surowym SQL i nazwą ograniczenia w `SqlException` (`Database_rejects_invalid_block_even_bypassing_domain`).
- Unieważnianie cache: odczyt → zmiana → odczyt przez `fixture.SendAsync`; kolejność względem commitu: ręczna transakcja
  (`CacheInvalidationTests`).

## Krok 5: uruchom

```bash
# szybka pętla bez Dockera
dotnet test --project src/Services/Knowledge/tests/Knowledge.Domain.Tests --filter-class "*MaterialRatingTests"
dotnet test --project src/Services/Knowledge/tests/Knowledge.Application.Tests --filter-method "*Rating*"

# testy integracyjne jednej klasy (Docker)
dotnet test --project src/Services/Knowledge/tests/Knowledge.IntegrationTests --filter-class "*MaterialRatingTests"

# przed pull requestem: całość, w tym testy architektury i analizatorów
dotnet build SuperApp.slnx
dotnet test --solution SuperApp.slnx
```

Przydatne opcje `dotnet test`: `--list-tests`, `--stop-on-fail`, `--parallel none`, `--show-slowest-tests 5`,
`--filter-not-class "*GatewayDatabaseTests"`. Uruchamiając bezpośrednio plik `.exe` projektu testów używasz składni natywnego
runnera xUnit: `-class`, `-method`, `-waitForDebugger` ([12.10](../12-testy.md)).

## Krok 6: sprawdź przed oddaniem

- [ ] Każda nowa reguła ma test na najniższym możliwym poziomie; nazwa testu opisuje regułę.
- [ ] Asercje na `Error`, nie na `IsFailure` ani komunikacie.
- [ ] Brak `DateTimeOffset.UtcNow` w testach domeny i aplikacji (stała `Now`, `FakeClock`).
- [ ] Testy integracyjne: kolekcja, `ActWith` na początku, dane z `Guid`, asercje na własnych danych.
- [ ] Nowy serwis: Api i Worker referowane z `tests/SuperApp.ArchitectureTests/SuperApp.ArchitectureTests.csproj`.
- [ ] `dotnet test --solution SuperApp.slnx` zielony; test, który „czasem” pada, jest błędem do naprawy, nie do ponownego uruchomienia.

## Typowe błędy

| Objaw | Przyczyna | Naprawa |
|---|---|---|
| `error xUnit1051` | brak `TestContext.Current.CancellationToken` | przekaż token |
| `error APP001` w teście | zignorowany `Result` | `Assert.True(...IsSuccess)` albo użycie wyniku |
| `error CS1591` w nowym projekcie testów | nazwa projektu bez końcówki `Tests` | zmień nazwę projektu |
| Wszystkie testy integracyjne padają w `InitializeAsync` | Docker nie działa, brak obrazu MSSQL | uruchom Docker, `docker pull mcr.microsoft.com/mssql/server:2022-latest` |
| Test przechodzi sam, pada w pełnym przebiegu | stałe dane unikalne, asercja na danych całej bazy, brak kolekcji | `Guid` w danych, asercje na własnych ID, `[Collection(PipelineCollection.Name)]` |
| Losowe `auth.missing_scope` | brak `ActWith` na początku testu albo brak kolekcji | dodaj oba |
| `Unable to resolve service for type ...` w teście pipeline | brak rejestracji fake'a portu | `services.AddSingleton<IPort>(fake)` |
| Test handlera „autoryzacji” zawsze zielony | handler wywołany bezpośrednio pomija `AuthorizationBehavior` | test przez pipeline lub integracyjny |
| `unknown option: --filter-class` | bezpośrednie uruchomienie `.exe` | `-class` albo `dotnet test --filter-class` |
| Reguły architektury nie widzą nowego serwisu | brak referencji w `SuperApp.ArchitectureTests.csproj` | dodaj `ProjectReference` do Api i Worker |
