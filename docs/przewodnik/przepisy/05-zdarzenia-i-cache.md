# Przepis 05: zdarzenia i cache

**Kiedy:** zmiana agregatu ma wywołać reakcję: unieważnić cache, poinformować inny serwis albo (asynchronicznie) zmienić
inne agregaty; albo nowe zapytanie ma być serwowane z cache.
**Przykłady z kodu:** archiwizacja materiału w Knowledge (`MaterialArchived` → `MaterialArchivedV1` →
`MaterialArchivedConsumer` → `RemoveFavoritesOfItem`) oraz lista kategorii z cache (`ListCategoriesHandler`,
`CategoryCacheInvalidation`).
**Wyjaśnienia:** [10 Zdarzenia i integracja](../10-zdarzenia-i-integracja.md), [11 Cache](../11-cache.md).

## Szybki start

```bash
dotnet superapp add event Knowledge Material MaterialPublished --integration   # zdarzenie domenowe + MaterialPublishedV1 + translator
dotnet superapp add consumer SleepDiary --event MaterialPublishedV1            # konsument w Workerze + referencja do Contracts
```

Wywołanie `Raise(...)` w metodzie agregatu, komendę wysyłaną przez konsumenta i kolejkę KEDA dopisujesz według kroków poniżej. Obie
operacje mają `remove`, które odmawia, dopóki zdarzenie lub konsument są używane. [22 Narzędzie](../22-narzedzie-superapp.md).

## Którą część przepisu potrzebujesz

| Chcesz... | Części |
|---|---|
| zareagować w tym samym serwisie i tej samej transakcji | A |
| poinformować inny serwis albo Worker własnego serwisu | A + B |
| zmienić inne agregaty w reakcji na zmianę (spójność ostateczna) | A + B + C |
| serwować zapytanie z cache | D + E (E wymaga A) |

## Pliki, które powstają (pełny wariant, Knowledge)

```
Knowledge.Domain/Materials/Events/MaterialArchived.cs                         A  zdarzenie domenowe
Knowledge.Domain/Materials/Material.cs                                        A  Raise(...) w metodzie agregatu
Knowledge.Contracts/MaterialArchivedV1.cs                                     B  zdarzenie integracyjne (kontrakt)
Knowledge.Application/IntegrationEvents/MaterialArchivedTranslator.cs         B  translator
Knowledge.Application/Features/Library/RemoveFavoritesOfItem/*.cs             C  komenda wysyłana przez konsumenta
Knowledge.Worker/Consumers/MaterialArchivedConsumer.cs                        C  konsument
Knowledge.Infrastructure/Caching/KnowledgeCache.cs                            D  klucze, tagi, opcje
Knowledge.Infrastructure/Features/Categories/ListCategoriesHandler.cs         D  handler zapytania z cache
Knowledge.Infrastructure/Caching/CategoryCacheInvalidation.cs                 E  unieważnienie po commicie
tests: Domain.Tests (A), Application.Tests (B), IntegrationTests (B, C, E)
```

---

## A. Zdarzenie domenowe

**Krok A1: rekord zdarzenia** obok agregatu, w czasie przeszłym, z danymi potrzebnymi handlerom i z czasem zmiany:

```csharp
// Knowledge.Domain/Materials/Events/MaterialArchived.cs
public sealed record MaterialArchived(MaterialId MaterialId, DateTimeOffset ArchivedAt) : IDomainEvent;
```

**Krok A2: `Raise` w metodzie agregatu**, po zmianie stanu i sprawdzeniu reguł, tylko przy faktycznej zmianie:

```csharp
public Result Archive(DateTimeOffset now)
{
    if (Status == PublicationStatus.Archived)
    {
        return Result.Success();          // no-op: bez zmiany i bez zdarzenia
    }

    Status = PublicationStatus.Archived;
    Touch(now);                           // UpdatedAt + MaterialChanged (raz na zapis)
    Raise(new MaterialArchived(Id, now)); // czas zdarzenia = czas zapisany w agregacie
    return Result.Success();
}
```

