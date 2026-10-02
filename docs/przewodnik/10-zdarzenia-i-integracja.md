# 10. Zdarzenia i integracja

**Czego się nauczysz:** czym różnią się zdarzenia domenowe od integracyjnych i kiedy użyć którego; jak dokładnie przebiega
zapis komendy z dispatchem zdarzeń w `WriteDbContextBase`; jak zdarzenie trafia do outboxa, potem do RabbitMQ i do
konsumenta w Workerze; jak MassTransit jest skonfigurowany w `AddAppMessaging`; jak pisać translatory i konsumentów;
co oznacza „co najmniej raz” i jak zapewnić idempotencję; jak rozwijać kontrakty (`V1` → `V2`); jak podejrzeć wiadomości
lokalnie i jak to wszystko testować.

**Wymagania wstępne:** [05 Model domeny](05-model-domeny.md) (agregaty, `Raise`), [06 Warstwa aplikacji](06-warstwa-aplikacji.md)
(pipeline MediatR, `TransactionBehavior`), [07 Dane i EF Core](07-dane-i-ef-core.md) (`WriteDbContext`, repozytoria).
Lokalne środowisko z [01 Start](01-start.md).

> **W skrócie**
>
> - Agregat **zgłasza** zdarzenie domenowe (`Raise(new MaterialArchived(...))`), nikogo nie wywołuje.
> - `IUnitOfWork.SaveChangesAsync` (`WriteDbContextBase`) zbiera zdarzenia, **czyści je**, wywołuje handlery
>   `IDomainEventHandler<T>` i dopiero potem zapisuje wszystko jednym `SaveChangesAsync`, w transakcji komendy.
> - Translator (handler w `Application/IntegrationEvents`) zamienia zdarzenie domenowe na integracyjne (`MaterialArchivedV1`
>   z `{Serwis}.Contracts`) i przekazuje je do `IIntegrationEventPublisher`. Wiadomość ląduje w tabeli **outboxa** w tej samej
>   transakcji co zmiana agregatu.
> - Po commicie **Worker** (jedyny proces z `OutboxDelivery.Enabled`) wysyła wiadomość do RabbitMQ. Dostarczenie jest
>   **co najmniej raz**; konsument musi być idempotentny (pomaga mu inbox).
> - Konsument jest cienki: wiadomość → komenda MediatR, wysłana jako tożsamość systemowa Workera. Błąd biznesowy: log
>   i potwierdzenie. Wyjątek techniczny: retry 100 ms / 500 ms / 1 s / 5 s, potem kolejka `_error`.
> - Kontrakty integracyjne zmieniasz tylko wstecznie zgodnie; zmiana łamiąca to nowy typ `...V2`.

W blokach kodu z repozytorium pominięto komentarze dokumentacji XML (`///`); reszta jest skopiowana z plików wskazanych nad
blokiem.

---

## 10.1 Dwa rodzaje zdarzeń

| | Zdarzenie domenowe | Zdarzenie integracyjne |
|---|---|---|
| Przykład | `MaterialArchived`, `MaterialChanged`, `SleepEntryRecorded` | `MaterialArchivedV1`, `SleepEntryRecordedV1` |
| Gdzie leży | `{Serwis}.Domain/{Agregat}/Events/` | `{Serwis}.Contracts/` |
| Interfejs | `IDomainEvent` (`SuperApp.Framework.Domain.Events`) | brak; zwykły `sealed record` |
| Kto widzi | tylko ten serwis, ten sam proces, ten sam scope DI | inne serwisy (i ten sam serwis w Workerze) przez RabbitMQ |
| Typy właściwości | dowolne typy domenowe (`MaterialId`, `MaterialType`, `UserId`) | **wyłącznie prymitywy** (`Guid`, `string`, `DateTimeOffset`, `DateOnly`, `int`) |
| Kiedy obsłużone | synchronicznie, podczas `SaveChangesAsync`, przed commitem | asynchronicznie, po commicie, w innym procesie |
| Gwarancja | handler wykona się albo cała komenda się wycofa | dostarczone co najmniej raz, z opóźnieniem |
| Zmiany | swobodne (refaktoryzacja jak każdy kod) | tylko wstecznie zgodne; łamiące = nowy typ `V2` |
| Nazwa | czas przeszły, język domeny | to samo + sufiks wersji `V1`, `V2`, ... |
| Pilnuje | APP003 (zakaz `INotification`), testy architektury (Domain bez zależności) | reguła 4 testów architektury: `Contracts` tylko z prymitywów, bez zależności od Domain |

### Kiedy które

