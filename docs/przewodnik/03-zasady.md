# 3. Zasady rozwiązania i dlaczego takie

**Czego się nauczysz:** wszystkich zasad, których pilnuje review, build i testy; przy każdej: dlaczego istnieje, co ją wymusza
i jak wygląda kod zły i dobry.
**Wymagania:** [01 Start](01-start.md), najlepiej też [02 Architektura w praktyce](02-architektura-w-praktyce.md).

> **W skrócie**
> - Experience: moduł + BFF experience + serwisy domenowe; ruch do domeny experience tylko przez jej BFF, brama brzegowa jest
>   wspólna i poza zakresem (`SuperApp.Gateway` to lokalny zamiennik).
> - Granice: serwis = kontekst, zero współdzielonego modelu, zależności tylko „do środka” (Api → Application → Domain).
> - Domena: logika w agregacie, `Result` zamiast wyjątków, silne ID i value objects, zdarzenia domenowe.
> - CQRS: komendy przez agregaty i Unit of Work (handler nie zapisuje), zapytania bezpośrednio z read modeli.
> - Kontrakty: OpenAPI commitowane, zdarzenia integracyjne wersjonowane, zmiany wstecznie zgodne.
> - Kod: jeden typ na plik, katalogi według odpowiedzialności, dokumentacja po angielsku dla nowych osób, logi przez `[LoggerMessage]`.

Każda zasada ma ADR z kontekstem decyzji ([lista](../adr/README.md)). Jeśli zasada przeszkadza, zaproponuj zmianę ADR
zamiast ją obchodzić: obejście zwykle i tak kończy się błędem kompilacji albo testu architektury. Nowy ADR, który tylko
doprecyzowuje starszy, dopisuje to w nagłówku starszego ADR i w indeksie (ADR-0043); zmiana decyzji to ADR zastępujący.

---

## 3.0 Experience, BFF i brama brzegowa (ADR-0037–0041)

| Zasada | Dlaczego | Co wymusza |
|---|---|---|
| Experience = moduł + BFF experience + 1..n serwisów domenowych; nic nie zakłada, że experience jest tylko jedna | wiele zespołów buduje experience z tych samych szablonów | review, szablony |
| Brama brzegowa jest wspólna dla super appki i poza naszym zakresem; `SuperApp.Gateway` to jej lokalny zamiennik i specyfikacja wymagań, bez logiki experience | nie utrzymujemy komponentu brzegowego na produkcji; logika experience należy do BFF | review (ADR-0037) |
| Moduł woła wyłącznie API publiczne BFF (`/v{n}`) przez bramę; serwisy domenowe nie są wystawiane na brzegu | klient zależy tylko od kontraktu BFF, serwisy zmieniają się bez wpływu na aplikację w sklepie | wymaganie wobec bramy (ADR-0039) |
| Ruch do domeny experience tylko przez jej BFF; BFF-y innych experience wołają API wewnętrzne BFF (`/internal/v{n}`), nigdy serwisy domenowe | jawny, wąski kontrakt zamiast dostępu do cudzej domeny | NetworkPolicy (ADR-0041); lokalnie nieegzekwowane |
| Wywołania w kontekście użytkownika przekazują jego token bez zmian; odbiorca waliduje JWT, scope i reguły zasobu | dane chronią reguły zasobu niezależnie od wołającego | ADR-0040, review |
| BFF orkiestruje, logika biznesowa zostaje w serwisach; BFF nie zmienia stanu kilku domen w jednym żądaniu | brak anemicznych serwisów i rozproszonych transakcji | review |
| BFF zna serwisy tylko przez klientów wygenerowanych Refitterem z ich kontraktów; nie referuje żadnego serwisu (nawet `Contracts`) ani innego BFF, nie ma `DbContext`; serwisy nie referują BFF | kontrakt HTTP jest jedyną zależnością, więc serwis i BFF wdrażają się niezależnie | testy architektury, reguły 12–14 (`tests/SuperApp.ArchitectureTests`) |
| BFF przekazuje odpowiedź serwisu bez zmian (`ToActionResult`) i nie waliduje treści żądań | jedno źródło walidacji i kodów błędów: serwis | review, `Example.Bff.Tests` |

