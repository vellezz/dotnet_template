# Przepis 01: endpoint komendy (operacja zmieniająca stan)

**Kiedy:** nowa operacja `POST`/`PUT`/`DELETE`, która zmienia stan **jednego** agregatu w istniejącym kontekście.
**Przykład prowadzący:** zmiana nazwy kategorii w Knowledge, `PUT /v1/categories/{categoryId}/name` (przypadek użycia
`RenameCategory`). Wariant „tworzenie zasobu z odpowiedzią 201” pokazuje `CreateCategory` w kroku 7.
**Wyjaśnienia:** [05 Model domeny](../05-model-domeny.md), [06 Warstwa aplikacji](../06-warstwa-aplikacji.md),
[08 API i kontrakty](../08-api-i-kontrakty.md), [15 Dokumentacja w kodzie](../15-dokumentacja-w-kodzie.md),
[12 Testy](../12-testy.md), [przepis 11](11-nowa-experience-i-bff.md) (BFF experience).

Operacja powstaje w serwisie domenowym, ale moduł jej nie zobaczy, dopóki nie wystawisz jej w **BFF experience** (krok 9): moduł
woła wyłącznie API publiczne BFF przez bramę, a serwis domenowy nie ma trasy w bramie (ADR-0038, ADR-0039).

Każdy plik poniżej jest pokazany **w całości**, z dokumentacją XML, dokładnie tak, jak jest w repozytorium. Gdy dodajesz
własny przypadek użycia, odwzoruj te same pliki z własnymi nazwami.

## Zanim zaczniesz

1. **Kontekst i agregat.** Operacja używa pojęć jednego kontekstu i zmienia jeden agregat? Jeśli nie, wróć do
   [04 Wybór kontekstu](../04-wybor-kontekstu.md). Jeśli trzeba też zmienić inny agregat, zrób to zdarzeniem
   ([przepis 05](05-zdarzenia-i-cache.md)), nie w tej komendzie.
2. **Model zapisu.** Jeśli operacja wymaga nowego pola lub tabeli, najpierw [przepis 04](04-zmiana-modelu-i-migracja.md)
   (migracja). `RenameCategory` nie zmienia schematu.
3. **Scope.** Ustal, który scope z `{Serwis}Scopes` jest wymagany. Nowy scope: [przepis 06](06-uprawnienia.md).
4. **Kody błędów.** Wypisz reguły i błędy, które klient może zobaczyć: to będzie dokumentacja komendy i akcji.

## Szybki start: `dotnet superapp add usecase`

```bash
dotnet superapp add usecase Knowledge Categories RenameCategory --scope catalog.write
```

Tworzy komendę, walidator, handler i akcję `POST rename-category` w `CategoriesController` (kontroler powstaje, gdy go nie ma). Szkielet
kompiluje się od razu: dokumentacja ma `TODO`, a handler rzuca `NotImplementedException` do czasu napisania. Zostaje to, co opisują
kroki poniżej:

- reguła w agregacie (krok 1);
- parametry komendy, walidator i handler (kroki 2–4);
- ciało żądania, trasa i odpowiedzi akcji (kroki 5–6);
- testy i BFF.

Scope musi być zadeklarowany w `{Serwis}Scopes`; nowy dodaje `dotnet superapp add service {Serwis} --experience … --scope …`.
[22 Narzędzie](../22-narzedzie-superapp.md).

## Pliki, które powstają lub się zmieniają

```
src/Services/Knowledge/
  Knowledge.Domain/Categories/Category.cs                                            metoda Rename (reguły)
  Knowledge.Domain/Categories/CategoryErrors.cs                                      błąd NotFound (jeśli nowy)
  Knowledge.Application/Features/Categories/RenameCategory/RenameCategory.cs          komenda
  Knowledge.Application/Features/Categories/RenameCategory/RenameCategoryValidator.cs walidator
  Knowledge.Application/Features/Categories/RenameCategory/RenameCategoryHandler.cs   handler
  Knowledge.Api/Controllers/RenameCategoryRequest.cs                                  ciało żądania
  Knowledge.Api/Controllers/CategoriesController.cs                                   akcja
  Knowledge.Api/openapi/Knowledge.Api.json                                            regenerowany przy buildzie
  tests/Knowledge.Domain.Tests/CategoryTests.cs                                       reguła agregatu
  tests/Knowledge.Application.Tests/TextValidationTests.cs                            walidator zgodny z domeną
  tests/Knowledge.IntegrationTests/...                                                cały pipeline na MSSQL
src/Bff/Example.Bff/
  Clients/Knowledge/Generated/KnowledgeApi.cs                                         klient Refitter, regenerowany
  Controllers/Knowledge/KnowledgeCategoriesController.cs                              akcja przekazująca
  openapi/Example.Bff_public.json                                                     regenerowany przy buildzie BFF
```

