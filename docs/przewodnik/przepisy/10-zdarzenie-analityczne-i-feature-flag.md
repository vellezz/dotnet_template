# Przepis 10: zdarzenie analityczne i feature flag

**Kiedy:** analiza produktu potrzebuje nowego faktu z backendu w PostHog (część A) albo nową funkcję serwisu chcesz
udostępniać stopniowo, testować w eksperymencie lub móc wyłączyć bez wdrożenia (część B).
**Przykład:** A) nowe zdarzenie `knowledge_collection_published` (publikacja kolekcji w Knowledge); B) flaga
`knowledge_material_ratings` dla oceniania materiałów. **Obu przykładów nie ma w repozytorium**: to wzorce oparte na istniejącym
kodzie (`MaterialPublishedConsumer`, `ConsumerMappingTests`, `IFeatureFlags`).
**Wyjaśnienia:** [21 Analityka i feature flags](../21-analityka-i-feature-flags.md),
[10 Zdarzenia i integracja](../10-zdarzenia-i-integracja.md), ADR-0036.

W blokach kodu komentarze dokumentacji XML pokazano tylko tam, gdzie są nowe i wymagane (CS1591, APP006).

---

## Szybki start

```bash
dotnet superapp add product-event CollectionPublishedV1    # konsument w forwarderze, nazwa w ProductEventNames, referencja do Contracts
dotnet superapp add flag Knowledge material_ratings        # KnowledgeFeatureFlags.MaterialRatings, domyślnie false (--default-on dla wyłącznika)
```

Konsument dostaje tylko identyfikatory (GUID-y), czas zdarzenia i użytkownika (`UserId`, zamieniany przez sink na pseudonim). Przegląd
prywatności, test w `ConsumerMappingTests` i odczyt flagi w handlerze opisują kroki poniżej. Obie operacje mają `remove`.
[22 Narzędzie](../22-narzedzie-superapp.md).

## A. Nowe zdarzenie backendowe

### Pliki, które powstają lub się zmieniają

```
Knowledge.Domain/Collections/Events/CollectionPublished.cs                      A1 zdarzenie domenowe (jeśli go nie ma)
Knowledge.Domain/Collections/Collection.cs                                      A1 Raise(...) w Publish
Knowledge.Contracts/CollectionPublishedV1.cs                                    A1 zdarzenie integracyjne
Knowledge.Application/IntegrationEvents/CollectionPublishedTranslator.cs        A1 translator
src/Analytics/SuperApp.AnalyticsForwarder/Events/ProductEventNames.cs                A3 nazwa zdarzenia
src/Analytics/SuperApp.AnalyticsForwarder/Consumers/CollectionPublishedConsumer.cs   A4 konsument
src/Analytics/SuperApp.AnalyticsForwarder.Tests/ConsumerMappingTests.cs              A5 test mapowania
deploy/helm/superapp-analytics-forwarder/values.yaml                                 A6 kolejka w KEDA
```

### Krok A1: zdarzenie integracyjne