| Potrzebujesz... | Użyj |
|---|---|
| zareagować na zmianę **w tym samym serwisie i tej samej transakcji** (unieważnić cache, przetłumaczyć na zdarzenie integracyjne) | tylko zdarzenia domenowego + `IDomainEventHandler<T>` |
| poinformować **inny serwis** (albo Worker własnego serwisu), że coś się stało | zdarzenia domenowego + translatora na zdarzenie integracyjne |
| zmienić **inny agregat** w reakcji na zmianę (np. usunąć ulubione po archiwizacji materiału) | zdarzenia integracyjnego + konsumenta w Workerze, który wysyła komendę (spójność ostateczna) |
| odpowiedzi od innego serwisu **teraz**, w trakcie żądania | wyjątkowo: wywołania synchronicznego przez ACL ([10.12](#1012-wywołania-synchroniczne-acl)) |

Zdarzenie integracyjne **zawsze** powstaje z domenowego. Nie publikuje się go z handlera komendy ani z kontrolera: to agregat
jest źródłem prawdy o tym, co się stało, a publikacja w translatorze gwarantuje, że wiadomość istnieje wtedy i tylko wtedy, gdy
zmiana agregatu została zatwierdzona.

### Jakie dane niesie zdarzenie

Zdarzenie domenowe niesie to, czego potrzebują handlery, żeby nie musiały ponownie ładować agregatu ani czytać zegara:
identyfikator agregatu, zmienione wartości i **czas zmiany wzięty z agregatu**.

```csharp
// src/Services/Knowledge/Knowledge.Domain/Materials/Events/MaterialPublished.cs
public sealed record MaterialPublished(MaterialId MaterialId, MaterialType Type, string Title, DateTimeOffset PublishedAt) : IDomainEvent;

// src/Services/Knowledge/Knowledge.Domain/Materials/Events/MaterialArchived.cs
public sealed record MaterialArchived(MaterialId MaterialId, DateTimeOffset ArchivedAt) : IDomainEvent;

// src/Services/Knowledge/Knowledge.Domain/Materials/Events/MaterialChanged.cs
public sealed record MaterialChanged(MaterialId MaterialId) : IDomainEvent;

// src/Services/SleepDiary/SleepDiary.Domain/Entries/Events/SleepEntryRecorded.cs
public sealed record SleepEntryRecorded(
    SleepEntryId EntryId,
    UserId UserId,
    DateOnly Date,
    int SleepMinutes,
    int Quality,
    DateTimeOffset RecordedAt) : IDomainEvent;
```

`MaterialChanged` celowo nie niesie szczegółów: służy wyłącznie do unieważniania cache ([11 Cache](11-cache.md)) i nigdy
nie wychodzi poza serwis.

Zdarzenie integracyjne niesie **tylko to, czego potrzebują inne konteksty**, w prymitywach:

```csharp
// src/Services/Knowledge/Knowledge.Contracts/MaterialPublishedV1.cs
namespace Knowledge.Contracts;

// Published language of the Knowledge service (ADR-0005): primitive types only; a breaking change means a new version of the type.

public sealed record MaterialPublishedV1(Guid MaterialId, string Type, string Title, DateTimeOffset PublishedAt);

// src/Services/Knowledge/Knowledge.Contracts/MaterialArchivedV1.cs
public sealed record MaterialArchivedV1(Guid MaterialId, DateTimeOffset ArchivedAt);

// src/Services/SleepDiary/SleepDiary.Contracts/SleepEntryRecordedV1.cs
public sealed record SleepEntryRecordedV1(Guid EntryId, string UserId, DateOnly Date, int SleepMinutes, int Quality, DateTimeOffset RecordedAt);
```

Enum domenowy (`MaterialType`) idzie jako nazwa (`"Article"`), silne ID jako `Guid`/`string`. Dzięki temu konsument nie
potrzebuje żadnego typu z cudzej domeny, a projekt `Contracts` nie zależy od `Domain` (reguła 4 w
`tests/SuperApp.ArchitectureTests/ArchitectureRules.cs`).

### Źle / dobrze: zawartość kontraktu

```csharp
// ŹLE: typy domenowe w kontrakcie, cały stan agregatu "na zapas"
public sealed record MaterialArchivedV1(MaterialId MaterialId, Material Material, PublicationStatus Status);

// DOBRZE: identyfikator i fakt, w prymitywach
public sealed record MaterialArchivedV1(Guid MaterialId, DateTimeOffset ArchivedAt);
```

Dlaczego: typ domenowy w kontrakcie zmusza konsumenta do referencji do cudzej domeny (łamie granicę kontekstu i test
architektury), a każda refaktoryzacja agregatu staje się zmianą łamiącą kontrakt. Nadmiar danych też jest zobowiązaniem: każde
pole, które opublikujesz, ktoś zacznie czytać i nie usuniesz go bez wersji `V2`.

---

## 10.2 Cykl życia zdarzenia domenowego krok po kroku

Weźmy archiwizację materiału: `POST /v1/materials/{materialId}/archive`.

### Krok 1: agregat zmienia stan i zgłasza zdarzenia

```csharp
// src/Services/Knowledge/Knowledge.Domain/Materials/Material.cs
public Result Archive(DateTimeOffset now)
{
    if (Status == PublicationStatus.Archived)
    {
        return Result.Success();
    }

    Status = PublicationStatus.Archived;
    Touch(now);
    Raise(new MaterialArchived(Id, now));
    return Result.Success();
}

// Records a successful change: updates the timestamp and raises MaterialChanged unless one is already pending,
// so that one unit of work yields at most one cache invalidation per material.
private void Touch(DateTimeOffset now)
{
    UpdatedAt = now;
    if (!DomainEvents.OfType<MaterialChanged>().Any())
    {
        Raise(new MaterialChanged(Id));
    }
}
```

`Raise` (w `SuperApp.Framework.Domain/Aggregates/AggregateRoot{TId}.cs`) tylko dodaje zdarzenie do prywatnej listy
`_domainEvents`. Nic się jeszcze nie dzieje. Zauważ dwie rzeczy:

- ponowna archiwizacja jest idempotentnym no-opem **bez zdarzeń**; zdarzenie zgłasza się tylko przy faktycznej zmianie,
- `Raise` jest wywoływane **po** zmianie stanu i sprawdzeniu reguł, więc nieudana operacja nie zostawia „zabłąkanego” zdarzenia.

### Krok 2: handler komendy kończy pracę, nie zapisuje

```csharp
// src/Services/Knowledge/Knowledge.Application/Features/Materials/ArchiveMaterial/ArchiveMaterialHandler.cs
internal sealed class ArchiveMaterialHandler(IMaterialRepository materials, IClock clock) : ICommandHandler<ArchiveMaterial>
{
    public async Task<Result> Handle(ArchiveMaterial command, CancellationToken cancellationToken)
    {
        if (!MaterialId.Create(command.MaterialId).TryGetValue(out var materialId, out _))
        {
            return MaterialErrors.NotFound;
        }

        var material = await materials.GetAsync(materialId, cancellationToken);
        return material is null ? MaterialErrors.NotFound : material.Archive(clock.UtcNow);
    }
}
```

### Krok 3: `TransactionBehavior` zapisuje i zatwierdza

```csharp
// src/Framework/SuperApp.Framework.Application/Behaviors/TransactionBehavior{TRequest,TResponse}.cs
internal sealed class TransactionBehavior<TRequest, TResponse>(IUnitOfWork unitOfWork)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommand<TResponse>
    where TResponse : IResultFactory<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (unitOfWork.HasActiveTransaction)
        {
            var nested = await next();
            if (!IsSuccess(nested))
            {
                unitOfWork.DiscardChanges();
                return nested;
            }

            var savedNested = await unitOfWork.SaveChangesAsync(cancellationToken);
            return savedNested.IsSuccess ? nested : TResponse.FromError(savedNested.Error);
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var response = await next();
        if (!IsSuccess(response))
        {
            return response;
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return TResponse.FromError(saved.Error);
        }

        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    private static bool IsSuccess(TResponse response) => response is Result { IsSuccess: true };
}
```

Gałąź `HasActiveTransaction` to przypadek komendy wysłanej przez konsumenta MassTransit: transakcję otworzył już outbox
konsumenta ([10.6](#106-konsumenci)), więc behavior tylko zapisuje i zostawia commit konsumentowi (ADR-0005).

### Krok 4: `WriteDbContextBase.SaveChangesAsync` – dispatch i zapis

```csharp
// src/Framework/SuperApp.Framework.Infrastructure/Persistence/WriteDbContextBase.cs
public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
{
    var aggregates = ChangeTracker.Entries<IAggregateRoot>()
        .Select(entry => entry.Entity)
        .Where(aggregate => aggregate.DomainEvents.Count > 0)
        .ToList();

    var domainEvents = aggregates.SelectMany(aggregate => aggregate.DomainEvents).ToList();
    aggregates.ForEach(aggregate => aggregate.ClearDomainEvents());

    foreach (var domainEvent in domainEvents)
    {
        await dispatcher.DispatchAsync(domainEvent, cancellationToken);
    }

    return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
}
```

Kolejność ma znaczenie:

1. **Zebranie** zdarzeń ze wszystkich agregatów śledzonych przez `ChangeTracker` (agregat musi być śledzony: załadowany przez
   repozytorium albo dodany przez `Add`).
2. **Wyczyszczenie** (`ClearDomainEvents`) **przed** dispatchem. Gdyby zdarzenia zostały na agregacie, kolejny zapis w tym samym
   scope (np. zapis outboxa konsumenta) opublikowałby je drugi raz (ADR-0027, reguła 4).
3. **Dispatch** każdego zdarzenia po kolei. Handlery działają, **zanim** cokolwiek trafi do bazy, w tej samej transakcji.
4. **Jeden** `base.SaveChangesAsync`: zmiany agregatu i wiersze outboxa dodane przez translatory w kroku 3 zapisują się razem.
   To dlatego dispatch jest przed zapisem: publikacja przez outbox MassTransit to dodanie encji `OutboxMessage` do tego samego
   `DbContext`.

**Dispatch jest jednoprzebiegowy.** Zdarzenia zgłoszone przez agregaty w trakcie działania handlerów nie są obsługiwane w tym
zapisie (zostają na agregacie). To kolejny powód, dla którego handler zdarzenia nie modyfikuje agregatów.

Synchroniczne `SaveChanges()` rzuca `NotSupportedException` („Zapis wyłącznie przez IUnitOfWork.SaveChangesAsync (ADR-0027).”),
a analizator APP003 zgłasza jego użycie już przy kompilacji: synchroniczny zapis pominąłby asynchroniczny dispatch.

### Krok 5: dispatcher wybiera handlery z bieżącego scope DI

```csharp
// src/Framework/SuperApp.Framework.Infrastructure/Events/DomainEventDispatcher.cs
internal sealed partial class DomainEventDispatcher(IServiceProvider serviceProvider, ILogger<DomainEventDispatcher> logger)
    : IDomainEventDispatcher
{
    private static readonly MethodInfo InvokeHandlersMethod =
        typeof(DomainEventDispatcher).GetMethod(nameof(InvokeHandlersAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly ConcurrentDictionary<Type, Func<IServiceProvider, IDomainEvent, CancellationToken, Task>> Invokers = new();

    public async Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        var eventName = domainEvent.GetType().Name;
        using var activity = InfrastructureTelemetry.ActivitySource.StartActivity($"domain-event {eventName}");
        LogDispatching(logger, eventName);

        var invoker = Invokers.GetOrAdd(domainEvent.GetType(), static type =>
            InvokeHandlersMethod.MakeGenericMethod(type).CreateDelegate<Func<IServiceProvider, IDomainEvent, CancellationToken, Task>>());

        await invoker(serviceProvider, domainEvent, cancellationToken);
    }

    private static async Task InvokeHandlersAsync<TEvent>(IServiceProvider provider, IDomainEvent domainEvent, CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        foreach (var handler in provider.GetServices<IDomainEventHandler<TEvent>>())
        {
            await handler.HandleAsync((TEvent)domainEvent, cancellationToken);
        }
    }

    [LoggerMessage(200, LogLevel.Debug, "Dispatching domain event {EventName}")]
    private static partial void LogDispatching(ILogger logger, string eventName);
}
```

- Dispatcher jest **scoped** (rejestruje go `AddAppPersistence`), więc `serviceProvider` to scope żądania lub wiadomości.
  Handlery dostają ten sam `WriteDbContext`, ten sam `IUnitOfWork` i ten sam `IPublishEndpoint` co komenda. Stąd ich wpisy
  outboxa trafiają do tego samego zapisu.
- Handlery są wybierane po **typie runtime** zdarzenia. Każde zdarzenie ma span `domain-event {Nazwa}` (źródło
  `SuperApp.Infrastructure`) i log Debug z `EventId` 200.
- Handlery rejestruje automatycznie `AddAppApplication` (wszystkie implementacje `IDomainEventHandler<>` w skanowanych
  assembly, jako scoped). Skanowane są Application i Infrastructure serwisu, więc translatory mogą leżeć w Application,
  a handlery unieważniające cache w Infrastructure.
- Kolejność handlerów jednego zdarzenia **nie jest gwarantowana kontraktem**; nie polegaj na niej.
- Wyjątek w handlerze przerywa zapis: `base.SaveChangesAsync` się nie wykona, transakcja zostanie wycofana, klient dostanie 500.

### Cały przepływ na jednym diagramie

```mermaid
sequenceDiagram
    participant C as MaterialsController
    participant TB as TransactionBehavior
    participant H as ArchiveMaterialHandler
    participant M as Material (agregat)
    participant UoW as WriteDbContextBase
    participant D as DomainEventDispatcher
    participant T as MaterialArchivedTranslator
    participant CI as MaterialCacheInvalidation
    participant DB as MSSQL (schemat knowledge)

    C->>TB: Send(ArchiveMaterial)
    TB->>DB: BEGIN TRANSACTION
    TB->>H: next()
    H->>M: Archive(now)
    M->>M: Raise(MaterialChanged), Raise(MaterialArchived)
    H-->>TB: Result.Success
    TB->>UoW: SaveChangesAsync()
    UoW->>M: zebranie zdarzeń + ClearDomainEvents()
    UoW->>D: DispatchAsync(MaterialChanged)
    D->>CI: HandleAsync → OnCommitted(usuń tag z cache)
    UoW->>D: DispatchAsync(MaterialArchived)
    D->>T: HandleAsync → PublishAsync(MaterialArchivedV1)
    T->>UoW: nowa encja OutboxMessage (śledzona)
    UoW->>DB: base.SaveChangesAsync(): UPDATE Materials + INSERT OutboxMessage
    TB->>DB: COMMIT
    DB-->>UoW: AfterCommitInterceptor: akcje OnCommitted (unieważnienie cache)
    TB-->>C: Result.Success → 204 No Content
```

### Czego handler zdarzenia domenowego nie robi

| Zakaz | Dlaczego |
|---|---|
| ładowanie i modyfikowanie **innego** agregatu | jedna transakcja = jeden agregat; dispatch jest jednoprzebiegowy, więc zdarzenia tego agregatu i tak by się nie obsłużyły |
| wywołanie `SaveChangesAsync` | handler działa **wewnątrz** trwającego zapisu; zagnieżdżony zapis wywołałby ponowny dispatch i zapisał stan w połowie |
| wywołania zewnętrzne (HTTP, e-mail, RabbitMQ bezpośrednio) | trzymają otwartą transakcję bazy; a jeśli transakcja się wycofa, e-maila nie da się „odwysłać” |
| praca wymagająca zatwierdzonej zmiany (unieważnienie cache) wprost w handlerze | handler działa przed commitem; użyj `IUnitOfWork.OnCommitted` ([11 Cache](11-cache.md)) |
| czytanie zegara (`IClock`) | czas zdarzenia jest w zdarzeniu, wzięty z agregatu; drugi odczyt zegara daje inny czas niż zapisany w bazie |

```csharp
// ŹLE: handler zdarzenia zmienia inny agregat i sam zapisuje
internal sealed class RemoveFavoritesOnArchive(IFavoriteRepository favorites, IUnitOfWork unitOfWork)
    : IDomainEventHandler<MaterialArchived>
{
    public async Task HandleAsync(MaterialArchived domainEvent, CancellationToken cancellationToken)
    {
        await favorites.RemoveAllForItemAsync(FavoriteItemType.Material, domainEvent.MaterialId.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

// DOBRZE: translator publikuje fakt, a zmianę innych agregatów robi konsument przez osobną komendę
internal sealed class MaterialArchivedTranslator(IIntegrationEventPublisher publisher) : IDomainEventHandler<MaterialArchived>
{
    public Task HandleAsync(MaterialArchived domainEvent, CancellationToken cancellationToken) =>
        publisher.PublishAsync(new MaterialArchivedV1(domainEvent.MaterialId.Value, domainEvent.ArchivedAt), cancellationToken);
}
```

Wersja „źle” zmienia setki agregatów `Favorite` w transakcji archiwizacji (blokady, długi czas żądania), wywołuje zapis
w środku zapisu, a porażka usuwania ulubionych cofnęłaby archiwizację, która sama w sobie była poprawna.

---

## 10.3 Translatory: zdarzenie domenowe → integracyjne

Translator to `IDomainEventHandler<T>` w `{Serwis}.Application/IntegrationEvents/`, `internal sealed`, jedna linijka logiki:

```csharp
// src/Services/Knowledge/Knowledge.Application/IntegrationEvents/MaterialPublishedTranslator.cs
internal sealed class MaterialPublishedTranslator(IIntegrationEventPublisher publisher) : IDomainEventHandler<MaterialPublished>
{
    public Task HandleAsync(MaterialPublished domainEvent, CancellationToken cancellationToken) =>
        publisher.PublishAsync(
            new MaterialPublishedV1(domainEvent.MaterialId.Value, domainEvent.Type.ToString(), domainEvent.Title, domainEvent.PublishedAt),
            cancellationToken);
}

// src/Services/Knowledge/Knowledge.Application/IntegrationEvents/MaterialArchivedTranslator.cs
internal sealed class MaterialArchivedTranslator(IIntegrationEventPublisher publisher) : IDomainEventHandler<MaterialArchived>
{
    public Task HandleAsync(MaterialArchived domainEvent, CancellationToken cancellationToken) =>
        publisher.PublishAsync(new MaterialArchivedV1(domainEvent.MaterialId.Value, domainEvent.ArchivedAt), cancellationToken);
}

// src/Services/SleepDiary/SleepDiary.Application/IntegrationEvents/SleepEntryRecordedTranslator.cs
internal sealed class SleepEntryRecordedTranslator(IIntegrationEventPublisher publisher) : IDomainEventHandler<SleepEntryRecorded>
{
    public Task HandleAsync(SleepEntryRecorded domainEvent, CancellationToken cancellationToken) =>
        publisher.PublishAsync(
            new SleepEntryRecordedV1(
                domainEvent.EntryId.Value, domainEvent.UserId.Value, domainEvent.Date, domainEvent.SleepMinutes, domainEvent.Quality, domainEvent.RecordedAt),
            cancellationToken);
}
```

`IIntegrationEventPublisher` (`SuperApp.Framework.Application.Events`) to port; Application nie wie nic o MassTransit (reguła 3
testów architektury). Implementację rejestruje `AddAppMessaging`:

```csharp
// src/Framework/SuperApp.Framework.Infrastructure/Messaging/MessagingServiceCollectionExtensions.cs
private sealed class IntegrationEventPublisher(IPublishEndpoint publishEndpoint) : IIntegrationEventPublisher
{
    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : class =>
        publishEndpoint.Publish(integrationEvent, cancellationToken);
}
```

`IPublishEndpoint` ze scope, przy włączonym bus outboxie, **nie wysyła** nic do RabbitMQ. Zapisuje wiadomość jako encję
`OutboxMessage` w `WriteDbContext`. Zadanie `PublishAsync` kończy się więc w momencie dodania wiadomości do outboxa;
wiadomość opuści serwis dopiero po commicie, a przy wycofaniu zniknie razem z resztą zmian.

### Czas zdarzenia zawsze z agregatu

```csharp
// ŹLE: translator czyta zegar
internal sealed class MaterialArchivedTranslator(IIntegrationEventPublisher publisher, IClock clock) : IDomainEventHandler<MaterialArchived>
{
    public Task HandleAsync(MaterialArchived domainEvent, CancellationToken cancellationToken) =>
        publisher.PublishAsync(new MaterialArchivedV1(domainEvent.MaterialId.Value, clock.UtcNow), cancellationToken);
}

// DOBRZE: czas niesie zdarzenie domenowe, a to ma go od agregatu
publisher.PublishAsync(new MaterialArchivedV1(domainEvent.MaterialId.Value, domainEvent.ArchivedAt), cancellationToken);
```

Dlaczego: `clock.UtcNow` w translatorze to inna chwila niż `UpdatedAt` zapisane w agregacie (handler komendy wywołał
`clock.UtcNow` wcześniej). Konsument porównujący czasy (np. „czy mam nowszą wersję”) dostałby dane, które nie zgadzają się
z bazą dostawcy. Pilnują tego testy `IntegrationEventTranslatorTests` i `SleepEntryRecordedTranslatorTests`
([10.13](#1013-testowanie-zdarzeń)).

### Nie publikuj z handlera komendy

```csharp
// ŹLE: publikacja obok agregatu
var result = material.Archive(clock.UtcNow);
await publisher.PublishAsync(new MaterialArchivedV1(material.Id.Value, clock.UtcNow), cancellationToken);
return result;
```

Dlaczego: handler opublikuje także wtedy, gdy archiwizacja była no-opem (materiał już zarchiwizowany) albo zwróciła błąd, a dwa
miejsca decydujące o tym, „co się stało”, prędzej czy później się rozjadą. Agregat wie, czy zaszła zmiana; zgłasza zdarzenie
tylko wtedy.

---

## 10.4 Konfiguracja MassTransit: `AddAppMessaging`

Serwis nie konfiguruje MassTransit samodzielnie. Robi to jedna metoda frameworku, wywoływana z composition root serwisu:

```csharp
// src/Framework/SuperApp.Framework.Infrastructure/Messaging/MessagingServiceCollectionExtensions.cs
public static IServiceCollection AddAppMessaging<TWriteDbContext>(
    this IServiceCollection services,
    IConfiguration configuration,
    string servicePrefix,
    OutboxDelivery outboxDelivery,
    Action<IBusRegistrationConfigurator>? configureConsumers = null)
    where TWriteDbContext : DbContext
{
    services.AddScoped<IIntegrationEventPublisher, IntegrationEventPublisher>();

    // The build-time OpenAPI generator starts the application without any infrastructure (ADR-0009).
    if (BuildTimeDocumentGeneration.IsActive)
    {
        return services;
    }

    services.AddMassTransit(bus =>
    {
        bus.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter(servicePrefix, includeNamespace: false));
        configureConsumers?.Invoke(bus);

        bus.AddEntityFrameworkOutbox<TWriteDbContext>(outbox =>
        {
            outbox.UseSqlServer();
            outbox.UseBusOutbox(busOutbox =>
            {
                if (outboxDelivery == OutboxDelivery.Disabled)
                {
                    busOutbox.DisableDeliveryService();
                }
            });
        });

        bus.AddConfigureEndpointsCallback((context, _, endpoint) =>
        {
            endpoint.UseMessageRetry(retry => retry.Intervals(100, 500, 1000, 5000));
            endpoint.UseEntityFrameworkOutbox<TWriteDbContext>(context);

            if (endpoint is IRabbitMqReceiveEndpointConfigurator rabbitMq)
            {
                rabbitMq.SetQuorumQueue();
            }
        });

        bus.UsingRabbitMq((context, rabbitMq) =>
        {
            var connectionString = configuration.GetConnectionString("RabbitMq");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("Brak connection stringu RabbitMq.");
            }

            rabbitMq.Host(new Uri(connectionString));
            rabbitMq.ConfigureEndpoints(context);
        });
    });

    return services;
}
```

Wywołanie w serwisie (Knowledge):

```csharp
// src/Services/Knowledge/Knowledge.Infrastructure/InfrastructureServiceCollectionExtensions.cs
public static IServiceCollection AddKnowledgeInfrastructure(
    this IServiceCollection services,
    IConfiguration configuration,
    OutboxDelivery outboxDelivery,
    Action<IBusRegistrationConfigurator>? configureConsumers = null)
{
    services.AddKnowledgeCore(configuration);
    services.AddAppMessaging<KnowledgeWriteDbContext>(configuration, KnowledgeWriteDbContext.SchemaName, outboxDelivery, configureConsumers);
    return services;
}
```

### Co daje każdy element

| Element | Efekt | Dlaczego |
|---|---|---|
| `AddEntityFrameworkOutbox<TWriteDbContext>` + `UseSqlServer()` | tabele `InboxState`, `OutboxState`, `OutboxMessage` w schemacie serwisu (dodaje je `WriteDbContextBase.OnModelCreating`, tworzą migracje serwisu) | outbox i inbox w tej samej bazie i transakcji co agregaty |
| `UseBusOutbox` | `IPublishEndpoint` ze scope zapisuje do outboxa zamiast wysyłać | atomowość: wiadomość istnieje wtedy i tylko wtedy, gdy commit się udał |
| `DisableDeliveryService()` dla `OutboxDelivery.Disabled` | API zapisuje wiadomości, ale ich nie wysyła; wysyła Worker | skalowanie API nie mnoży pracy dostarczania; czas odpowiedzi nie zależy od brokera |
| `UseMessageRetry(Intervals(100, 500, 1000, 5000))` | wyjątek w konsumencie: 4 ponowienia w procesie (łącznie 5 prób), potem kolejka `{kolejka}_error` | przejściowe błędy (deadlock, timeout) mijają same; trwałe trafiają do monitorowanej kolejki |
| `UseEntityFrameworkOutbox<TWriteDbContext>(context)` na endpointach | inbox (deduplikacja po `MessageId` + konsument) i outbox konsumenta w jednej transakcji z komendą | konsument przetwarza wiadomość raz, a jego własne publikacje są atomowe z jego zmianą |
| `SetQuorumQueue()` | kolejki typu quorum | replikacja kolejek w klastrze RabbitMQ (ADR-0005) |
| `KebabCaseEndpointNameFormatter(servicePrefix, includeNamespace: false)` | kolejka konsumenta `MaterialArchivedConsumer` w Knowledge to `knowledge-material-archived` | kolejki serwisów nie kolidują we wspólnym brokerze |
| `BuildTimeDocumentGeneration.IsActive` | przy generowaniu `openapi/*.json` w buildzie rejestrowany jest tylko `IIntegrationEventPublisher`, bez MassTransit | build nie potrzebuje brokera ani sekretów (ADR-0009) |

Connection string `ConnectionStrings:RabbitMq` (`amqps://user:password@host/vhost`) pochodzi z Vault; lokalnie compose ustawia
`amqp://guest:guest@rabbitmq:5672/`, a `appsettings.Development.json` (uruchomienie z IDE) `amqp://guest:guest@localhost:5672/`.
Brak wartości (także pusty `"RabbitMq": ""` z `appsettings.json`) kończy start procesu wyjątkiem `InvalidOperationException`
„Brak connection stringu RabbitMq.”.

MassTransit jest w wersji **8.5.11** (Apache-2.0, ADR-0035). Nie podnoś do 9.x: to powrót do licencji komercyjnej i wymaga ADR.

### API i Worker: kto dostarcza outbox

```csharp
// src/Services/Knowledge/Knowledge.Api/Program.cs
builder.Services.AddKnowledgeInfrastructure(builder.Configuration, OutboxDelivery.Disabled);

// src/Services/Knowledge/Knowledge.Worker/Program.cs
builder.AddAppWorker();
builder.Services.AddKnowledgeInfrastructure(
    builder.Configuration,
    OutboxDelivery.Enabled,
    bus => bus.AddConsumers(typeof(Program).Assembly));
```

```csharp
// src/Framework/SuperApp.Framework.Infrastructure/Messaging/OutboxDelivery.cs
public enum OutboxDelivery
{
    Disabled,
    Enabled,
}
```

Oba procesy **zapisują** do outboxa (API z komend HTTP, Worker z komend wysyłanych przez konsumentów). Tylko Worker uruchamia
usługę dostarczania, która cyklicznie odpytuje `OutboxState`/`OutboxMessage` i wysyła zatwierdzone wiadomości do RabbitMQ.
Wniosek praktyczny: **jeśli Worker nie działa, zdarzenia z API czekają w tabeli `OutboxMessage`**. Nic nie ginie; po starcie
Workera zostaną wysłane.

### Telemetria i zdrowie

- `AddAppServiceDefaults` dodaje źródło `MassTransit` do tracingu i metryk. Nagłówek `traceparent` jest zapisywany w outboxie
  i przekazywany w wiadomości, więc w Tempo zobaczysz jeden ślad: żądanie HTTP → zapis → (z opóźnieniem) konsumpcja w Workerze.
- Health checki MassTransit (tag `masstransit`) są w `/health/dependencies`, **nie** w sondach startup/readiness (ADR-0018):
  niedostępny broker nie wyłącza podów API, bo API i tak tylko pisze do bazy.

---

## 10.5 Droga wiadomości: outbox → RabbitMQ → konsument

```mermaid
flowchart LR
    subgraph API["knowledge-api (OutboxDelivery.Disabled)"]
        CMD["Komenda ArchiveMaterial<br/>+ MaterialArchivedTranslator"]
    end
    subgraph DB["MSSQL, schemat knowledge"]
        MAT[("Materials")]
        OM[("OutboxMessage<br/>OutboxState")]
        IN[("InboxState")]
        FAV[("Favorites")]
    end
    subgraph W["knowledge-worker (OutboxDelivery.Enabled)"]
        DS["Usługa dostarczania outboxa"]
        CON["MaterialArchivedConsumer<br/>→ RemoveFavoritesOfItem"]
    end
    subgraph MQ["RabbitMQ"]
        EX{{"exchange<br/>Knowledge.Contracts:MaterialArchivedV1"}}
        Q[["kolejka quorum<br/>knowledge-material-archived"]]
        ERR[["knowledge-material-archived_error"]]
    end

    CMD -- "1. jedna transakcja" --> MAT
    CMD -- "1. jedna transakcja" --> OM
    DS -- "2. odczyt po commicie" --> OM
    DS -- "3. publish" --> EX
    EX --> Q
    Q -- "4. dostarczenie" --> CON
    CON -- "5. jedna transakcja:<br/>DELETE + inbox" --> FAV
    CON --> IN
    CON -. "wyjątek po 5 próbach" .-> ERR
```

1. Komenda w API zapisuje `UPDATE Materials` i `INSERT OutboxMessage` w jednej transakcji.
2. Usługa dostarczania w Workerze znajduje zatwierdzone wiadomości.
3. Publikuje je do exchange nazwanego od typu wiadomości (`Knowledge.Contracts:MaterialArchivedV1`); MassTransit tworzy
   topologię sam przy starcie Workera.
4. Exchange przekazuje kopię do każdej kolejki subskrybenta. Każdy serwis, który konsumuje to zdarzenie, ma **własną** kolejkę
   (`knowledge-material-archived`, w przyszłości np. `sleepdiary-material-archived`), więc każdy dostaje swoją kopię.
5. Konsument wykonuje komendę; inbox, zmiany komendy i ewentualne nowe wiadomości outboxa zatwierdzają się razem.

Nazwa typu wiadomości to **przestrzeń nazw + nazwa typu** (`Knowledge.Contracts:MaterialArchivedV1`). Zmiana namespace albo
nazwy rekordu to zmiana łamiąca, nawet jeśli pola się nie zmieniły: stare kolejki przestaną dostawać wiadomości.

---

## 10.6 Konsumenci

Konsumenci leżą w `{Serwis}.Worker/Consumers/` i są rejestrowani automatycznie (`bus.AddConsumers(typeof(Program).Assembly)`).
Jedyny wzorzec, jaki stosujemy: **wiadomość → komenda**.

```csharp
// src/Services/Knowledge/Knowledge.Worker/Consumers/MaterialArchivedConsumer.cs
public sealed partial class MaterialArchivedConsumer(ISender sender, ILogger<MaterialArchivedConsumer> logger) : IConsumer<MaterialArchivedV1>
{
    public async Task Consume(ConsumeContext<MaterialArchivedV1> context)
    {
        var result = await sender.Send(new RemoveFavoritesOfItem(FavoriteItemType.Material, context.Message.MaterialId), context.CancellationToken);
        if (result.IsFailure)
        {
            LogRejected(logger, context.Message.MaterialId, result.Error.Code);
        }
    }

    // Source-generated log method (the only allowed way of logging, see architecture rules §11).
    [LoggerMessage(2001, LogLevel.Warning, "Removing favorites of archived material {MaterialId} rejected: {ErrorCode}")]
    private static partial void LogRejected(ILogger logger, Guid materialId, string errorCode);
}
```

Komenda, którą wysyła, jest zwykłą komendą Application:

```csharp
// src/Services/Knowledge/Knowledge.Application/Features/Library/RemoveFavoritesOfItem/RemoveFavoritesOfItem.cs
[RequiresScope(KnowledgeScopes.CatalogWrite)]
public sealed record RemoveFavoritesOfItem(FavoriteItemType ItemType, Guid ItemId) : ICommand;

// src/Services/Knowledge/Knowledge.Application/Features/Library/RemoveFavoritesOfItem/RemoveFavoritesOfItemHandler.cs
internal sealed class RemoveFavoritesOfItemHandler(IFavoriteRepository favorites) : ICommandHandler<RemoveFavoritesOfItem>
{
    public async Task<Result> Handle(RemoveFavoritesOfItem command, CancellationToken cancellationToken)
    {
        await favorites.RemoveAllForItemAsync(command.ItemType, command.ItemId, cancellationToken);
        return Result.Success();
    }
}
```

`RemoveAllForItemAsync` to `ExecuteDeleteAsync` (jedno `DELETE` w bazie, bez śledzenia zmian i bez zdarzeń domenowych),
wykonane w transakcji otwartej przez outbox konsumenta. Usunięcie wielu agregatów `Favorite` naraz to świadomy wyjątek od
zasady „jedna transakcja – jeden agregat” (ADR-0028): nie łamie żadnego niezmiennika żadnego z nich.

### Co się dzieje w czasie obsługi wiadomości

```mermaid
sequenceDiagram
    participant MQ as RabbitMQ
    participant R as Retry (100/500/1000/5000 ms)
    participant OB as EF outbox/inbox konsumenta
    participant C as MaterialArchivedConsumer
    participant P as Pipeline MediatR
    participant DB as MSSQL

    MQ->>R: MaterialArchivedV1 (MessageId)
    R->>OB: próba 1
    OB->>DB: BEGIN TRANSACTION, InboxState(MessageId, ConsumerId)
    alt wiadomość już przetworzona
        OB-->>MQ: ACK bez wywołania konsumenta
    else nowa wiadomość
        OB->>C: Consume
        C->>P: Send(RemoveFavoritesOfItem)
        P->>P: Logging → Authorization (SystemCurrentUser) → Validation
        P->>DB: TransactionBehavior: HasActiveTransaction = true, tylko SaveChangesAsync
        C-->>OB: koniec
        OB->>DB: SaveChanges + COMMIT (zmiany + inbox + outbox konsumenta)
        OB-->>MQ: ACK
    end
    Note over R,MQ: wyjątek: ponowienie; po 5 nieudanych próbach wiadomość trafia do knowledge-material-archived_error
```

### Tożsamość: system, wszystkie scope

`AddAppWorker` rejestruje `SystemCurrentUser`:

```csharp
// src/Framework/SuperApp.Framework.Infrastructure/Security/SystemCurrentUser.cs
internal sealed class SystemCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => true;

    public string? Subject => null;

    public bool HasScope(string scope) => true;
}
```

Autoryzacja zdarzeń integracyjnych odbywa się na granicy (publikować do brokera mogą tylko zaufane serwisy), więc
`[RequiresScope]` komendy nie blokuje konsumenta. Konsekwencja: **komenda wymagająca użytkownika** (`currentUser.RequireUserId()`,
np. `RecordSleepEntry`) w konsumencie zwróci błąd, bo `Subject` jest `null`. Jeśli wiadomość dotyczy konkretnego użytkownika,
przekaż jego identyfikator w komendzie jawnie (tak jak `SleepEntryRecordedV1` niesie `UserId`).

### Błąd biznesowy a wyjątek techniczny

| Sytuacja | Co robi konsument | Efekt |
|---|---|---|
| komenda zwraca `Result` z błędem (walidacja, reguła biznesowa, `NotFound`) | loguje ostrzeżenie `[LoggerMessage]` z kodem błędu, **kończy normalnie** | wiadomość potwierdzona; ponowienie nic by nie zmieniło |
| wyjątek (baza niedostępna, timeout, deadlock) | **nie łapie** go | retry 100 ms → 500 ms → 1 s → 5 s; potem `_error` (alert) |

```csharp
// ŹLE: połknięty wyjątek techniczny i logika biznesowa w konsumencie
public async Task Consume(ConsumeContext<MaterialArchivedV1> context)
{
    try
    {
        var favorites = await db.Favorites.Where(f => f.ItemId == context.Message.MaterialId).ToListAsync();
        if (favorites.Count > 0 && context.Message.ArchivedAt < DateTimeOffset.UtcNow.AddDays(-30)) { /* ... */ }
        db.RemoveRange(favorites);
        await db.SaveChangesAsync();
    }
    catch (Exception)
    {
        // "żeby kolejka się nie zapychała"
    }
}

// DOBRZE: cienki konsument; wyjątki lecą do retry; reguły w agregacie/komendzie
var result = await sender.Send(new RemoveFavoritesOfItem(FavoriteItemType.Material, context.Message.MaterialId), context.CancellationToken);
if (result.IsFailure)
{
    LogRejected(logger, context.Message.MaterialId, result.Error.Code);
}
```

Wersja „źle”: przy chwilowej awarii bazy wiadomość jest potwierdzona i **utracona na zawsze** (ulubione nigdy nie zostaną
usunięte), konsument omija pipeline (autoryzacja, walidacja, transakcja, telemetria), a reguła biznesowa ląduje poza agregatem.
Do tego Worker referuje Infrastructure tylko jako composition root; użycie `DbContext` w konsumencie łamie regułę 6 testów
architektury.

> **Uwaga na nieudany wynik w konsumencie.** W API nieudany `Result` oznacza wycofanie transakcji (behavior jej nie zatwierdza).
> W konsumencie transakcję zatwierdza outbox MassTransit **zawsze**, gdy konsument nie rzucił wyjątku, a przed commitem wywołuje
> `SaveChangesAsync` kontekstu. Zmiany śledzone przez komendę, która potem zwróciła błąd, zostałyby więc zapisane. Dlatego
> metody agregatów sprawdzają reguły **przed** zmianą stanu, a handler komendy nie dodaje niczego do repozytorium, zanim nie ma
> pewności sukcesu (wzór: `RecordSleepEntryHandler` wywołuje `entries.Add(entry)` dopiero po udanym `SleepEntry.Record`).

`EventId` logów Workera bierz z zakresu serwisu w [rejestrze](../logowanie-eventid.md) (Knowledge Worker: 2000–2999).

---

## 10.7 Co najmniej raz: idempotencja

Outbox gwarantuje, że wiadomość **nie zginie** i **nie pojawi się bez zmiany**. Nie gwarantuje, że przyjdzie dokładnie raz.
Duplikat powstaje np. gdy Worker wysłał wiadomość do RabbitMQ, ale padł przed oznaczeniem jej jako dostarczonej; albo gdy
konsument zatwierdził transakcję, ale połączenie zerwało się przed ACK.

Warstwy obrony:

1. **Inbox** (`InboxState`, klucz `MessageId` + `ConsumerId`): ta sama wiadomość (ten sam `MessageId`) nie zostanie
   przetworzona drugi raz przez tego samego konsumenta. Okno deduplikacji jest ograniczone czasem przechowywania wpisów
   inboxa (ustawienie domyślne MassTransit), więc to zabezpieczenie przed typowymi powtórzeniami, nie dowód.
2. **Idempotentna komenda**: jej efekt przy drugim wykonaniu jest taki sam jak przy pierwszym. To wymóg, nie opcja.

| Wzorzec | Przykład w repozytorium |
|---|---|
| operacja zbiorcza „doprowadź do stanu” | `RemoveFavoritesOfItem`: usunięcie ulubionych elementu, który ich nie ma, to no-op |
| przejście stanu jako no-op przy powtórzeniu | `Material.Archive` i `Material.Publish`: ponowne wywołanie zwraca sukces bez zmian i bez zdarzeń |
| unikalny indeks + mapowanie konfliktu | `IX_Favorites_UserId_ItemType_ItemId` → `LibraryErrors.FavoriteAddedConcurrently` (ADR-0015); drugi zapis tego samego zwraca `Conflict`, który konsument loguje i potwierdza |
| porównanie wersji / czasu | przy kopiach danych z innego serwisu zapisuj czas zdarzenia (`ArchivedAt`, `RecordedAt`) i ignoruj starsze; dlatego czas musi pochodzić z agregatu |

```csharp
// ŹLE: komenda nieidempotentna (każde wykonanie dolicza)
reader.IncrementArchivedMaterialsCount();

// DOBRZE: stan wynikający z faktu, ten sam przy powtórzeniu
reader.MarkMaterialArchived(materialId, archivedAt);   // drugi raz: już oznaczony, no-op
```

**Kolejność nie jest gwarantowana.** Retry, wielu konsumentów i redelivery mogą zamienić kolejność wiadomości. Konsument
`MaterialArchivedV1` może dostać zdarzenie o materiale, o którego publikacji (`MaterialPublishedV1`) nigdy nie słyszał
(dokumentacja `MaterialArchivedV1` mówi to wprost: materiał zarchiwizowany jako szkic nigdy nie był ogłoszony). Projektuj
konsumentów tak, żeby to tolerowali.

**Spójność ostateczna w UX.** Do czasu przetworzenia zdarzenia dane są „po staremu”. Knowledge rozwiązuje to w odczycie:
lista ulubionych już ukrywa zarchiwizowane elementy, zanim konsument je usunie (opis endpointu archiwizacji w
`openapi/Knowledge.Api.json`).

---

## 10.8 Ewolucja kontraktu: od `V1` do `V2`

Kontrakt to publiczne API serwisu. Konsumenci mogą być wdrażani w innym rytmie niż dostawca, a w kolejce mogą czekać
wiadomości w starym kształcie.

| Zmiana | Zgodna wstecznie? | Jak |
|---|---|---|
| nowe pole opcjonalne na końcu (`string? Summary = null`) | tak | dodaj w `V1`; starzy konsumenci je zignorują, nowi muszą obsłużyć `null` (stare wiadomości w kolejce) |
| usunięcie pola, zmiana nazwy, zmiana typu, zmiana znaczenia | **nie** | nowy typ `V2` |
| zmiana nazwy rekordu lub namespace | **nie** | to zmiana typu wiadomości w RabbitMQ (exchange), patrz 10.5 |
| pole wymagane bez wartości domyślnej | **nie** | stare wiadomości go nie mają; `V2` albo pole opcjonalne |

Procedura zmiany łamiącej (expand/contract dla wiadomości):

1. Dodaj `MaterialArchivedV2` w `Knowledge.Contracts`; podbij `<Version>` w `Knowledge.Contracts.csproj`.
2. Translator publikuje **oba** typy w okresie przejściowym:

   ```csharp
   public async Task HandleAsync(MaterialArchived domainEvent, CancellationToken cancellationToken)
   {
       await publisher.PublishAsync(new MaterialArchivedV1(domainEvent.MaterialId.Value, domainEvent.ArchivedAt), cancellationToken);
       await publisher.PublishAsync(new MaterialArchivedV2(/* ... */), cancellationToken);
   }
   ```

   Obie wiadomości trafiają do outboxa w tej samej transakcji.
3. Konsumenci przechodzą na `V2` (nowy konsument; stary usuwany w tym samym wdrożeniu konsumenta).
4. Gdy żaden konsument nie słucha `V1` i kolejki są puste, usuń publikację `V1`, a potem typ.

Dokumentacja XML typu w `Contracts` jest częścią kontraktu: opisz, **kiedy** zdarzenie jest publikowane, jaka jest gwarancja
dostarczenia i znaczenie każdego pola (wzór: `MaterialArchivedV1.cs`).

---

## 10.9 Konsumowanie zdarzeń innego serwisu

Dziś Knowledge Worker konsumuje zdarzenia **własnego** serwisu, a zdarzenia Knowledge i SleepDiary konsumuje jeszcze tylko
forwarder analityki `SuperApp.AnalyticsForwarder` (ADR-0036); SleepDiary.Worker nie ma konsumentów (tylko `Consumers/README.md`).
Pierwsze konsumowanie zdarzenia cudzego serwisu przez serwis domenowy wygląda tak:

1. Referencja z `{Konsument}.Worker` do `{Dostawca}.Contracts` (w monorepo `ProjectReference`; `Knowledge.Contracts.csproj`
   ma `IsPackable` i `Version`, docelowo pakiet NuGet).
2. **Testy architektury:** reguła 1 (`Service_does_not_depend_on_other_services`) dopuszcza zależność od `{Inny}.Contracts`
   (published language), a zabrania zależności od pozostałych warstw innego serwisu (ADR-0036). Pierwszym subskrybentem zdarzeń
   wielu serwisów jest forwarder analityki ([21.5](21-analityka-i-feature-flags.md#215-zdarzenia-z-backendu-superappanalyticsforwarder)).
3. Konsument w `{Konsument}.Worker/Consumers/`, cienki, obcy typ zostaje w konsumencie, komenda mówi językiem konsumenta:

   ```csharp
   // SleepDiary.Worker/Consumers/MaterialPublishedConsumer.cs (wzorzec)
   public sealed partial class MaterialPublishedConsumer(ISender sender, ILogger<MaterialPublishedConsumer> logger)
       : IConsumer<Knowledge.Contracts.MaterialPublishedV1>
   {
       public async Task Consume(ConsumeContext<Knowledge.Contracts.MaterialPublishedV1> context)
       {
           var message = context.Message;
           var result = await sender.Send(new RecommendReading(message.MaterialId, message.Title, message.PublishedAt), context.CancellationToken);
           if (result.IsFailure)
           {
               LogRejected(logger, message.MaterialId, result.Error.Code);
           }
       }

       [LoggerMessage(5001, LogLevel.Warning, "Recommending material {MaterialId} rejected: {ErrorCode}")]
       private static partial void LogRejected(ILogger logger, Guid materialId, string errorCode);
   }
   ```

   (`RecommendReading` jest przykładem; takiej komendy nie ma.) Kolejka powstanie sama: `sleepdiary-material-published`.
4. Jeśli konsument potrzebuje danych dostawcy później, utrzymuje **własną kopię** w swoim schemacie (read model zasilany
   zdarzeniami), a nie odpytuje dostawcy przy każdym żądaniu i nigdy nie czyta cudzego schematu (ADR-0021).

---

## 10.10 Sagi

ADR-0005 przewiduje sagi (state machine MassTransit z persystencją w MSSQL) dla procesów wieloetapowych między kontekstami.
**W repozytorium nie ma jeszcze żadnej sagi** i nie ma dla nich konfiguracji w `AddAppMessaging` (parametr `configureConsumers`
pozwala je zarejestrować w Workerze). Pierwsza saga wymaga: tabel stanu w schemacie serwisu (migracja `WriteDbContext`), decyzji
o repozytorium stanu i testów; przedstaw plan przed implementacją. Do tego czasu procesy wieloetapowe projektuj jako łańcuch
„zdarzenie → konsument → komenda → zdarzenie”.

---

## 10.11 Podgląd wiadomości lokalnie

Uruchom całość: `docker compose -f deploy/local/docker-compose.yml --profile app up -d --build` (szczegóły:
[13 Lokalne środowisko](13-lokalne-srodowisko-i-debugowanie.md)).

### Wywołanie, które publikuje zdarzenie

```bash
TOKEN=$(curl -s http://localhost:8081/realms/superapp/protocol/openid-connect/token \
  -d grant_type=password -d client_id=dev-cli -d username=editor -d password=editor \
  -d "scope=openid knowledge.catalog.read knowledge.catalog.write" | jq -r .access_token)

curl -s -i -X POST http://localhost:5101/v1/materials/3f2c8a51-0d7e-4c0b-9a57-6c1f1f7b2e10/archive \
  -H "Authorization: Bearer $TOKEN"
# HTTP/1.1 204 No Content
```

Wywołanie bezpośrednio na porcie 5101 służy tylko do debugowania. Moduł (przeglądarka, Angular) wysyła to samo żądanie przez
bramę: `POST https://localhost:5001/api/example/v1/knowledge/materials/{id}/archive` z ciasteczkiem sesji i nagłówkiem
`X-CSRF: 1`; brama usuwa prefiks `/api/example` i dokłada token, a BFF experience woła Knowledge pod `/v1/materials/{id}/archive`
z tym samym tokenem i przekazuje odpowiedź bez zmian.

Błąd nie publikuje niczego (transakcja wycofana albo agregat nic nie zgłosił):

```http
HTTP/1.1 404 Not Found
Content-Type: application/problem+json

{"title":"Materiał nie istnieje.","status":404,"instance":"/v1/materials/3f2c8a51-0d7e-4c0b-9a57-6c1f1f7b2e10/archive",
 "code":"knowledge.material.not_found","traceId":"4bf92f3577b34da6a3ce929d0e0e4736"}
```

### Outbox w bazie

```sql
-- sqlcmd -S localhost,1433 -U sa -P "Dev!Passw0rd1" -d SuperApp -C
-- wiadomości czekające na wysłanie (pusto = Worker już wysłał)
SELECT TOP 20 SequenceNumber, MessageType, SentTime, OutboxId, InboxMessageId, Body
FROM knowledge.OutboxMessage ORDER BY SequenceNumber DESC;

SELECT OutboxId, Created, Delivered FROM knowledge.OutboxState ORDER BY Created DESC;

-- wiadomości przyjęte przez konsumentów Workera
SELECT TOP 20 MessageId, ConsumerId, Received, ReceiveCount, Consumed, Delivered
FROM knowledge.InboxState ORDER BY Received DESC;
```

`MessageType` zawiera `urn:message:Knowledge.Contracts:MaterialArchivedV1`, `Body` to koperta JSON MassTransit z polem
`message` (`{"materialId":"...","archivedAt":"..."}`). Wiersze rosnące w `OutboxMessage` przy działającym API to prawie zawsze
**niedziałający Worker** albo Worker bez połączenia z RabbitMQ (sprawdź jego logi i `/health/dependencies`).

### Panel RabbitMQ

http://localhost:15672 (guest / guest):

- **Exchanges:** `Knowledge.Contracts:MaterialArchivedV1`, `Knowledge.Contracts:MaterialPublishedV1`,
  `Knowledge.Contracts:CollectionArchivedV1`, `SleepDiary.Contracts:SleepEntryRecordedV1`.
- **Queues:** `knowledge-material-archived`, `knowledge-collection-archived`, kolejki forwardera analityki
  `analytics-material-published`, `analytics-material-archived`, `analytics-collection-archived`, `analytics-sleep-entry-recorded`
  (wszystkie typu `quorum`) oraz, po pierwszym trwałym błędzie, np. `knowledge-material-archived_error`. W `_error` wiadomość ma nagłówki `MT-Fault-Message`, `MT-Fault-ExceptionType`
  i `MT-Fault-StackTrace`; przycisk *Get messages* pokazuje je bez zdejmowania wiadomości z kolejki (*Ack mode: Nack, requeue*).
- Zdarzenie bez żadnego subskrybenta ma exchange, ale żadnej kolejki: wiadomość jest po prostu odrzucana przez brokera. To
  poprawne. Dziś każde zdarzenie ma co najmniej kolejkę forwardera analityki (uruchomionego w profilu `app`).

Ponowne przetworzenie wiadomości z `_error` po naprawie: *Shovel* w panelu albo przeniesienie przez *Get messages* i ponowną
publikację. Na środowiskach dev/test/prod robi to zespół po analizie, nie automat.

---

## 10.12 Wywołania synchroniczne (ACL)

Domyślnie konteksty rozmawiają zdarzeniami. Wywołanie HTTP innego serwisu jest wyjątkiem, gdy odpowiedź jest potrzebna
natychmiast i nie da się jej utrzymać jako lokalnej kopii. W repozytorium takie wywołania wykonuje dziś tylko **BFF experience**
(`Example.Bff` → Knowledge i SleepDiary); wywołań serwis → serwis jeszcze nie ma. Pakiety (`Refit`, `Refit.HttpClientFactory`,
`Refit.Reflection`, `Microsoft.Extensions.Http.Resilience`) są w `Directory.Packages.props`, a narzędzie `refitter` w
`.config/dotnet-tools.json`.

**Kogo wolno wołać (ADR-0038–ADR-0041):**

- serwis domenowy **własnej experience**: od BFF tej experience albo od innego jej serwisu;
- **inną experience** wyłącznie przez API wewnętrzne jej BFF (`/internal/v{n}`), nigdy bezpośrednio jej serwisy domenowe; gwarancją
  jest NetworkPolicy ([9.5](09-bezpieczenstwo.md#networkpolicy-ruch-do-domeny-tylko-przez-bff-experience));
- **system zewnętrzny** (np. dostawcę) wyłącznie przez ACL, własnymi poświadczeniami; token użytkownika nie opuszcza granicy zaufania.

**Jakim tokenem** ([9.8](09-bezpieczenstwo.md#98-wywołania-synchroniczne-token-użytkownika-albo-client-credentials), ADR-0040):

- wywołanie **w kontekście użytkownika** (domyślne) przekazuje token użytkownika bez zmian; odbiorca waliduje JWT, scope i reguły
  zasobu i nie ufa identyfikatorowi użytkownika z payloadu. Mechanizm jest gotowy: `AddUserTokenForwarding()`
  (`SuperApp.Framework.Infrastructure/Http/UserContext`);
- wywołanie **systemowe** (wyjątek: dane ogólne, zbiorcze, procesy w tle, np. z konsumenta w Workerze, który nie ma użytkownika)
  używa tokenu client credentials. Ten mechanizm jest gotowy: `AddClientCredentialsToken` (token z cache,
  `SuperApp.Framework.Infrastructure/Http/ClientCredentials`), a w lokalnym Keycloaku istnieją klienci `knowledge-client`
  i `sleepdiary-client`.

Rejestracja klienta jest taka sama w BFF i w serwisie (`SuperApp.Framework.Infrastructure/Http/Downstream`):

```csharp
builder.Services.AddDownstreamApi<IKnowledgeApi>(builder.Configuration, "Knowledge").AddUserTokenForwarding();
```

`AddDownstreamApi` daje klienta Refit przez `IHttpClientFactory`, adres z `Downstream:{nazwa}:BaseAddress`, standardową odporność
(ponowienia tylko metod bezpiecznych (GET, HEAD, OPTIONS), timeouty, circuit breaker), JSON i daty zgodne z kontraktami oraz obsługę niedostępności:
nieosiągalny albo zbyt wolny odbiorca kończy się odpowiedzią `503 downstream.unavailable` / `504 downstream.timeout`
(`DownstreamUnavailableExceptionHandler`, log 220), a nie `500`.

W BFF wygenerowany klient jest używany wprost w kontrolerze (BFF nie ma domeny). W **serwisie** obowiązuje dodatkowo wzorzec ACL
z ADR-0014:

- port w Application konsumenta (`I{Obcy}Gateway`), mówiący językiem konsumenta i zwracający `Result`,
- implementacja w `Infrastructure/Integrations/{Dostawca}/`, wywołująca interfejs Refit wygenerowany przez Refitter
  z commitowanego `openapi/*.json` dostawcy; wygenerowane typy są `internal` i nie wychodzą poza Infrastructure,
- klient przez `AddDownstreamApi` z `AddUserTokenForwarding()` albo `AddClientCredentialsToken(...)`,
- cache odpowiedzi przez `FailSafeCache` tam, gdzie dane rzadko się zmieniają ([11 Cache](11-cache.md)).

Wywołanie synchroniczne nie zastępuje procesu między domenami: jeśli zmiana ma objąć kilka domen, użyj zdarzeń albo sagi (10.10),
a nie łańcucha wywołań HTTP, w którym token użytkownika może wygasnąć.

Kompletny przepis krok po kroku: [przepis 08](przepisy/08-wywolanie-innego-serwisu.md).

---

## 10.13 Testowanie zdarzeń

| Poziom | Co sprawdzasz | Jak | Przykład |
|---|---|---|---|
| Domain.Tests | agregat zgłasza właściwe zdarzenie z właściwymi danymi (i nie zgłasza przy no-opie) | `material.DomainEvents.OfType<...>()` | `MaterialTests.Published_event_carries_the_publication_time_of_the_aggregate` |
| Application.Tests | translator mapuje zdarzenie na kontrakt, czas z agregatu | `FakeIntegrationEventPublisher` | `IntegrationEventTranslatorTests`, `SleepEntryRecordedTranslatorTests` |
| IntegrationTests | pełny przepływ komenda → zapis → dispatch → publikacja; komenda konsumenta | `ServiceFixture` z `TestIntegrationEventPublisher` (bez MassTransit), MSSQL w Testcontainers | `KnowledgeFlowTests.Library_tracks_favorites_and_completions_and_archiving_cleans_favorites` |
| ręcznie, docker compose | przejście przez RabbitMQ, Worker, kolejki | 10.11 | — |

Test translatora (Application.Tests, bez bazy):

```csharp
// src/Services/Knowledge/tests/Knowledge.Application.Tests/IntegrationEventTranslatorTests.cs
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

Testy integracyjne budują kontener DI przez `AddKnowledgeCore` (wszystko poza MassTransit) i rejestrują
`TestIntegrationEventPublisher`, który zbiera opublikowane wiadomości:

```csharp
// src/Services/Knowledge/tests/Knowledge.IntegrationTests/Infrastructure/ServiceFixture.cs (fragment)
services.AddSingleton<IIntegrationEventPublisher>(Publisher);
services.AddKnowledgeCore(configuration);
```

```csharp
// src/Services/Knowledge/tests/Knowledge.IntegrationTests/KnowledgeFlowTests.cs (fragment)
Act(KnowledgeScopes.CatalogWrite);
Assert.True((await fixture.SendAsync(new ArchiveMaterial(materialId))).IsSuccess);
Assert.Contains(fixture.Publisher.Published, message => message is MaterialArchivedV1 archived && archived.MaterialId == materialId);

// The MaterialArchivedV1 consumer in the Worker sends this command (ADR-0028).
Assert.True((await fixture.SendAsync(new RemoveFavoritesOfItem(FavoriteItemType.Material, materialId))).IsSuccess);
```

Ponieważ konsument jest cienki, testuje się **komendę**, którą wysyła; sam konsument nie ma osobnych testów, a przejście przez
RabbitMQ sprawdza się ręcznie w docker compose. Fake publikatora zbiera wiadomości również z transakcji wycofanych (nie ma
outboxa), więc testem integracyjnym nie udowodnisz atomowości outboxa; tę gwarantuje MassTransit i konfiguracja
`AddAppMessaging`.

```bash
dotnet test --project src/Services/Knowledge/tests/Knowledge.Application.Tests
dotnet test --project src/Services/Knowledge/tests/Knowledge.IntegrationTests   # wymaga Dockera
```

Więcej o fixture'ach: [12 Testy](12-testy.md), [przepis 09](przepisy/09-testy.md).

---

## Typowe błędy

| Objaw | Przyczyna | Naprawa |
|---|---|---|
| Zdarzenie integracyjne nie dociera do konsumenta; w `knowledge.OutboxMessage` przybywa wierszy | Worker nie działa albo nie łączy się z RabbitMQ (API ma `OutboxDelivery.Disabled`) | uruchom Worker; sprawdź `ConnectionStrings:RabbitMq`, logi Workera i `/health/dependencies` |
| `OutboxMessage` pusta, mimo że komenda się udała | agregat nie zgłosił zdarzenia (no-op, np. ponowna archiwizacja) albo brak translatora | test domeny na `DomainEvents`; sprawdź, czy translator implementuje `IDomainEventHandler<WłaściweZdarzenie>` i leży w skanowanym assembly |
| Handler zdarzenia domenowego się nie wywołuje | agregat nie jest śledzony (utworzony bez `repository.Add`) albo zapis poszedł z pominięciem `IUnitOfWork` | dodaj agregat przez repozytorium; nie zapisuj ręcznie |
| `NotSupportedException: Zapis wyłącznie przez IUnitOfWork.SaveChangesAsync (ADR-0027).` | synchroniczne `SaveChanges()` | usuń; zapis robi `TransactionBehavior` |
| `NotSupportedException: Zdarzenia domenowe nie są obsługiwane w czasie projektowania.` | zapis agregatu w `dotnet ef` / `SuperApp.Migrator` (`DesignTimeDomainEventDispatcher`) | narzędzia migracji nie zapisują agregatów; dane początkowe przez `HasData` w migracji |
| Konsument dostaje wiadomość drugi raz i dubluje efekt | komenda nieidempotentna | zrób komendę „doprowadź do stanu” albo oprzyj ją na unikalnym indeksie (10.7) |
| Wiadomości lądują w `..._error` | wyjątek techniczny po 5 próbach albo wyjątek z błędu programistycznego | nagłówki `MT-Fault-*` w panelu RabbitMQ, ślad po `traceId` w Grafanie; po naprawie przenieś wiadomości z powrotem |
| Konsument „przepuszcza” błąd, nic nie jest ponawiane | wyjątek złapany w konsumencie | nie łap wyjątków technicznych; błąd biznesowy tylko loguj |
| Komenda z konsumenta zwraca błąd „brak użytkownika” | `SystemCurrentUser.Subject` jest `null` | przekaż identyfikator użytkownika w komendzie (z wiadomości) |
| Po zmianie nazwy/namespace kontraktu konsumenci milkną | zmienił się typ wiadomości (exchange) | traktuj jak zmianę łamiącą: nowy typ `V2`, okres publikacji obu |
| Test architektury `Service_does_not_depend_on_other_services` po dodaniu referencji do innego serwisu | referencja do warstwy innej niż `{Inny}.Contracts` (Domain, Application, Infrastructure) | tylko `{Inny}.Contracts` (10.9); nie kopiuj typu kontraktu do swojego serwisu |
| `InvalidOperationException: Brak connection stringu RabbitMq.` przy starcie | brak albo pusty `ConnectionStrings:RabbitMq`: `appsettings.json` ma `"RabbitMq": ""`, a środowisko nie dostarczyło wartości | z IDE uruchamiaj w środowisku `Development` (`appsettings.Development.json` ma `amqp://guest:guest@localhost:5672/`); w compose wartość ustawia `x-service-env`; w klastrze sekret z Vault |

## Do zapamiętania

- Agregat zgłasza zdarzenie (`Raise`) dopiero po zmianie stanu i tylko przy faktycznej zmianie.
- `WriteDbContextBase.SaveChangesAsync`: zbierz → wyczyść → dispatch (jeden przebieg) → jeden `base.SaveChangesAsync`.
  Handlery działają przed commitem, w tym samym scope i tej samej transakcji.
- Handler zdarzenia domenowego: nie zmienia innych agregatów, nie zapisuje, nie woła świata zewnętrznego, nie czyta zegara.
- Zdarzenia integracyjne powstają wyłącznie w translatorach z Application; kontrakt z prymitywów, czas z agregatu.
- Outbox: wiadomość istnieje wtedy i tylko wtedy, gdy zmiana została zatwierdzona. Wysyła ją Worker (`OutboxDelivery.Enabled`).
- Dostarczenie co najmniej raz, bez gwarancji kolejności: inbox plus idempotentna komenda.
- Konsument: wiadomość → komenda; błąd biznesowy = log + ACK; wyjątek = retry → `_error`.
- Kontrakt zmieniasz zgodnie wstecz; zmiana łamiąca (także nazwa typu i namespace) = `V2` i okres publikacji obu wersji.
- Sagi nie są jeszcze zaimplementowane. Wywołania synchroniczne przez Refit wykonuje dziś tylko BFF experience
  (`AddDownstreamApi` + `AddUserTokenForwarding`); serwis → serwis to wyjątek za ACL.

## Powiązane

- Rozdziały: [05 Model domeny](05-model-domeny.md), [06 Warstwa aplikacji](06-warstwa-aplikacji.md),
  [07 Dane i EF Core](07-dane-i-ef-core.md), [11 Cache](11-cache.md), [12 Testy](12-testy.md),
  [13 Lokalne środowisko](13-lokalne-srodowisko-i-debugowanie.md), [14 Logowanie i obserwowalność](14-logowanie-i-obserwowalnosc.md),
  [17 Rozwiązywanie problemów](17-rozwiazywanie-problemow.md).
- Przepisy: [05 Zdarzenia i cache](przepisy/05-zdarzenia-i-cache.md), [08 Wywołanie innego serwisu](przepisy/08-wywolanie-innego-serwisu.md).
- ADR: [0005](../adr/0005-komunikacja-asynchroniczna-outbox-inbox.md) (outbox, inbox, RabbitMQ),
  [0014](../adr/0014-klienci-http-refit-refitter.md) (Refit, Refitter), [0015](../adr/0015-result.md) (`Result`),
  [0017](../adr/0017-kolejnosc-pipeline-behaviors.md) (kolejność behaviors), [0018](../adr/0018-health-checki-readiness.md) (sondy),
  [0021](../adr/0021-schemat-bazy-per-mikroserwis.md) (schemat per serwis), [0024](../adr/0024-value-objects.md) (prymitywy w kontraktach),
  [0027](../adr/0027-zdarzenia-domenowe-dispatch-w-uow.md) (dispatch w UoW), [0028](../adr/0028-knowledge-model-domeny-i-tresc-blokowa.md)
  (ulubione i archiwizacja), [0035](../adr/0035-mediatr-i-masstransit-w-wersjach-open-source.md) (MassTransit 8).