Nie zmieniasz: rejestracji DI (handler i walidator rejestrują się same, klient Refit w BFF jest już zarejestrowany),
konfiguracji bramy (trasa `/api/example/v{version:int}/{**rest}` obejmuje całe API publiczne BFF), migracji (schemat bez zmian).

## Krok 1: reguła w agregacie

Najpierw domena. Metoda ma nazwę z języka domeny, sprawdza reguły **przed** zmianą stanu, zgłasza zdarzenie, jeśli ktoś na nie
reaguje, i zwraca `Result`. Czas, jeśli potrzebny, przychodzi parametrem `now`.

`Knowledge.Domain/Categories/Category.cs`, metoda `Rename`:

```csharp
    /// <summary>Changes the display name of the category and raises <see cref="CategoryChanged"/>; the slug stays unchanged.</summary>
    /// <remarks>
    /// Idempotent: when the trimmed name equals the current <see cref="Name"/> (ordinal, case-sensitive comparison) the call succeeds
    /// without changing anything and without raising an event, so the cached category list is not invalidated needlessly.
    /// A change of letter case only (<c>"sleep"</c> to <c>"Sleep"</c>) is a real rename.
    /// </remarks>
    /// <param name="name">New display name; trimmed, required, at most <see cref="MaxNameLength"/> characters.</param>
    /// <returns>Success, or the validation error <c>knowledge.category.invalid_name</c> for a blank or too long name.</returns>
    public Result Rename(string name)
    {
        var validName = Text.Required(name, MaxNameLength, "knowledge.category.invalid_name", "name");
        if (validName.IsFailure)
        {
            return validName.Error;
        }

        if (string.Equals(Name, validName.Value, StringComparison.Ordinal))
        {
            return Result.Success();
        }

        Name = validName.Value;
        Raise(new CategoryChanged(Id));
        return Result.Success();
    }
```