Forwarder zna serwisy **wyłącznie** przez `{Serwis}.Contracts` (reguła 10 testów architektury). Jeśli fakt, który chcesz
analizować, nie jest jeszcze publikowany jako zdarzenie integracyjne, dodaj je zwykłą drogą: zdarzenie domenowe → translator →
outbox ([przepis 05, części A i B](05-zdarzenia-i-cache.md#a-zdarzenie-domenowe)). Nie dodawaj do serwisu niczego „dla
analityki”: serwis publikuje fakt biznesowy, a forwarder decyduje, co z niego trafi do PostHog.

`Collection.Publish` nie zgłasza dziś zdarzenia (dokumentacja `CollectionArchivedV1`: „There is no corresponding collection
published event”). Wzorzec:

```csharp
// Knowledge.Domain/Collections/Events/CollectionPublished.cs
public sealed record CollectionPublished(CollectionId CollectionId, int ItemCount, DateTimeOffset PublishedAt) : IDomainEvent;
```

```csharp
// Knowledge.Domain/Collections/Collection.cs, metoda Publish: po zmianie stanu, tylko przy faktycznej zmianie
Status = PublicationStatus.Published;
PublishedAt = now;
UpdatedAt = now;
Raise(new CollectionPublished(Id, _items.Count, now));
return Result.Success();
```

```csharp
// Knowledge.Contracts/CollectionPublishedV1.cs (dokumentacja XML jak w CollectionArchivedV1: kiedy, gwarancja dostarczenia, pola)
public sealed record CollectionPublishedV1(Guid CollectionId, int ItemCount, DateTimeOffset PublishedAt);
```

```csharp
// Knowledge.Application/IntegrationEvents/CollectionPublishedTranslator.cs
internal sealed class CollectionPublishedTranslator(IIntegrationEventPublisher publisher) : IDomainEventHandler<CollectionPublished>
{
    public Task HandleAsync(CollectionPublished domainEvent, CancellationToken cancellationToken) =>
        publisher.PublishAsync(
            new CollectionPublishedV1(domainEvent.CollectionId.Value, domainEvent.ItemCount, domainEvent.PublishedAt),
            cancellationToken);
}
```

Testy domeny i translatora jak w przepisie 05 (kroki A3, B3). Jeśli zdarzenie integracyjne już istnieje, pomiń ten krok.
Zdarzenie innego, **nowego** serwisu wymaga referencji forwardera do jego `Contracts` w
`src/Analytics/SuperApp.AnalyticsForwarder/SuperApp.AnalyticsForwarder.csproj` (obok `Knowledge.Contracts` i `SleepDiary.Contracts`);
żadnej innej warstwy serwisu forwarder referencjonować nie może.

### Krok A2: lista właściwości i przegląd prywatności

Zanim napiszesz kod, wypisz w opisie pull requestu: nazwę zdarzenia, właściciela (`system` albo użytkownik), każdą właściwość
z przykładową wartością i uzasadnieniem. Dla przykładu:

| Zdarzenie | Właściciel | Właściwość | Przykład | Po co |
|---|---|---|---|---|
| `knowledge_collection_published` | `system` (publikuje redaktor, ale to fakt katalogu, nie zachowanie czytelnika) | `collection_id` | `"7d2f..."` | łączenie z otwarciami kolekcji w SPA |
| | | `item_count` | `5` | czy liczba materiałów wpływa na ukończenia |

Czego **nie** dodajesz, nawet jeśli jest w kontrakcie: tytułu, opisu, treści wpisanej przez użytkownika, danych z dziennika snu
(daty, czasu snu, jakości), `sub` jako właściwości. Recenzent sprawdza listę względem
[21.2](../21-analityka-i-feature-flags.md#212-prywatność-i-zgoda). Właściwość z danymi o zdrowiu nie przechodzi nigdy.

### Krok A3: nazwa w `ProductEventNames`

Konwencja `{serwis}_{obiekt}_{czasownik w czasie przeszłym}`, snake case. Nazwa jest kontraktem z osobami budującymi analizy:
dodajesz nową stałą, istniejących nie zmieniasz.

```csharp
// src/Analytics/SuperApp.AnalyticsForwarder/Events/ProductEventNames.cs
/// <summary>
/// <c>knowledge_collection_published</c>, from <c>CollectionPublishedV1</c>; system event. Properties: <c>collection_id</c>,
/// <c>item_count</c>.
/// </summary>
public const string KnowledgeCollectionPublished = "knowledge_collection_published";
```

### Krok A4: konsument

W `src/Analytics/SuperApp.AnalyticsForwarder/Consumers/`, `public sealed`, tylko mapowanie; bez komend, bez stanu, bez wywołań
innych serwisów. Rejestracja jest automatyczna (`bus.AddConsumers(typeof(Program).Assembly)`), a kolejka powstaje sama:
`analytics-collection-published` (prefiks `analytics`, nazwa konsumenta bez `Consumer`, kebab case, quorum).

```csharp
// src/Analytics/SuperApp.AnalyticsForwarder/Consumers/CollectionPublishedConsumer.cs
using SuperApp.AnalyticsForwarder.Events;
using MassTransit;
using Knowledge.Contracts;

namespace SuperApp.AnalyticsForwarder.Consumers;

/// <summary>
/// Forwards <see cref="CollectionPublishedV1"/> to product analytics as <see cref="ProductEventNames.KnowledgeCollectionPublished"/> (ADR-0036).
/// </summary>
/// <remarks>
/// System event. The title and description are not forwarded: analytics groups by collection identifier, and texts belong to the catalogue.
/// The consumer only maps the message; it holds no state and calls no service, so a redelivered message only repeats the event.
/// </remarks>
/// <param name="sink">Destination of product events.</param>
public sealed class CollectionPublishedConsumer(IProductEventSink sink) : IConsumer<CollectionPublishedV1>
{
    /// <summary>Maps the message to the product event and queues it.</summary>
    /// <param name="context">The consumed message and its MassTransit metadata (message identifier).</param>
    /// <returns>A completed task: queuing does not wait for PostHog.</returns>
    public Task Consume(ConsumeContext<CollectionPublishedV1> context)
    {
        var message = context.Message;
        sink.Capture(new ProductEvent(
            ProductEventNames.KnowledgeCollectionPublished,
            message.PublishedAt,
            context.MessageId,
            Subject: null,
            new Dictionary<string, object>
            {
                ["collection_id"] = message.CollectionId.ToString(),
                ["item_count"] = message.ItemCount,
            }));
        return Task.CompletedTask;
    }
}
```

| Decyzja | Reguła |
|---|---|
| `OccurredAt` | czas faktu z wiadomości (`PublishedAt`), nigdy `DateTimeOffset.UtcNow` |
| `Subject` | `null` dla zdarzeń systemowych; `sub` użytkownika z wiadomości (np. `UserId` w `SleepEntryRecordedV1`), gdy zdarzenie jest zachowaniem tej osoby. Sink zamieni go na pseudonim. |
| właściwości | klucze snake case, wartości prymitywne, identyfikatory jako `string`; nic spoza listy z kroku A2 |
| wyjątki | nie łap; retry 100/500/1000/5000 ms, potem `analytics-collection-published_error` |
| `[LoggerMessage]` | zwykle niepotrzebny; jeśli dodasz, `EventId` z zakresu 9000–9999 i wpis w [rejestrze](../../logowanie-eventid.md) |

### Krok A5: test na test harness

Dopisz przypadek do `ConsumerMappingTests` (metoda pomocnicza `ConsumeAsync` już tam jest). Sprawdź nazwę, właściciela, czas
faktu, `message_id` i **dokładną** listę kluczy właściwości:

```csharp
// src/Analytics/SuperApp.AnalyticsForwarder.Tests/ConsumerMappingTests.cs
[Fact]
public async Task Collection_published_is_a_system_event_with_identifier_and_item_count_only()
{
    var collectionId = Guid.NewGuid();

    var captured = await ConsumeAsync(new CollectionPublishedV1(collectionId, 5, At));

    Assert.Equal(ProductEventNames.KnowledgeCollectionPublished, captured.Name);
    Assert.Null(captured.Subject);
    Assert.Equal(At, captured.OccurredAt);
    Assert.NotNull(captured.MessageId);
    Assert.Equal(["collection_id", "item_count"], captured.Properties.Keys.Order());
    Assert.Equal(collectionId.ToString(), captured.Properties["collection_id"]);
    Assert.Equal(5, captured.Properties["item_count"]);
}
```

Dokładna lista kluczy sprawia, że każda przyszła zmiana właściwości wymaga zmiany testu, a więc przechodzi przez review
i przegląd prywatności. Dla zdarzenia użytkownika sprawdź `Assert.Equal("user-sub-1", captured.Subject)`; dla zdarzenia
z SleepDiary dodatkowo, że nie ma żadnej właściwości z danymi o zdrowiu.

```bash
dotnet test --project src/Analytics/SuperApp.AnalyticsForwarder.Tests
dotnet test --project tests/SuperApp.ArchitectureTests          # reguły 10 i 11
```

### Krok A6: kolejka w KEDA

Forwarder skaluje się po długości swoich kolejek; nowej kolejki KEDA nie zna, dopóki jej nie dopiszesz:

```yaml
# deploy/helm/superapp-analytics-forwarder/values.yaml
keda:
  queues:
    - analytics-material-published
    - analytics-material-archived
    - analytics-collection-archived
    - analytics-sleep-entry-recorded
    - analytics-collection-published
```

### Krok A7: weryfikacja lokalna (log 9001)

Lokalnie analityka jest wyłączona, więc forwarder **loguje** zdarzenie zamiast je wysyłać. To wystarcza, żeby sprawdzić
mapowanie od końca do końca (komenda → outbox → RabbitMQ → forwarder):

```bash
dotnet build SuperApp.slnx
docker compose -f deploy/local/docker-compose.yml --profile app up -d --build knowledge-api knowledge-worker analytics-forwarder
# opublikuj kolekcję: POST /v1/collections/{id}/publish (token dev-cli użytkownika editor, jak w 01 Start)
docker compose -f deploy/local/docker-compose.yml logs analytics-forwarder | grep "\[9001\]" -A1
# info: SuperApp.AnalyticsForwarder.Events.LoggingProductEventSink[9001]
#       Analytics disabled: product event knowledge_collection_published (system, properties: collection_id,item_count) not sent
```

Brak wpisu: sprawdź kolejkę `analytics-collection-published` w panelu RabbitMQ (`http://localhost:15672`, `Consumers: 1`),
`knowledge.OutboxMessage` (Worker serwisu musi działać) i log translatora/komendy. Wysyłkę do PostHog sprawdzasz tylko
z własnym projektem deweloperskim ([21.12](../21-analityka-i-feature-flags.md#2112-lokalnie-krok-po-kroku)).

### Krok A8: przekaż nazwę

Zdarzenie pojawi się w PostHog po wdrożeniu forwardera i serwisu. Przekaż osobom budującym analizy nazwę, właściwości
i znaczenie (np. w opisie zmiany); od tej chwili nazwy nie zmieniasz.

---

## B. Nowa flaga w serwisie

### Pliki, które powstają lub się zmieniają

```
Knowledge.Application/KnowledgeFeatureFlags.cs                                   B1 deklaracja flagi (pierwsza flaga serwisu = nowa klasa)
Knowledge.Domain/Library/LibraryErrors.cs                                        B2 błąd „funkcja niedostępna” (jeśli potrzebny)
Knowledge.Application/Features/Library/RateMaterial/RateMaterialHandler.cs       B2 użycie w handlerze
Knowledge.Application.Tests/Fakes/FakeFeatureFlags.cs                            B3 fake portu (raz na projekt testów)
Knowledge.Application.Tests/RateMaterialHandlerTests.cs                          B3 test obu stanów flagi
```

`AddAppFeatureFlags` jest już wołane w `AddKnowledgeCore` (i w każdym serwisie z szablonu): rejestracji nie zmieniasz.

### Krok B1: deklaracja

W `{Serwis}.Application/{Serwis}FeatureFlags.cs`, obok `{Serwis}Scopes`. Klucz z prefiksem serwisu, wartość domyślna
bezpieczna do trwałego działania (nowa funkcja: `false`; wyłącznik awaryjny istniejącej funkcji: `true`):

```csharp
// Knowledge.Application/KnowledgeFeatureFlags.cs
using SuperApp.Framework.Application.FeatureFlags;

namespace Knowledge.Application;

/// <summary>
/// Feature flags of the Knowledge service (ADR-0036): switches for features being rolled out gradually and kill switches of existing
/// features, evaluated through <see cref="IFeatureFlags"/>.
/// </summary>
/// <remarks>
/// Keys are PostHog flag keys with the <c>knowledge_</c> prefix, and configuration keys (<c>FeatureFlags:{key}</c>) where analytics is
/// disabled. Each default value is what users get when PostHog cannot answer, so it must be safe to run with indefinitely. A flag is not a
/// permission: scopes still apply (ADR-0012).
/// </remarks>
public static class KnowledgeFeatureFlags
{
    /// <summary>
    /// <c>knowledge_material_ratings</c>: readers can rate published materials. Rolled out gradually; off by default, so the feature stays
    /// hidden when PostHog is unreachable.
    /// </summary>
    public static readonly FeatureFlag MaterialRatings = new("knowledge_material_ratings", DefaultValue: false);
}
```

### Krok B2: użycie w handlerze

Flaga przełącza zachowanie w handlerze komendy albo zapytania; reguły biznesowe zostają w agregacie (jeśli agregat potrzebuje
decyzji, dostaje ją jako argument). Komenda nadal ma `[RequiresScope]`: flaga nie jest uprawnieniem.

```csharp
// Knowledge.Domain/Library/LibraryErrors.cs (nowy błąd)
/// <summary>
/// <c>knowledge.library.ratings_not_available</c> (NotFound, HTTP 404): rating materials is switched off for this user
/// (feature flag <c>knowledge_material_ratings</c>, ADR-0036).
/// </summary>
/// <remarks>
/// NotFound, so that a client sees the feature as not existing yet, exactly as before the rollout. Returned before any state change.
/// </remarks>
public static readonly Error RatingsNotAvailable =
    Error.NotFound("knowledge.library.ratings_not_available", "Ocenianie materiałów nie jest dostępne.");
```

```csharp
// Knowledge.Application/Features/Library/RateMaterial/RateMaterialHandler.cs
internal sealed class RateMaterialHandler(
    IMaterialRatingRepository ratings,
    IMaterialRepository materials,
    IFeatureFlags flags,
    ICurrentUser currentUser,
    IClock clock) : ICommandHandler<RateMaterial>
{
    public async Task<Result> Handle(RateMaterial command, CancellationToken cancellationToken)
    {
        // First, before reading anything: a switched-off feature behaves as if it did not exist.
        if (!await flags.IsEnabledAsync(KnowledgeFeatureFlags.MaterialRatings, cancellationToken))
        {
            return LibraryErrors.RatingsNotAvailable;
        }

        if (!currentUser.RequireUserId().TryGetValue(out var userId, out var userError))
        {
            return userError;
        }

        // ... material lookup and the aggregate method, as in MarkMaterialCompletedHandler
        return Result.Success();
    }
}
```

Co warto wiedzieć ([21.6](../21-analityka-i-feature-flags.md#216-feature-flags)):

- w żądaniu flaga jest liczona raz (zakres DI) dla pseudonimu użytkownika; kolejne odczyty nie idą do sieci;
- przy awarii PostHog, timeoucie (1 s) albo braku flagi w PostHog handler dostaje `DefaultValue` (log 400/401, metryka
  `superapp.feature_flags.fallbacks`); nie łap wyjątków wokół `IsEnabledAsync`;
- w konsumencie Workera flaga jest liczona dla `system` (jedna wartość dla wszystkich wiadomości);
- jeśli SPA ukrywa przycisk tą samą flagą (`posthog.isFeatureEnabled('knowledge_material_ratings')`), backend i tak musi ją
  sprawdzić, jak wyżej.

### Krok B3: test handlera z fake'iem `IFeatureFlags`

Fake raz na projekt testów, obok `FakeCurrentUser`:

```csharp
// Knowledge.Application.Tests/Fakes/FakeFeatureFlags.cs
using SuperApp.Framework.Application.FeatureFlags;

namespace Knowledge.Application.Tests.Fakes;

internal sealed class FakeFeatureFlags(params FeatureFlag[] enabled) : IFeatureFlags
{
    public ValueTask<bool> IsEnabledAsync(FeatureFlag flag, CancellationToken cancellationToken) =>
        ValueTask.FromResult(enabled.Contains(flag));
}
```

Testy obu stanów flagi (styl `LibraryHandlerTests`):

```csharp
// Knowledge.Application.Tests/RateMaterialHandlerTests.cs
public sealed class RateMaterialHandlerTests
{
    private readonly FakeClock _clock = new();
    private readonly FakeMaterialRepository _materials = new();
    private readonly FakeMaterialRatingRepository _ratings = new();

    [Fact]
    public async Task Rating_is_not_available_while_the_flag_is_off()
    {
        var handler = Handler(new FakeFeatureFlags());

        var result = await handler.Handle(new RateMaterial(Guid.NewGuid(), 5), TestContext.Current.CancellationToken);

        Assert.Equal(LibraryErrors.RatingsNotAvailable, result.Error);
        Assert.Empty(_ratings.Items);
    }

    [Fact]
    public async Task Rating_is_saved_when_the_flag_is_on()
    {
        var material = PublishedMaterial();
        var handler = Handler(new FakeFeatureFlags(KnowledgeFeatureFlags.MaterialRatings));

        var result = await handler.Handle(new RateMaterial(material.Id.Value, 5), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Single(_ratings.Items);
    }

    private RateMaterialHandler Handler(FakeFeatureFlags flags) =>
        new(_ratings, _materials, flags, new FakeCurrentUser("user-1"), _clock);

    // PublishedMaterial(): as in LibraryHandlerTests
}
```

Test integracyjny (Testcontainers) działa na `ConfigurationFeatureFlags`, bo `ServiceFixture` nie ustawia
`Analytics:ProjectToken`. Żeby włączyć flagę w testach integracyjnych projektu, dopisz ją do konfiguracji fixture'a:

```csharp
// Knowledge.IntegrationTests/Infrastructure/ServiceFixture.cs (fragment słownika AddInMemoryCollection)
["FeatureFlags:knowledge_material_ratings"] = "true",
```

### Krok B4: lokalne włączenie

Bez analityki (domyślnie) flaga ma wartość domyślną, dopóki jej nie ustawisz w procesie, który wykonuje handler (API albo
Worker):

```json
// Knowledge.Api/appsettings.Development.json (lokalnie, bez commitowania zmiany, jeśli to tylko eksperyment)
{
  "FeatureFlags": {
    "knowledge_material_ratings": true
  }
}
```

albo zmienna `FeatureFlags__knowledge_material_ratings=true` w profilu `launchSettings.json` / terminalu. Kontenery z compose
nie dostają zmiennych `FeatureFlags__*`: do próby uruchom API z IDE (tryb hybrydowy,
[13](../13-lokalne-srodowisko-i-debugowanie.md#1-dwa-tryby-pracy)). Wartość musi być `true` albo `false`.

### Krok B5: flaga w PostHog

Przed wdrożeniem kodu utwórz flagę w projekcie PostHog **każdego** środowiska (dev, test, prod) z identycznym kluczem
(`knowledge_material_ratings`):

| Ustawienie | Zalecenie |
|---|---|
| typ | boolean (port czyta tylko wartość logiczną) |
| warunek | procent użytkowników (rollout „przyklejony” do pseudonimu), ewentualnie warunek na `distinct_id`; nie targetuj po danych osobowych |
| flaga używana w Workerze | wartość dla `system`: warunek na `distinct_id = system` albo 0 % / 100 % |
| start | 0 % albo mała grupa testowa; zwiększaj, obserwując błędy i metryki |

Brak flagi w PostHog nie psuje żądań (wartość domyślna), ale daje log 400 `unknown flag` przy każdym odczycie: to sygnał, że
o tym kroku zapomniano.

### Krok B6: sprzątanie po pełnym rolloucie

Flaga to dług techniczny. Gdy funkcja działa dla 100 % użytkowników na prod i nie planujesz jej wyłączać:

1. usuń odczyt flagi i gałąź „wyłączone” z handlera (oraz błąd `RatingsNotAvailable`, jeśli nic go już nie zwraca);
2. usuń stałą z `KnowledgeFeatureFlags` (klasę, jeśli była ostatnia), testy stanu „wyłączone” i wpisy `FeatureFlags:*`
   w konfiguracji i fixture'ach;
3. wdroż; **dopiero potem** usuń flagę w PostHog (stara wersja aplikacji w trakcie rolling update nadal ją czyta).

Wyłącznik awaryjny (`DefaultValue: true`) może zostać na stałe: wtedy opisz w dokumentacji stałej, kiedy go użyć.

---

## Typowe błędy

| Objaw | Przyczyna | Naprawa |
|---|---|---|
| Brak logu 9001 po akcji | kolejka `analytics-...` bez konsumenta (stary obraz forwardera), zdarzenie integracyjne nie wyszło z outboxa (Worker serwisu nie działa) | `--build analytics-forwarder`; `knowledge.OutboxMessage`; panel RabbitMQ |
| Test `ConsumerMappingTests` przechodzi, a w PostHog brak właściwości | konsument dodał właściwość pod inną nazwą niż ta z przeglądu | klucze snake case zgodne z listą z A2 i z testem |
| Zdarzenie użytkownika w PostHog jako `system` | `Subject: null` zamiast `sub` z wiadomości | przekaż `UserId` z kontraktu; sprawdź w teście `captured.Subject` |
| W PostHog trafiła data wpisu snu albo tytuł | właściwość skopiowana z kontraktu bez przeglądu | usuń z konsumenta i z danych w PostHog; test z dokładną listą kluczy |
| `Analytics_forwarder_depends_only_on_contracts_of_services` nie przechodzi | referencja forwardera do `{Serwis}.Application`/`Domain` | tylko `{Serwis}.Contracts`; brakujące pole dodaj do kontraktu (zgodnie wstecz) |
| Forwarder nie skaluje się przy rosnącej nowej kolejce | brak kolejki w `keda.queues` | krok A6 |
| Zmieniona nazwa zdarzenia „psuje” wykresy | nazwy są kontraktem | przywróć starą nazwę; nowe znaczenie = nowa nazwa |
| Flaga zawsze `false` na środowisku, log 400 `unknown flag` | flagi nie ma w projekcie PostHog tego środowiska albo inny klucz | krok B5 |
| Flaga lokalnie nie działa | wpis w innym procesie (API vs Worker), literówka w kluczu, lokalnie włączona analityka | krok B4 |
| Funkcja „za flagą” dostępna dla użytkownika bez uprawnień | brak `[RequiresScope]`, flaga traktowana jak uprawnienie | scope na komendzie; flaga tylko włącza funkcję |
| Testy handlera przechodzą tylko dla jednego stanu flagi | brak testu stanu „wyłączone” | dwa testy z `FakeFeatureFlags` |

## Checklista

- [ ] Fakt biznesowy publikowany jako zdarzenie integracyjne (outbox), forwarder zależy tylko od `Contracts`.
- [ ] Lista właściwości przeszła przegląd prywatności; brak treści użytkownika, danych o zdrowiu, `sub`.
- [ ] Nazwa w `ProductEventNames` (`{serwis}_{obiekt}_{czasownik}`), z dokumentacją źródła i właściwości; istniejące nazwy bez zmian.
- [ ] Konsument cienki, czas faktu z wiadomości, `Subject` poprawny, test z dokładną listą kluczy.
- [ ] Kolejka `analytics-{konsument}` w `keda.queues`; log 9001 sprawdzony lokalnie.
- [ ] Flaga zadeklarowana w `{Serwis}FeatureFlags` z prefiksem serwisu i bezpieczną wartością domyślną.
- [ ] Flaga sprawdzana w backendzie, scope bez zmian; testy obu stanów flagi.
- [ ] Flaga utworzona w PostHog każdego środowiska; plan sprzątania po pełnym rolloucie.
- [ ] `dotnet build` bez ostrzeżeń, `dotnet test` zielone (w tym testy architektury).