**W repozytorium.** BFF przykładowej experience to `src/Bff/Example.Bff` (szablon `dotnet new superapp-bff`), a lokalna brama ma
jedną trasę experience `/api/example/v{n}/**` do niego; tras do serwisów domenowych nie ma. Szczegóły:
[02 Architektura w praktyce](02-architektura-w-praktyce.md#experience-w-repozytorium).

## 3.1 Granice kontekstów i zależności

### Serwis = bounded context, własny schemat (ADR-0002, ADR-0021)

**Zasada.** Każdy serwis ma własny model domeny, własny schemat w bazie i własny login z uprawnieniami tylko do niego.
Nie ma wspólnego modelu biznesowego między serwisami.

**Dlaczego.** Serwisy mogą się zmieniać niezależnie. Gdyby SleepDiary czytał tabele Knowledge, każda zmiana schematu
Knowledge mogłaby zepsuć SleepDiary bez żadnego sygnału w kompilacji.

**Co wymusza.** Login `sleepdiary_app` nie ma uprawnień do schematu `knowledge` (bootstrap `deploy/sql/01-bootstrap.sql`),
testy architektury blokują referencje między serwisami.

```csharp
// Źle: SleepDiary sięga do danych Knowledge
var titles = await sleepDiaryDb.Database
    .SqlQuery<string>($"SELECT Title FROM knowledge.Materials WHERE Status = 'Published'")
    .ToListAsync();

// Dobrze: SleepDiary subskrybuje zdarzenie Knowledge i trzyma własną kopię potrzebnych pól
public sealed class MaterialPublishedConsumer(ISender sender) : IConsumer<MaterialPublishedV1>
{
    public Task Consume(ConsumeContext<MaterialPublishedV1> context) =>
        sender.Send(new RememberRecommendedMaterial(context.Message.MaterialId, context.Message.Title), context.CancellationToken);
}
```

### Reguła zależności między warstwami

```
Api, Worker, Infrastructure ──► Application ──► Domain ──► SuperApp.Framework.Domain
                                 Application ──► Contracts
Api, Worker ──► Infrastructure   (wyłącznie composition root: rejestracja DI)
```

**Dlaczego.** Domena i przypadki użycia nie zależą od technologii: można je testować bez bazy i wymieniać infrastrukturę
(np. bibliotekę messagingu) bez ruszania logiki.

**Co wymusza.** `tests/SuperApp.ArchitectureTests` (ArchUnitNET) i referencje projektów.

```csharp
// Źle: handler w Application używa EF Core
internal sealed class RenameCategoryHandler(KnowledgeWriteDbContext db) : ICommandHandler<RenameCategory> { ... }

// Dobrze: handler zna tylko port z Domain
internal sealed class RenameCategoryHandler(ICategoryRepository categories) : ICommandHandler<RenameCategory> { ... }
```

### Integracja: zdarzenia domyślnie, ACL wyjątkowo (ADR-0005, ADR-0014)

**Dlaczego.** Wywołanie synchroniczne wiąże dostępność serwisów (awaria jednego to awaria drugiego). Zdarzenia przez outbox
nie giną i nie blokują. Gdy wywołanie synchroniczne jest konieczne i dzieje się w kontekście użytkownika, idzie przez klienta
Refit z **przekazanym tokenem użytkownika**; client credentials tylko dla wywołań systemowych (ADR-0040). Szczegóły:
[10 Zdarzenia i integracja](10-zdarzenia-i-integracja.md), [9.8](09-bezpieczenstwo.md).

---

## 3.2 Model domeny

### Logika biznesowa w agregacie (ADR-0002)

**Zasada.** Reguły („opublikowany materiał musi mieć treść”, „wpis dziennika nie może być z przyszłości”) są w metodach
agregatu. Handler tylko orkiestruje.

**Dlaczego.** Reguła w jednym miejscu jest zawsze sprawdzana: niezależnie od tego, czy operację wywołuje API, Worker czy test.
Reguły w handlerach rozjeżdżają się między przypadkami użycia (model anemiczny).

```csharp
// Źle: reguła w handlerze, agregat to worek właściwości
if (material.Blocks.Count == 0) return MaterialErrors.ContentRequired;
material.Status = PublicationStatus.Published;          // publiczny setter
material.PublishedAt = clock.UtcNow;

// Dobrze: handler wywołuje operację domenową (Knowledge.Application/.../PublishMaterialHandler.cs)
var material = await materials.GetAsync(materialId, cancellationToken);
return material is null ? MaterialErrors.NotFound : material.Publish(clock.UtcNow);
```

### Jedna komenda zmienia jeden agregat

**Dlaczego.** Agregat jest granicą spójności transakcyjnej. Zmiana wielu agregatów w jednej transakcji to blokady, konflikty
współbieżności i ukryte powiązania. Reakcje innych agregatów idą przez zdarzenia.

```csharp
// Źle: archiwizacja materiału od razu czyści ulubione wszystkich użytkowników w tej samej komendzie
material.Archive(now);
foreach (var favorite in await favorites.ListForItemAsync(material.Id)) favorites.Remove(favorite);

// Dobrze: Material.Archive zgłasza MaterialArchived → translator publikuje MaterialArchivedV1 →
// Worker wywołuje komendę RemoveFavoritesOfItem (Knowledge.Worker/Consumers/MaterialArchivedConsumer.cs)
```

### Błędy biznesowe to `Result`, nie wyjątki (ADR-0015)

**Zasada.** Oczekiwane porażki (walidacja, brak zasobu, konflikt, reguła biznesowa) są zwracane jako `Result` z `Error`
o stałym kodzie. Wyjątki tylko dla błędów technicznych.

**Dlaczego.** Kod błędu jest częścią kontraktu API (klient rozpoznaje go po `code`), pipeline nie zatwierdza transakcji przy
porażce, a ścieżki błędów są widoczne w sygnaturze metody.

**Co wymusza.** APP001 (zignorowany `Result` to błąd kompilacji).

```csharp
// Źle
if (Status == PublicationStatus.Archived) throw new InvalidOperationException("Archived");

// Dobrze
if (Status == PublicationStatus.Archived) return MaterialErrors.Archived;   // knowledge.material.archived → 422
```

Odczyt wartości zawsze przez `TryGetValue`:

```csharp
// Źle: zmienna przechowuje wynik, a nie kategorię; wartości z niej nie odczytasz, bo Result<T> nie ma Value (ADR-0047)
var category = Category.Create(command.Name, command.Slug);
if (category.IsFailure) return category.Error;

// Dobrze
if (!Category.Create(command.Name, command.Slug).TryGetValue(out var category, out var error))
{
    return error;
}

categories.Add(category);
return category.Id.Value;
```

### Silne ID i value objects (ADR-0023, ADR-0024)

**Dlaczego.** `MaterialId` i `CollectionId` to różne typy, więc kompilator nie pozwoli ich pomylić. Value object
(`SleepQuality`) nie może mieć niepoprawnej wartości: walidacja jest w jednym miejscu (`Create`).

**Co wymusza.** APP002 (`default`/`new()` to błąd kompilacji).

```csharp
// Źle: obejście walidacji
var quality = new SleepQuality();          // APP002
var id = default(MaterialId);              // APP002
var id2 = MaterialId.FromTrusted(command.MaterialId);   // FromTrusted tylko dla infrastruktury i testów

// Dobrze
if (!MaterialId.Create(command.MaterialId).TryGetValue(out var materialId, out _))
{
    return MaterialErrors.NotFound;
}
```

### Czas przekazywany do domeny, nie czytany w niej

```csharp
// Źle: domena czyta zegar (testy niedeterministyczne)
public Result Publish() { PublishedAt = DateTimeOffset.UtcNow; ... }

// Dobrze: handler przekazuje czas z IClock
public Result Publish(DateTimeOffset now) { PublishedAt = now; ... }
```

Szczegóły: [05 Model domeny](05-model-domeny.md).

---

## 3.3 CQRS i zapis

### Handler komendy nie zapisuje (ADR-0017, ADR-0027)

**Zasada.** Handler kończy się na `repository.Add(...)` albo na wywołaniu metody agregatu. Zapis, dispatch zdarzeń
domenowych, outbox i commit wykonuje `TransactionBehavior` po udanym wyniku.

**Dlaczego.** Każda komenda jest atomowa (agregat, zdarzenia, outbox w jednej transakcji), porażka nigdy nie zostawia
częściowego zapisu, nikt nie zapomni o zapisie ani nie zapisze dwa razy. Wyścig na unikalnym indeksie zamienia się
w `Result` z `Conflict`, a nie w 500.

**Co wymusza.** APP003 blokuje synchroniczne `SaveChanges`; review.

```csharp
// Źle
categories.Add(category);
await dbContext.SaveChangesAsync(cancellationToken);   // handler zna EF i zapisuje sam

// Dobrze
categories.Add(category);                              // zapis i commit zrobi pipeline
return category.Id.Value;
```

### Zapytania omijają domenę (ADR-0003, ADR-0026)

**Zasada.** Handler zapytania leży w Infrastructure, czyta `ReadDbContext` (bez śledzenia zmian) i od razu projektuje
dane na DTO. Bez agregatów, bez repozytoriów.

**Dlaczego.** Odczyty są szybkie (tylko potrzebne kolumny), a model domeny nie musi służyć ekranom.

```csharp
// Źle: ładowanie agregatów do odczytu
var materials = await materialRepository.ListPublishedAsync(); return materials.Select(m => new MaterialDto(...));

// Dobrze: projekcja z read modelu (Knowledge.Infrastructure/Features/Categories/ListCategoriesHandler.cs)
db.Categories.OrderBy(category => category.Name)
    .Select(category => new CategoryDto(category.Id, category.Name, category.Slug))
    .ToListAsync(token)
```

### Cache tylko po stronie odczytu, unieważniany po commicie (ADR-0020)

Szczegóły i wyścig, któremu zapobiega `IUnitOfWork.OnCommitted`: [11 Cache](11-cache.md).

---

## 3.4 API i kontrakty

| Zasada | Dlaczego | Co wymusza |
|---|---|---|
| Kontroler jest cienki: HTTP → komenda/zapytanie → `ToActionResult` | logika w jednym miejscu, spójne odpowiedzi | review |
| Każda akcja ma `[ProducesResponseType]` dla każdego statusu i dokumentację XML | kontrakt OpenAPI kompletny, generowani klienci mają typy | review diffu `openapi/*.json` |
| Błędy jako `application/problem+json` z `code` i `traceId` (32 znaki hex), także z ASP.NET Core (ADR-0044) | klient rozpoznaje błąd po kodzie, wsparcie znajduje żądanie | `ResultHttpExtensions`, `ProblemDetailsConventions` |
| Kontrakt OpenAPI commitowany i wstecznie zgodny | zmiana API widoczna w review, klienci nie psują się bez ostrzeżenia | review, CI dostawcy |
| BFF ma API publiczne `/v{n}` (konsument: moduł) i wewnętrzne `/internal/v{n}` (BFF-y innych experience, tylko zmiany wstecznie zgodne, scope `{experience}.internal.*`); kontrakty serwisów domenowych są wewnętrzne experience | granica publiczne/wewnętrzne na poziomie BFF, nie serwisów | wymaganie wobec bramy, review (ADR-0039) |
| Zdarzenia integracyjne wersjonowane (`...V1`) | konsumenci w innych serwisach | review |
| Enumy w JSON tylko jako nazwy | liczby omijałyby walidację wartości | `ConfigureJson` |

Szczegóły: [08 API i kontrakty](08-api-i-kontrakty.md).

---

## 3.5 Bezpieczeństwo

| Zasada | Dlaczego |
|---|---|
| Brak tokenów w przeglądarce (brama brzegowa, lokalnie profil `bff-web`: ciasteczko `__Host-bff`, sesja w MSSQL) | XSS nie wykradnie tokenu |
| Każdy serwis sam waliduje JWT, nie ufa nagłówkom tożsamości | zero trust: przechwycone wywołanie wewnątrz klastra nie podszyje się pod użytkownika |
| `[RequiresScope]` na każdej komendzie i zapytaniu; reguły zasobu w handlerze/agregacie | brama nie zna reguł domeny; autoryzacja jest blisko danych; BFF sprawdza wcześniej, serwis zawsze |
| Token użytkownika przekazywany bez zmian w wywołaniach w kontekście użytkownika; odbiorca nie ufa identyfikatorowi użytkownika z payloadu | komponent z tokenem technicznym mógłby pytać o dane dowolnej osoby (ADR-0040) |
| Client credentials tylko dla wywołań systemowych (scope `{experience}.internal.system.*` dla API wewnętrznego BFF, `{serwis}.system.{akcja}` między serwisami experience, ADR-0042; audyt `azp`); token exchange to otwarta furtka | wyjątek musi być widoczny i ograniczony |
| Czas życia tokenu sprawdzany z `ClockSkew` 30 s | krótsze okno dla tokenu po wylogowaniu |
| NetworkPolicy per experience (etykiety `app.kubernetes.io/part-of`, `superapp.example/experience-role`) | izolację experience gwarantuje sieć, nie token (ADR-0041) |
| `X-CSRF: 1` na `/api/*`; wylogowanie przez `GET` z `sid` | ochrona przed CSRF przy ciasteczku sesyjnym |
| Sekrety z Vault, nigdy w repozytorium; zakaz logowania tokenów i danych osobowych | wycieki |
| PostHog zna użytkownika tylko jako pseudonim `u_…`; zdarzenia bez treści użytkownika i danych z dziennika snu; klienci po zgodzie | RODO (dane o zdrowiu), ePrivacy |
| Serwisy nie wysyłają zdarzeń analitycznych; robi to forwarder ze zdarzeń integracyjnych | awaria lub zmiana analityki nie dotyka komend |
| Feature flag przez `IFeatureFlags`, z bezpieczną wartością domyślną; flaga nie jest uprawnieniem | flagę zmienia się bez review; o dostępie decyduje scope |

Szczegóły: [09 Bezpieczeństwo](09-bezpieczenstwo.md), [21 Analityka i feature flags](21-analityka-i-feature-flags.md).

---

## 3.6 Kod i dokumentacja

### Jeden typ na plik, katalogi według odpowiedzialności (ADR-0032)

**Co wymusza.** APP004 (więcej niż jeden typ w pliku), APP005 (nazwa pliku ≠ nazwa typu), IDE0130 (przestrzeń nazw ≠ katalog).

```
Źle:  Knowledge.Domain/Common/Ids.cs           (CategoryId, MaterialId, CollectionId w jednym pliku)
Dobrze: Knowledge.Domain/Categories/CategoryId.cs, Knowledge.Domain/Materials/MaterialId.cs, ...
```

Konwencje nazw plików: typ generyczny `Result{T}.cs`, dodatkowa część typu `partial` `Order.Log.cs`, migracja
`AddCategoryDescription.cs` (bez daty; plik `.Designer.cs` z datą zostaje).

### Widoczność i styl

- `sealed` domyślnie; `internal` domyślnie, `public` tylko API projektu (typy używane przez inne projekty).
- File-scoped namespaces, primary constructors tam, gdzie czytelne, `CancellationToken` w każdej metodzie asynchronicznej.
- Repozytoria: odczyty asynchroniczne, `Add`/`Remove` synchroniczne (tylko change tracker).

### Dokumentacja w kodzie po angielsku, dla osoby nowej (ADR-0033)

**Co wymusza.** CS1591 (brak dokumentacji publicznej składowej), APP006 (brak `param`/`typeparam`/`returns`; we Frameworku
także `remarks`). Jakość treści: review.

```csharp
// Źle: powtarza sygnaturę
/// <summary>Command without return value.</summary>
public interface ICommand : ICommand<Result>;

// Dobrze: rola, przebieg, reguły, przykład (fragment SuperApp.Framework.Application/Messaging/ICommand.cs)
/// <summary>
/// A request to change the state of the system that reports only success or failure, without returning data.
/// </summary>
/// <remarks>
/// Most state changes are of this kind: renaming, publishing, archiving, deleting... In a controller,
/// <c>this.ToActionResult(result)</c> turns a successful result into HTTP 204 No Content ...
/// </remarks>
```

Szczegóły: [15 Dokumentacja w kodzie](15-dokumentacja-w-kodzie.md).

### Logowanie (ADR-0008)

```csharp
// Źle: interpolacja, dane osobowe, brak EventId
logger.LogInformation($"User {email} saved entry {entry}");

// Dobrze: source-generated, stały EventId z zakresu serwisu, bez danych osobowych
[LoggerMessage(2001, LogLevel.Warning, "Removing favorites of archived material {MaterialId} rejected: {ErrorCode}")]
private static partial void LogRejected(ILogger logger, Guid materialId, string errorCode);
```

Szczegóły: [14 Logowanie i obserwowalność](14-logowanie-i-obserwowalnosc.md).

### Pakiety (ADR-0035)

Nowy pakiet tylko z uzasadnieniem, wersje w `Directory.Packages.props`. MediatR zostaje na 12.x, MassTransit na 8.x
(ostatnie wersje open source); wyższe wersje są komercyjne i wymagają nowego ADR.

---

## 3.7 Migracje (ADR-0004)

| Zasada | Dlaczego |
|---|---|
| Każda zmiana modelu zapisu = nowa migracja; nigdy edycja zastosowanej | zastosowana migracja jest historią baz na wszystkich środowiskach |
| Expand/contract: stara i nowa wersja aplikacji działają na tym samym schemacie | rolling update bez przestoju |
| Serwisy nigdy nie migrują przy starcie; robi to Migrator (dev/test) albo DBA (prod) | wiele replik = wyścig; prod wymaga przeglądu |
| Startup probe sprawdza brak oczekujących migracji | nowa wersja nie przyjmie ruchu przed migracją |

Szczegóły: [07 Dane i EF Core](07-dane-i-ef-core.md), [przepis 04](przepisy/04-zmiana-modelu-i-migracja.md).

---

## 3.8 Co oznaczają błędy kompilacji

| Kod | Znaczenie | Jak naprawić |
|---|---|---|
| APP001 | Zignorowany `Result` | obsłuż (`TryGetValue`, `IsFailure`) albo zwróć dalej; `_ =` tylko świadomie |
| APP002 | `default`/`new()` dla silnego ID lub value objectu | `Xxx.Create(...)` (dane z zewnątrz) albo `Xxx.New()` (nowe ID) |
| APP003 | Zakazane API: `INotification`, `IPublisher`, `IMediator`, synchroniczne `SaveChanges` | zdarzenia przez `IDomainEventHandler<T>`, wysyłanie przez `ISender`, zapis przez Unit of Work |
| APP004 | Więcej niż jeden typ w pliku | przenieś typ do własnego pliku |
| APP005 | Nazwa pliku inna niż typ | zmień nazwę pliku (`Result{T}.cs`, `Order.Log.cs`); migracje bez daty w nazwie to konwencja, APP005 ich nie sprawdza |
| APP006 | Niekompletna dokumentacja XML | uzupełnij `param`/`typeparam`/`returns`/`remarks`; implementacje: `/// <inheritdoc />` |
| CS1591 | Publiczna składowa bez dokumentacji | dopisz dokumentację albo zmień widoczność na `internal` |
| IDE0130 | Przestrzeń nazw niezgodna z katalogiem | popraw `namespace` albo przenieś plik |
| NU1901–NU1904 | Znana podatność pakietu (także przechodniego) | zaktualizuj w ramach wersji open source albo zgłoś w ADR |
| Test architektury | Niedozwolona zależność | odwróć zależność przez port w Application |

Więcej przypadków: [17 Rozwiązywanie problemów](17-rozwiazywanie-problemow.md).

## Do zapamiętania

- Zasady nie są stylem, tylko kontraktem zespołu: większość z nich łamie build albo testy.
- Pytanie kontrolne przy każdej zmianie: czy logika jest w agregacie, czy handler tylko orkiestruje, czy zapis robi pipeline,
  czy kontrakt jest wstecznie zgodny, czy dokumentacja uczy.
- Gdy reguła wydaje się zbędna, znajdź jej ADR; zmiana reguły to zmiana ADR, instrukcji Copilota i podręcznika.