Decyzje do podjęcia przy własnej metodzie (szczegóły: [05, anatomia agregatu](../05-model-domeny.md#53-anatomia-agregatu)):

| Pytanie | `Rename` |
|---|---|
| Czy powtórzenie ma być sukcesem bez zmian (idempotencja)? | tak: ta sama nazwa nie zmienia stanu i nie zgłasza zdarzenia |
| Jakie zdarzenie i kto na nie reaguje? | `CategoryChanged` → `CategoryCacheInvalidation` (unieważnienie cache listy kategorii po commicie) |
| Jakie błędy, jaki typ? | `knowledge.category.invalid_name` (Validation, 400) |
| Co nie należy do agregatu? | istnienie kategorii (handler + repozytorium) |

Błąd wielokrotnego użytku dodaj do katalogu błędów agregatu. `Knowledge.Domain/Categories/CategoryErrors.cs`, pole `NotFound`:

```csharp
    /// <summary>
    /// <c>knowledge.category.not_found</c> (NotFound, HTTP 404): no category with the given ID exists (or the ID is empty).
    /// Returned by the rename-category handler.
    /// </summary>
    public static readonly Error NotFound = Error.NotFound("knowledge.category.not_found", "Kategoria nie istnieje.");
```

Kod błędu `{serwis}.{pojęcie}.{problem}` jest częścią kontraktu API: raz opublikowanego nie zmieniasz. Typ błędu wybierz z
tabeli w [05.11](../05-model-domeny.md#511-błędy-error-errortype-katalogi-agregaterrors).

## Krok 2: komenda

`Knowledge.Application/Features/Categories/RenameCategory/RenameCategory.cs` (cały plik):

```csharp
using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Messaging;

namespace Knowledge.Application.Features.Categories.RenameCategory;

/// <summary>
/// Changes the display name of an existing category; the slug stays the same. Requires scope <see cref="KnowledgeScopes.CatalogWrite"/>.
/// </summary>
/// <remarks>
/// <para>
/// Input rules (<c>RenameCategoryValidator</c>): <see cref="CategoryId"/> not empty; <see cref="Name"/> not blank, at most
/// <see cref="Knowledge.Domain.Categories.Category.MaxNameLength"/> characters after trimming.
/// </para>
/// <para>
/// The handler loads the category and calls <c>Category.Rename</c>, which trims the name and raises <c>CategoryChanged</c>
/// (invalidating the cached category list after the commit). Renaming to the current name (after trimming) is an idempotent success
/// that changes nothing. Names do not have to be unique.
/// </para>
/// <para>Result: success without a value. Possible errors:</para>
/// <list type="bullet">
///   <item><description><c>auth.unauthenticated</c> (403), <c>auth.missing_scope</c> (403).</description></item>
///   <item><description><c>validation.failed</c> (400): the input rules above.</description></item>
///   <item><description><see cref="Knowledge.Domain.Categories.CategoryErrors.NotFound"/> (<c>knowledge.category.not_found</c>, 404):
///   no category with this identifier.</description></item>
/// </list>
/// </remarks>
/// <param name="CategoryId">Identifier of the category to rename; must not be <see cref="Guid.Empty"/>.</param>
/// <param name="Name">New display name; not blank, at most <see cref="Knowledge.Domain.Categories.Category.MaxNameLength"/> characters after trimming. Stored trimmed.</param>
[RequiresScope(KnowledgeScopes.CatalogWrite)]
public sealed record RenameCategory(Guid CategoryId, string Name) : ICommand;
```

Reguły:

- Nazwa = przypadek użycia, bez sufiksu `Command`; `public sealed record` z parametrami pozycyjnymi.
- Tylko prymitywy (`Guid`, nie `CategoryId`); żadnego identyfikatora użytkownika (handler bierze go z tokenu).
- `ICommand` (wynik `Result`, HTTP 204) albo `ICommand<Result<T>>` (np. `Result<Guid>` z ID nowego zasobu, HTTP 201).
- `[RequiresScope(...)]` ze stałą z `KnowledgeScopes` (nigdy literał).
- Dokumentacja: co robi, scope, reguły wejścia, co robi handler, **wszystkie** kody błędów ze statusami, `<param>` każdego
  parametru (inaczej APP006).

## Krok 3: walidator

`Knowledge.Application/Features/Categories/RenameCategory/RenameCategoryValidator.cs` (cały plik):

```csharp
using FluentValidation;
using Knowledge.Application.Validation;
using Knowledge.Domain.Categories;

namespace Knowledge.Application.Features.Categories.RenameCategory;

/// <summary>
/// Input rules of <see cref="RenameCategory"/>: a non-empty identifier and a required name within <see cref="Category.MaxNameLength"/>.
/// </summary>
/// <remarks>
/// Runs in the validation behavior before the handler; failures become <c>validation.failed</c> (400). Text lengths are measured after trimming, exactly as the aggregate measures them
/// (<see cref="Knowledge.Application.Validation.TextRuleExtensions.MaximumTrimmedLength{T}"/>), so the validator accepts the same values as the aggregate.
/// </remarks>
internal sealed class RenameCategoryValidator : AbstractValidator<RenameCategory>
{
    /// <summary>Defines the rules.</summary>
    public RenameCategoryValidator()
    {
        RuleFor(command => command.CategoryId).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumTrimmedLength(Category.MaxNameLength);
    }
}
```

Walidator sprawdza tylko **kształt** wejścia, z **tymi samymi stałymi** i **tym samym przycinaniem** co agregat. Reguły
zależne od stanu (istnienie, archiwum, unikalność) zostają w handlerze i agregacie
([06.6 Walidatory](../06-warstwa-aplikacji.md#66-walidatory)). Komenda bez pól do sprawdzenia nie potrzebuje walidatora.

## Krok 4: handler

`Knowledge.Application/Features/Categories/RenameCategory/RenameCategoryHandler.cs` (cały plik):

```csharp
using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Domain.Results;
using Knowledge.Domain.Categories;

namespace Knowledge.Application.Features.Categories.RenameCategory;

/// <summary>
/// Handles <see cref="RenameCategory"/>: loads the category and delegates the change to <see cref="Category.Rename"/>.
/// </summary>
/// <remarks>An identifier that cannot be converted to <see cref="CategoryId"/> is reported as <see cref="CategoryErrors.NotFound"/>.</remarks>
internal sealed class RenameCategoryHandler(ICategoryRepository categories) : ICommandHandler<RenameCategory>
{
    /// <inheritdoc />
    public async Task<Result> Handle(RenameCategory command, CancellationToken cancellationToken)
    {
        if (!CategoryId.Create(command.CategoryId).TryGetValue(out var categoryId, out _))
        {
            return CategoryErrors.NotFound;
        }

        var category = await categories.GetAsync(categoryId, cancellationToken);
        return category is null ? CategoryErrors.NotFound : category.Rename(command.Name);
    }
}
```

Schemat każdego handlera komendy: (użytkownik z tokenu, jeśli potrzebny) → `Create` + `TryGetValue` dla ID i value objectów →
repozytorium → **jedno** wywołanie agregatu → zwrot `Result`. Handler **nie** wywołuje `SaveChangesAsync`: po sukcesie
`TransactionBehavior` zapisuje zmiany, wywołuje handlery zdarzeń domenowych (tu: unieważnienie cache) i robi commit;
po błędzie wszystko jest wycofywane ([06.4](../06-warstwa-aplikacji.md#64-pipeline-mediatr-krok-po-kroku)).

Gdy przypadek użycia dotyczy danych użytkownika, zacznij od:

```csharp
if (!currentUser.RequireUserId().TryGetValue(out var userId, out var userError))
{
    return userError;
}
```

Gdy potrzebny jest czas, wstrzyknij `IClock` i przekaż `clock.UtcNow` do agregatu (`material.Publish(clock.UtcNow)`).

## Krok 5: ciało żądania

`Knowledge.Api/Controllers/RenameCategoryRequest.cs` (cały plik):

```csharp
namespace Knowledge.Api.Controllers;

// Controllers are thin: HTTP <-> command/query through ISender, errors as ProblemDetails (ADR-0015).

/// <summary>Request body of <c>PUT /v1/categories/{categoryId}/name</c>: the new name of a category.</summary>
/// <param name="Name">The new name: required, at most 100 characters (trimmed). The slug of the category does not change.</param>
public sealed record RenameCategoryRequest(string Name);
```

Osobny rekord żądania jest potrzebny, bo `CategoryId` przychodzi z trasy, a dokumentacja rekordu trafia do kontraktu
OpenAPI, więc piszesz ją dla konsumenta API (liczby wprost, bez `<see cref>`). Używaj go także wtedy, gdy ciało pokrywa się
z komendą: dokumentacja komendy jest pisana dla programisty .NET i w kontrakcie wygląda źle
([15.9](../15-dokumentacja-w-kodzie.md#159-kontrolery-dokumentacja-trafia-do-openapi)).

## Krok 6: akcja kontrolera

`Knowledge.Api/Controllers/CategoriesController.cs`, nagłówek klasy (atrybuty wspólne) i akcja `Rename`:

```csharp
[ApiController]
[Route("v1/categories")]
[ProducesErrorResponseType(typeof(void))]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, MediaTypeNames.Application.ProblemJson)]
public sealed class CategoriesController(ISender sender) : ControllerBase
{
    // ...

    /// <summary>Renames a category; its slug stays unchanged.</summary>
    /// <remarks>
    /// Required scope: <c>knowledge.catalog.write</c> (editor). Name: required, at most 100 characters (trimmed).
    /// Renaming to the current name succeeds.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation of the HTTP request.</param>
    /// <param name="categoryId">Identifier of the category.</param>
    /// <param name="request">The new name of the category.</param>
    /// <returns>An empty response on success.</returns>
    /// <response code="204">The category was renamed.</response>
    /// <response code="400">Invalid name or an empty identifier (<c>validation.failed</c>, <c>knowledge.category.invalid_name</c>).</response>
    /// <response code="401">Missing, expired or invalid access token.</response>
    /// <response code="403">The token lacks the scope <c>knowledge.catalog.write</c> (<c>auth.missing_scope</c>).</response>
    /// <response code="404">The category does not exist (<c>knowledge.category.not_found</c>).</response>
    /// <response code="409">
    /// The category was changed by another request between reading and saving (<c>persistence.concurrency_conflict</c>);
    /// reload it and retry.
    /// </response>
    [HttpPut("{categoryId:guid}/name")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    public async Task<IActionResult> Rename(Guid categoryId, RenameCategoryRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await sender.Send(new RenameCategory(categoryId, request.Name), cancellationToken));
}
```

Zasady akcji:

- Jedno wyrażenie: zbuduj komendę, `sender.Send`, `this.ToActionResult(result)`. Bez `if`-ów, bez wyboru statusów, bez try/catch.
- `ToActionResult(Result)`: sukces → 204, błąd → `application/problem+json` ze statusem z `Error.Type` oraz `code` i `traceId`.
- `[ProducesResponseType]` dla **każdego** statusu, który akcja może zwrócić (akcja zwraca `IActionResult`, więc generator
  OpenAPI niczego sam nie wywnioskuje). 401 i 403 są zadeklarowane raz na klasie; opisujesz je w `<response>` każdej akcji.
- `<response code>` dla każdego statusu, z kodami błędów w `<c>`.
- `<param name="cancellationToken">` **przed** parametrem ciała: generator kopiuje do opisu ciała każdy `param`, który nie jest
  parametrem trasy ani query, więc wygrywa ostatni.
- Nie dodawaj `[Produces("application/json")]`: nadpisałby `application/problem+json` w odpowiedziach błędów.
- Trasa z ograniczeniem typu (`{categoryId:guid}`): niepoprawny GUID w URL-u daje 404 z routingu, zanim zadziała pipeline.

## Krok 7 (wariant): komenda tworząca zasób, odpowiedź 201

Komenda zwraca `Result<Guid>`, handler dodaje agregat do repozytorium i zwraca ID, akcja zamienia sukces na 201
z `CreatedResponse`. Handler `CreateCategoryHandler` (cały plik):

```csharp
using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Domain.Results;
using Knowledge.Domain.Categories;

namespace Knowledge.Application.Features.Categories.CreateCategory;

/// <summary>
/// Handles <see cref="CreateCategory"/>: checks slug uniqueness, creates the <see cref="Category"/> aggregate and adds it to the repository.
/// </summary>
/// <remarks>
/// Uniqueness is checked against saved categories only; the unique index on the slug column is the final guard when two requests race,
/// and the unit of work reports that violation as the same <see cref="CategoryErrors.SlugTaken"/> (the write context maps the index).
/// Saving happens in the transaction behavior after the handler returns success.
/// </remarks>
internal sealed class CreateCategoryHandler(ICategoryRepository categories) : ICommandHandler<CreateCategory, Result<Guid>>
{
    /// <inheritdoc />
    public async Task<Result<Guid>> Handle(CreateCategory command, CancellationToken cancellationToken)
    {
        if (await categories.SlugExistsAsync(command.Slug, cancellationToken))
        {
            return CategoryErrors.SlugTaken;
        }

        if (!Category.Create(command.Name, command.Slug).TryGetValue(out var category, out var error))
        {
            return error;
        }

        categories.Add(category);
        return category.Id.Value;
    }
}
```

Akcja:

```csharp
[HttpPost]
[ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created, MediaTypeNames.Application.Json)]
[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
public async Task<IActionResult> Create(CreateCategory command, CancellationToken cancellationToken) =>
    this.ToActionResult(await sender.Send(command, cancellationToken), id => StatusCode(StatusCodes.Status201Created, new CreatedResponse(id)));
```

Pełny tekst komendy `CreateCategory` z dokumentacją jest w [06.5](../06-warstwa-aplikacji.md#65-komendy). Uwaga: ta akcja
przyjmuje komendę bezpośrednio jako ciało (starszy wzorzec); nowe endpointy rób z osobnym rekordem żądania jak w kroku 5.
Gdy tworzony zasób ma unikalny klucz, dodaj indeks do mapy `UniqueConstraintErrors` kontekstu zapisu, żeby wyścig dał ten sam
błąd co sprawdzenie w handlerze ([07 Dane i EF Core](../07-dane-i-ef-core.md)).

## Krok 8: testy

### Domena: reguła agregatu, bez mocków

`src/Services/Knowledge/tests/Knowledge.Domain.Tests/CategoryTests.cs` (fragment z repozytorium):

```csharp
[Fact]
public void Renaming_to_the_current_name_changes_nothing_and_raises_no_event()
{
    var category = ResultAssert.Success(Category.Create("Sen", "sen"));
    category.ClearDomainEvents();

    Assert.True(category.Rename("Sen").IsSuccess);
    Assert.True(category.Rename("  Sen  ").IsSuccess);

    Assert.Equal("Sen", category.Name);
    Assert.Empty(category.DomainEvents);
}

[Fact]
public void Renaming_to_a_different_name_raises_event()
{
    var category = ResultAssert.Success(Category.Create("Sen", "sen"));
    category.ClearDomainEvents();

    Assert.True(category.Rename("Zdrowy sen").IsSuccess);

    Assert.Equal("Zdrowy sen", category.Name);
    Assert.Equal(category.Id, Assert.Single(category.DomainEvents.OfType<CategoryChanged>()).CategoryId);
}
```

Testuj: sukces ze zmianą stanu i zdarzeniem, każdy kod błędu, idempotencję, brak zmian po błędzie.

### Application: walidator zgodny z domeną, handler z fake'ami

`src/Services/Knowledge/tests/Knowledge.Application.Tests/TextValidationTests.cs` (fragment z repozytorium):

```csharp
[Fact]
public void Category_name_follows_the_category_limit_after_trimming()
{
    Assert.True(new CreateCategoryValidator().Validate(new CreateCategory(Padded(Category.MaxNameLength), "sen")).IsValid);
    Assert.True(new RenameCategoryValidator().Validate(new RenameCategory(Guid.NewGuid(), Padded(Category.MaxNameLength))).IsValid);
    Assert.False(new RenameCategoryValidator().Validate(new RenameCategory(Guid.NewGuid(), Padded(Category.MaxNameLength + 1))).IsValid);
}
```

Handler testuje się z fake'ami portów z katalogu `Fakes` (`FakeCategoryRepository`, `FakeCurrentUser`, `FakeClock`).
Przykład testu, który warto dopisać dla własnego handlera (wzór z `LibraryHandlerTests`; tego testu nie ma w repozytorium):

```csharp
[Fact]
public async Task Renaming_unknown_category_returns_not_found()
{
    var handler = new RenameCategoryHandler(new FakeCategoryRepository());

    var result = await handler.Handle(new RenameCategory(Guid.NewGuid(), "Sen"), TestContext.Current.CancellationToken);

    Assert.Equal(CategoryErrors.NotFound, result.Error);
}
```

### Integracja: cały pipeline na prawdziwym MSSQL

Testy w `Knowledge.IntegrationTests` używają `ServiceFixture` (MSSQL w Testcontainers, migracje, prawdziwy pipeline bez
MassTransit) i muszą mieć `[Collection(PipelineCollection.Name)]`, bo współdzielą użytkownika i cache fixture'a.
Przykład w stylu istniejących testów (tego testu nie ma w repozytorium):

```csharp
[Collection(PipelineCollection.Name)]
public sealed class RenameCategoryTests(ServiceFixture fixture)
{
    [Fact]
    public async Task Editor_renames_category_and_reader_sees_new_name()
    {
        fixture.ActWith(KnowledgeScopes.CatalogWrite);
        var created = ResultAssert.Success(await fixture.SendAsync(new CreateCategory("Sen", $"sen-{Guid.NewGuid():N}")));

        var renamed = await fixture.SendAsync(new RenameCategory(created, "Zdrowy sen"));

        fixture.ActWith(KnowledgeScopes.CatalogRead);
        var categories = await fixture.SendAsync(new ListCategories());

        Assert.True(renamed.IsSuccess);
        Assert.Contains(ResultAssert.Success(categories), category => category.Id == created && category.Name == "Zdrowy sen");
    }

    [Fact]
    public async Task Reader_cannot_rename_category()
    {
        fixture.ActWith(KnowledgeScopes.CatalogRead);

        var result = await fixture.SendAsync(new RenameCategory(Guid.NewGuid(), "Zdrowy sen"));

        Assert.Equal("auth.missing_scope", result.Error.Code);
    }
}
```

Unikalne wartości (`$"sen-{Guid.NewGuid():N}"`) są konieczne, bo baza fixture'a jest wspólna dla wszystkich testów.
Drugi test pokazuje też, że scope jest sprawdzany **przed** walidacją i handlerem.

## Krok 9: wystaw operację w BFF experience

Kontrakt serwisu jest wewnętrzny experience; jego konsumentem jest BFF (ADR-0039). Moduł zobaczy operację dopiero w kontrakcie
publicznym BFF.

1. **Zbuduj serwis**, żeby zregenerować jego kontrakt (`Knowledge.Api/openapi/Knowledge.Api.json`). Nowa operacja ma tam
   `operationId` w postaci `{Controller}_{Action}` (`Categories_Rename`, `OpenApiOperationIdTransformer`); od niego zależy nazwa
   metody klienta.
2. **Zregeneruj klienta Refitter w BFF** z kontraktu serwisu (ustawienia w pliku `.refitter` obok katalogu `Generated`):

   ```bash
   dotnet tool restore
   dotnet refitter --settings-file src/Bff/Example.Bff/Clients/Knowledge/knowledge.refitter
   git diff src/Bff/Example.Bff/Clients/Knowledge/Generated/
   ```

   W diffie pojawia się metoda `CategoriesRenameAsync` (szablon nazwy `{operationName}Async`) i typ ciała `RenameCategoryRequest`
   (typy `public`, bo są częścią kontraktu BFF). Wygenerowanego kodu nie edytujesz ręcznie.
3. **Dodaj akcję w kontrolerze BFF.** Kontrolery fasady odpowiadają kontrolerom serwisu, z nazwą serwisu w nazwie klasy
   (`CategoriesController` serwisu → `KnowledgeCategoriesController` BFF), a ścieżka BFF to ścieżka serwisu z segmentem serwisu
   (`/v1/categories/...` → `/v1/knowledge/categories/...`). Istniejące akcje fasady wygenerowano jednorazowo z kontraktów serwisów;
   skryptu nie ma w repozytorium, więc nową akcję dopisujesz ręcznie wzorem sąsiednich.
   `src/Bff/Example.Bff/Controllers/Knowledge/KnowledgeCategoriesController.cs`, akcja `Rename`:

   ```csharp
       /// <summary>Renames a category; its slug stays unchanged.</summary>
       /// <remarks>
       /// Required scope: <c>knowledge.catalog.write</c> (editor). Name: required, at most 100 characters (trimmed).
       /// Renaming to the current name succeeds.
       /// </remarks>
       /// <param name="cancellationToken">Cancellation of the HTTP request.</param>
       /// <param name="categoryId">Identifier of the category.</param>
       /// <param name="body">The new name of the category.</param>
       /// <returns>The service's answer, relayed unchanged.</returns>
       /// <response code="204">The category was renamed.</response>
       /// <response code="400">Invalid name or an empty identifier (<c>validation.failed</c>, <c>knowledge.category.invalid_name</c>).</response>
       /// <response code="401">Missing, expired or invalid access token.</response>
       /// <response code="403">The token lacks the scope <c>knowledge.catalog.write</c> (<c>auth.missing_scope</c>).</response>
       /// <response code="404">The category does not exist (<c>knowledge.category.not_found</c>).</response>
       /// <response code="409">
       /// The category was changed by another request between reading and saving (<c>persistence.concurrency_conflict</c>);
       /// reload it and retry.
       /// </response>
       [HttpPut("v1/knowledge/categories/{categoryId:guid}/name")]
       [ProducesResponseType(StatusCodes.Status204NoContent)]
       [ProducesResponseType<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
       [ProducesResponseType(StatusCodes.Status401Unauthorized)]
       [ProducesResponseType<Microsoft.AspNetCore.Mvc.ProblemDetails>(StatusCodes.Status403Forbidden, MediaTypeNames.Application.ProblemJson)]
       [ProducesResponseType<Microsoft.AspNetCore.Mvc.ProblemDetails>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
       [ProducesResponseType<Microsoft.AspNetCore.Mvc.ProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
       public async Task<IActionResult> Rename(System.Guid categoryId, [FromBody] RenameCategoryRequest body, CancellationToken cancellationToken) =>
           this.ToActionResult(await knowledge.CategoriesRenameAsync(categoryId, body, cancellationToken));
   ```

   Zasady akcji BFF:

   - Jedno wyrażenie: `this.ToActionResult(await client.XAsync(...))` (`DownstreamResponseExtensions`). Odpowiedź serwisu, także
     błąd ze statusem, `code` i `traceId`, idzie do modułu bez zmian. Niedostępny serwis daje `503 downstream.unavailable`, a
     przekroczony czas `504 downstream.timeout` (`DownstreamUnavailableExceptionHandler`).
   - Dokumentacja i `[ProducesResponseType]` powtarzają opis operacji z kontraktu serwisu (scope, reguły, **wszystkie** kody
     błędów), bo moduł czyta tylko kontrakt BFF. `<returns>` mówi, że odpowiedź jest przekazywana bez zmian. 401 deklarujesz w
     każdej akcji (kontrolery BFF nie mają atrybutów wspólnych na klasie).
   - Bez walidacji w BFF: walidatory atrybutów są wyłączone (`ModelValidatorProviders.Clear()`), więc ciało idzie do serwisu, a 400 z kodem zwraca serwis; BFF odrzuca sam tylko wejście niemożliwe do powiązania.
   - Bez scope w BFF: API publiczne nie ma własnych polityk; scope i reguły zasobu sprawdza serwis na podstawie przekazanego tokenu
     (ADR-0040).
   - Typy ciała i odpowiedzi to typy wygenerowane z kontraktu serwisu (`Example.Bff.Clients.Knowledge`); akcja przekazująca nie ma
     własnych DTO. Własny kształt danych mają tylko endpointy komponowane (`ExperienceSummaryController`).
4. **Zbuduj BFF.** Build regeneruje kontrakt publiczny `src/Bff/Example.Bff/openapi/Example.Bff_public.json`. W diffie sprawdź, że
   operacja trafiła do kontraktu publicznego, a nie do `Example.Bff_internal.json` (podział po prefiksie ścieżki `internal/`). Moduł
   generuje klienta z kontraktu publicznego BFF ([08 API i kontrakty](../08-api-i-kontrakty.md)).

## Krok 10: weryfikacja

```bash
dotnet build SuperApp.slnx                       # analizatory APP001-APP006, CS1591, regeneracja kontraktów serwisu i BFF
dotnet test --project src/Services/Knowledge/tests/Knowledge.Domain.Tests
dotnet test --project src/Services/Knowledge/tests/Knowledge.Application.Tests
dotnet test --solution SuperApp.slnx             # wszystko, łącznie z testami architektury i integracyjnymi (Docker)
dotnet test --project src/Bff/Example.Bff.Tests    # m.in. podział kontraktów BFF, operationId i security każdej operacji
git diff src/Services/Knowledge/Knowledge.Api/openapi/ src/Bff/Example.Bff/openapi/
```

W diffie kontraktu sprawdź: nowa operacja jest, ma `summary`, `description`, opis ciała (nie „Cancellation of the HTTP
request.”), każdy status z opisem i właściwym schematem (`ProblemDetails`/`ValidationProblemDetails`). Nowa operacja jest
zmianą wstecznie zgodną; zmiana istniejącej musi taka pozostać (ADR-0019, [08](../08-api-i-kontrakty.md)).

Ręczne sprawdzenie na lokalnym środowisku (token z [01 Start](../01-start.md#14-pierwsze-żądania)). Wywołania bezpośrednio na port
serwisu (5101) służą tylko do debugowania: w klastrze serwis domenowy nie jest osiągalny spoza experience (ADR-0041).

```bash
TOKEN=$(curl -s http://localhost:8081/realms/superapp/protocol/openid-connect/token \
  -d grant_type=password -d client_id=dev-cli -d username=editor -d password=editor \
  -d "scope=openid knowledge.catalog.read knowledge.catalog.write" | jq -r .access_token)

ID=$(curl -s -X POST http://localhost:5101/v1/categories -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"name":"Sen","slug":"sen-demo"}' | jq -r .id)

curl -i -X PUT http://localhost:5101/v1/categories/$ID/name -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"name":"Zdrowy sen"}'
# HTTP/1.1 204 No Content

curl -s -X PUT http://localhost:5101/v1/categories/0192a3c4-5d6e-7f80-9a1b-2c3d4e5f6a7b/name -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"name":"Zdrowy sen"}'
# {"title":"Kategoria nie istnieje.","status":404,"instance":"/v1/categories/0192a3c4-.../name",
#  "code":"knowledge.category.not_found","traceId":"..."}
```

Bezpośrednio przez BFF experience (port 5120, ten sam token: `dev-cli` ma domyślnie audience `example-bff`):

```bash
curl -i -X PUT http://localhost:5120/v1/knowledge/categories/$ID/name -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"name":"Zdrowy sen"}'
# HTTP/1.1 204 No Content (odpowiedź serwisu przekazana bez zmian)
```

Przez bramę, tak jak woła moduł (przeglądarka zalogowana jako `editor`), ta sama operacja to
`PUT https://localhost:5001/api/example/v1/knowledge/categories/{id}/name` z ciasteczkiem sesji i nagłówkiem `X-CSRF: 1`. Brama
usuwa prefiks `/api/example` i dołącza token, BFF dostaje `/v1/knowledge/categories/{id}/name` i woła serwis
`/v1/categories/{id}/name` z tym samym tokenem.

Na koniec [checklista przed pull requestem](../18-checklista.md).

## Typowe błędy

| Objaw | Przyczyna | Naprawa |
|---|---|---|
| 204, ale zmiana nie zapisana | handler zwrócił wynik metody, która nic nie zmieniła (idempotencja), albo zmieniasz obiekt niezaładowany przez repozytorium | sprawdź test domeny; agregat ładuj przez repozytorium (śledzenie EF) |
| 500 zamiast kodu błędu | reguła rzuca wyjątek, albo duplikat na indeksie bez mapowania | `return XxxErrors.Y;`; indeks w `UniqueConstraintErrors` |
| 403 `auth.missing_scope` dla poprawnego żądania | token bez scope komendy | poproś o scope w tokenie (lokalnie: `-d "scope=..."`), sprawdź `[RequiresScope]` |
| 400 `validation.failed` dla nazwy, którą domena przyjmuje | walidator mierzy bez przycinania albo ma inną stałą | `MaximumTrimmedLength(Category.MaxNameLength)` |
| APP001 w handlerze | wynik agregatu nie jest zwrócony | `return category.Rename(command.Name);` |
| APP006 przy komendzie | brak `<param>` dla parametru rekordu | `<param name="Name">` na typie |
| Test architektury: handler w złej warstwie / nie `internal sealed` | handler `public` albo w Api | `internal sealed` w Application |
| W kontrakcie opis ciała to „Cancellation of the HTTP request.” | `<param name="cancellationToken">` po parametrze ciała | przenieś przed parametr ciała |
| W kontrakcie brak statusu albo pusta odpowiedź 200 | brak `[ProducesResponseType]` | dodaj atrybut dla każdego statusu |
| Testy integracyjne „losowo” czerwone | brak `[Collection(PipelineCollection.Name)]` albo nieunikalne dane | dodaj kolekcję; unikalne slugi i tytuły |
| 404 z bramy dla nowej operacji, a bezpośrednio na 5101 działa | operacja nie jest wystawiona w BFF | krok 9: klient Refitter, akcja w kontrolerze BFF |
| Błąd kompilacji BFF: brak metody `CategoriesRenameAsync` | klient Refitter wygenerowany ze starego kontraktu | zbuduj serwis, potem `dotnet refitter --settings-file ...` |
| 503 `downstream.unavailable` z BFF | serwis nie działa albo zły `Downstream:Knowledge:BaseAddress` | uruchom serwis; sprawdź adres w `appsettings.Development.json` albo w compose |