**Krok A3: test domeny** (bez mocków; w repozytorium podobnie `MaterialTests.Archived_event_carries_the_archiving_time_of_the_aggregate`):

```csharp
[Fact]
public void Archiving_raises_event_with_the_archiving_time_and_second_archive_raises_nothing()
{
    var material = ResultAssert.Success(Material.Create(MaterialType.Article, "Higiena snu", null, null, null, Created));

    Assert.True(material.Archive(Changed).IsSuccess);
    Assert.Equal(Changed, Assert.Single(material.DomainEvents.OfType<MaterialArchived>()).ArchivedAt);

    material.ClearDomainEvents();
    Assert.True(material.Archive(Changed.AddHours(1)).IsSuccess);
    Assert.Empty(material.DomainEvents);
}
```

Nic nie rejestrujesz: handlery `IDomainEventHandler<T>` znajduje `AddAppApplication`. Handler zdarzenia domenowego **nie**
zmienia innych agregatów, **nie** wywołuje `SaveChangesAsync`, **nie** woła HTTP/e-maila, **nie** czyta zegara
([10.2](../10-zdarzenia-i-integracja.md#102-cykl-życia-zdarzenia-domenowego-krok-po-kroku)).

---

## B. Publikacja zdarzenia integracyjnego

**Krok B1: kontrakt** w `{Serwis}.Contracts`: `sealed record`, tylko prymitywy, wersja w nazwie, dokumentacja XML mówiąca
**kiedy** zdarzenie jest publikowane, jaka jest gwarancja dostarczenia i co znaczy każde pole (wzór: `MaterialArchivedV1.cs`):

```csharp
// Knowledge.Contracts/MaterialArchivedV1.cs
public sealed record MaterialArchivedV1(Guid MaterialId, DateTimeOffset ArchivedAt);
```

Test architektury (reguła 4) odrzuci kontrakt z silnym ID, value objectem albo zależnością od Domain.

**Krok B2: translator** w `{Serwis}.Application/IntegrationEvents/`, `internal sealed`:

```csharp
internal sealed class MaterialArchivedTranslator(IIntegrationEventPublisher publisher) : IDomainEventHandler<MaterialArchived>
{
    public Task HandleAsync(MaterialArchived domainEvent, CancellationToken cancellationToken) =>
        publisher.PublishAsync(new MaterialArchivedV1(domainEvent.MaterialId.Value, domainEvent.ArchivedAt), cancellationToken);
}
```

- Wiadomość trafia do outboxa (`knowledge.OutboxMessage`) w transakcji komendy; Worker wyśle ją po commicie. Brak zmian
  w konfiguracji MassTransit: robi to `AddAppMessaging`.
- Czas z `domainEvent`, nigdy z `IClock`.
- Nigdy nie publikuj z handlera komendy ani kontrolera.

**Krok B3: test translatora** (Application.Tests, `FakeIntegrationEventPublisher`):

```csharp
[Fact]
public async Task Material_archived_carries_the_aggregate_archiving_time()
{
    var material = ResultAssert.Success(Material.Create(MaterialType.Article, "Higiena snu", null, null, null, Created));
    Assert.True(material.Archive(Changed).IsSuccess);
    var domainEvent = material.DomainEvents.OfType<MaterialArchived>().Single();

    await new MaterialArchivedTranslator(_publisher).HandleAsync(domainEvent, TestContext.Current.CancellationToken);

    Assert.Equal(new MaterialArchivedV1(material.Id.Value, Changed), Assert.Single(_publisher.Published));
}
```

**Krok B4: test integracyjny** przepływu komenda → zapis → publikacja (`fixture.Publisher` to `TestIntegrationEventPublisher`):

```csharp
Assert.True((await fixture.SendAsync(new ArchiveMaterial(materialId))).IsSuccess);
Assert.Contains(fixture.Publisher.Published, message => message is MaterialArchivedV1 archived && archived.MaterialId == materialId);
```

**Zmiana istniejącego kontraktu:** nowe pole opcjonalne na końcu (`string? X = null`) jest zgodne wstecz. Usunięcie, zmiana
nazwy, typu albo znaczenia pola, a także zmiana nazwy rekordu lub namespace, to zmiana łamiąca: nowy typ `...V2`, translator
publikuje oba typy, konsumenci przechodzą, potem `V1` znika
([10.8](../10-zdarzenia-i-integracja.md#108-ewolucja-kontraktu-od-v1-do-v2)).

---

## C. Konsument w Workerze

**Krok C1: komenda**, którą wykona konsument: zwykła komenda Application (komenda, walidator, handler, `[RequiresScope]`),
**idempotentna**. Wzór: `RemoveFavoritesOfItem` (usunięcie ulubionych elementu, który ich nie ma, to no-op).

Jeśli komenda dotyczy użytkownika, przekaż jego identyfikator w komendzie: w Workerze `ICurrentUser` to `SystemCurrentUser`
(wszystkie scope, `Subject == null`), więc `RequireUserId()` zwróci błąd.

**Krok C2: konsument** w `{Serwis}.Worker/Consumers/`, cienki:

```csharp
public sealed partial class MaterialArchivedConsumer(ISender sender, ILogger<MaterialArchivedConsumer> logger) : IConsumer<MaterialArchivedV1>
{
    public async Task Consume(ConsumeContext<MaterialArchivedV1> context)
    {
        var result = await sender.Send(new RemoveFavoritesOfItem(FavoriteItemType.Material, context.Message.MaterialId), context.CancellationToken);
        if (result.IsFailure)
        {
            LogRejected(logger, context.Message.MaterialId, result.Error.Code);   // błąd biznesowy: log i potwierdzenie
        }
    }

    [LoggerMessage(2001, LogLevel.Warning, "Removing favorites of archived material {MaterialId} rejected: {ErrorCode}")]
    private static partial void LogRejected(ILogger logger, Guid materialId, string errorCode);
}
```

- Rejestracja automatyczna (`bus.AddConsumers(typeof(Program).Assembly)` w `Program.cs` Workera). Kolejka powstaje sama:
  `{serwis}-{nazwa-konsumenta-bez-Consumer}` w kebab-case, typ quorum, np. `knowledge-material-archived`.
- **Nie łap wyjątków.** Wyjątek techniczny → retry 100 ms, 500 ms, 1 s, 5 s → kolejka `..._error` (alert). Błąd biznesowy
  (`Result`) → log `[LoggerMessage]` i normalne zakończenie.
- Komenda nie może zmienić stanu przed zwróceniem błędu: w konsumencie outbox MassTransit zatwierdza transakcję także po
  nieudanym `Result` ([10.6](../10-zdarzenia-i-integracja.md#106-konsumenci)).
- Nowy `EventId` z zakresu Workera serwisu w [rejestrze](../../logowanie-eventid.md) (Knowledge Worker: 2000–2999); dopisz
  go do tabeli „Użyte”.
- Idempotencja: inbox odrzuca powtórzenie tej samej wiadomości, ale **komenda i tak musi** dawać ten sam efekt przy
  ponownym wykonaniu. Nie zakładaj kolejności wiadomości.

**Krok C3: test.** Konsument nie ma osobnego testu; testujesz komendę, którą wysyła, integracyjnie:

```csharp
// The MaterialArchivedV1 consumer in the Worker sends this command (ADR-0028).
Assert.True((await fixture.SendAsync(new RemoveFavoritesOfItem(FavoriteItemType.Material, materialId))).IsSuccess);
Act(KnowledgeScopes.LibraryRead);
Assert.DoesNotContain(ResultAssert.Success(await fixture.SendAsync(new ListMyFavorites())).Items, item => item.ItemId == materialId);
```

Dobrze jest dodać przypadek „wykonane dwa razy” (idempotencja). Przejście przez RabbitMQ sprawdzasz ręcznie (część F).

**Zdarzenie innego serwisu:** referencja `{Konsument}.Worker` → `{Dostawca}.Contracts`, obcy typ zostaje w konsumencie,
komenda mówi językiem konsumenta. Reguła 1 testów architektury (`Service_does_not_depend_on_other_services`) dopuszcza
zależność od cudzego `Contracts`, ale nie od innych warstw obcego serwisu
([10.9](../10-zdarzenia-i-integracja.md#109-konsumowanie-zdarzeń-innego-serwisu)). Zdarzenie dla analityki produktowej:
[przepis 10](10-zdarzenie-analityczne-i-feature-flag.md).

---

## D. Zapytanie z cache

**Krok D1: wpis w klasie cache serwisu** (`{Serwis}.Infrastructure/Caching/{Serwis}Cache.cs`): klucz z wersją i wszystkimi
parametrami, tag bez wersji, opcje:

```csharp
// Knowledge.Infrastructure/Caching/KnowledgeCache.cs
public const string CategoriesKey = "knowledge:categories:v1";   // zmiana kształtu DTO → v2
public const string CategoriesTag = "knowledge:categories";      // bez wersji
public static readonly FailSafeOptions Categories = new(Fresh: TimeSpan.FromMinutes(5), MaxStale: TimeSpan.FromHours(1));

// klucz z parametrem
public static string MaterialKey(Guid materialId) => $"knowledge:material:v1:{materialId}";
public static string MaterialTag(Guid materialId) => $"knowledge:material:{materialId}";
```

Dobór opcji: `Fresh` = ile użytkownik może widzieć stare dane, gdyby unieważnienie zawiodło; `MaxStale` = jak długo odpowiadać
starymi danymi przy awarii źródła; `LocalExpiration` zostaw domyślne 30 s ([11.4](../11-cache.md#114-dobór-failsafeoptions)).

**Krok D2: handler zapytania** w `{Serwis}.Infrastructure/Features/...`:

```csharp
internal sealed class ListCategoriesHandler(KnowledgeReadDbContext db, FailSafeCache cache)
    : IQueryHandler<ListCategories, Result<IReadOnlyList<CategoryDto>>>
{
    public async Task<Result<IReadOnlyList<CategoryDto>>> Handle(ListCategories query, CancellationToken cancellationToken)
    {
        var categories = await cache.GetOrCreateAsync(
            KnowledgeCache.CategoriesKey,
            async token => (IReadOnlyList<CategoryDto>)await db.Categories
                .OrderBy(category => category.Name)
                .Select(category => new CategoryDto(category.Id, category.Name, category.Slug))
                .ToListAsync(token),
            KnowledgeCache.Categories,
            [KnowledgeCache.CategoriesTag],
            cancellationToken);

        return Result<IReadOnlyList<CategoryDto>>.Success(categories);
    }
}
```

Sprawdź przed użyciem cache:

- [ ] wynik nie zależy od użytkownika (albo użytkownik jest w kluczu i tagu),
- [ ] wynik nie zależy od uprawnień (szkice, widok redaktora: osobna ścieżka bez cache, wzór `GetMaterialHandler`),
- [ ] cache'owany jest DTO serializowalny przez `System.Text.Json`, nie encja ani read model,
- [ ] istnieje zdarzenie domenowe zgłaszane przy **każdej** zmianie tych danych (inaczej część E jest niemożliwa),
- [ ] to strona odczytu albo ACL; nigdy komenda.

---

## E. Unieważnienie po commicie

**Krok E1: handler zdarzenia domenowego** w `{Serwis}.Infrastructure/Caching/`, rejestrujący usunięcie tagu przez
`IUnitOfWork.OnCommitted`:

```csharp
// Knowledge.Infrastructure/Caching/CategoryCacheInvalidation.cs
internal sealed class CategoryCacheInvalidation(IUnitOfWork unitOfWork, FailSafeCache cache) : IDomainEventHandler<CategoryChanged>
{
    public Task HandleAsync(CategoryChanged domainEvent, CancellationToken cancellationToken)
    {
        unitOfWork.OnCommitted(token => cache.RemoveByTagAsync(KnowledgeCache.CategoriesTag, token).AsTask());
        return Task.CompletedTask;
    }
}
```

Dlaczego nie `await cache.RemoveByTagAsync(...)` wprost: handler działa **przed** commitem. Równoległy odczyt wczytałby wtedy
stare (wciąż zatwierdzone) dane i zapisał je w cache na `Fresh`. Akcja `OnCommitted` wykonuje się po commicie (także
transakcji konsumenta w Workerze), przy wycofaniu jest porzucana, a jej błąd jest tylko logowany (`EventId` 210)
([11.7](../11-cache.md#117-unieważnianie-po-commicie)).

Pamiętaj: unieważnienie nie czyści L1 innych replik; przez do 30 s mogą zwracać starą wartość.

**Krok E2: test integracyjny**: przed commitem wpis nadal jest, po commicie odczyt zwraca nowe dane, po wycofaniu wpis
zostaje. Wzór: `CacheInvalidationTests` (`Category_list_is_invalidated_after_commit_and_read_returns_fresh_data`,
`Rolled_back_change_does_not_invalidate_category_list`, `Cached_material_view_is_fresh_after_update`). Najprostsza wersja:

```csharp
fixture.ActWith(KnowledgeScopes.CatalogRead);
Assert.Equal("Stary tytuł", ResultAssert.Success(await fixture.SendAsync(new GetMaterial(materialId))).Title);   // wpis w cache

fixture.ActWith(KnowledgeScopes.CatalogWrite);
Assert.True((await fixture.SendAsync(new UpdateMaterialDetails(materialId, "Nowy tytuł", null, null, null))).IsSuccess);

fixture.ActWith(KnowledgeScopes.CatalogRead);
Assert.Equal("Nowy tytuł", ResultAssert.Success(await fixture.SendAsync(new GetMaterial(materialId))).Title);    // unieważniony po commicie
```

---

## F. Sprawdzenie lokalnie

```bash
dotnet build SuperApp.slnx
dotnet test --solution SuperApp.slnx                                         # w tym testy architektury; wymaga Dockera
docker compose -f deploy/local/docker-compose.yml --profile app up -d --build
```

- Wywołaj endpoint komendy (token z `dev-cli`, jak w [01 Start](../01-start.md)).
- Outbox: `SELECT TOP 20 MessageType, SentTime FROM knowledge.OutboxMessage ORDER BY SequenceNumber DESC;` (pusto po chwili =
  Worker wysłał; rośnie = Worker nie działa).
- RabbitMQ: http://localhost:15672 (guest / guest), exchange `Knowledge.Contracts:MaterialArchivedV1`, kolejka
  `knowledge-material-archived`, ewentualnie `knowledge-material-archived_error` z nagłówkami `MT-Fault-*`.
- Inbox: `SELECT TOP 20 MessageId, Received, Consumed FROM knowledge.InboxState ORDER BY Received DESC;`
- Cache: `docker compose -f deploy/local/docker-compose.yml exec redis redis-cli --scan --pattern 'knowledge:*'`.

## Checklista

- [ ] Zdarzenie domenowe: czas przeszły, w `Domain/.../Events`, zgłaszane po zmianie stanu, tylko przy faktycznej zmianie.
- [ ] Handlery zdarzeń: bez zmian innych agregatów, bez zapisu, bez wywołań zewnętrznych, bez zegara.
- [ ] Kontrakt: prymitywy, wersja w nazwie, dokumentacja XML; zmiana zgodna wstecz albo `V2`.
- [ ] Translator w `Application/IntegrationEvents`, czas z agregatu, test z `FakeIntegrationEventPublisher`.
- [ ] Konsument cienki, bez `try/catch` na wyjątki techniczne, log błędu biznesowego przez `[LoggerMessage]` z nowym `EventId`.
- [ ] Komenda konsumenta idempotentna i nie zmienia stanu przed zwróceniem błędu.
- [ ] Cache tylko w handlerze zapytania/ACL; klucz z wersją i wszystkimi parametrami; tag bez wersji; wpisy w `{Serwis}Cache`.
- [ ] Unieważnienie przez `OnCommitted` dla każdego zdarzenia zmieniającego cache'owane dane; test w `IntegrationTests`.
- [ ] `dotnet build` bez ostrzeżeń, `dotnet test` zielone.
