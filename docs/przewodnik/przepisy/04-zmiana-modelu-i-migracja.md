# Przepis 04: zmiana modelu i migracja

**Kiedy:** nowe pole w istniejącym agregacie, zmiana długości lub typu kolumny, nowy indeks lub ograniczenie, usunięcie lub
zmiana nazwy pola, zmiana wartości enuma zapisywanego w bazie. Każda zmiana modelu zapisu to **nowa migracja**
`{Serwis}WriteDbContext` w stylu expand/contract.

**Przykład główny:** opcjonalny opis kategorii (`Category.Description`, do 500 znaków) w Knowledge, od domeny do API.
**Przykład drugi:** usunięcie kolumny w dwóch wydaniach (expand, potem contract).

Mechanizmy w tle (konteksty, konwencje, przegląd migracji, Migrator, skrypty DBA): [07 Dane i EF Core](../07-dane-i-ef-core.md),
sekcja [7.10](../07-dane-i-ef-core.md#710-migracje). Nowy agregat i nowa tabela: [przepis 03](03-nowy-zbior-danych.md).

W blokach kodu pomijamy komentarze dokumentacji XML (`///`); w Twoim kodzie są obowiązkowe dla typów i składowych publicznych.

## Zanim zaczniesz: jaka to zmiana?

Podczas rolling update stare i nowe pody działają **na tym samym schemacie**, a na prod DBA stosuje migracje **przed**
wdrożeniem aplikacji. Poprzednia wersja aplikacji musi więc działać na nowym schemacie.

| Zmiana | Faza | Co wygeneruje EF | Pułapka |
|---|---|---|---|
| Nowa kolumna nullable | expand | `AddColumn(..., nullable: true)` | brak |
| Nowa kolumna wymagana | expand z `defaultValue` (albo nullable + backfill + contract `NOT NULL`) | `AddColumn(..., nullable: false, defaultValue: ...)` | bez wartości domyślnej `ALTER TABLE` nie przejdzie na tabeli z danymi, a stara wersja nie poda wartości przy `INSERT` |
| Dłuższa kolumna (`HasMaxLength` w górę) | expand | `AlterColumn` | indeks na kolumnie jest przebudowywany: duża tabela = długa operacja |
| Krótsza kolumna, zmiana typu | contract (po sprawdzeniu danych) | `AlterColumn` + ostrzeżenie „may result in the loss of data” | dane dłuższe niż nowy limit: migracja padnie albo utnie dane |
| Nowy indeks | expand | `CreateIndex` | unikalny indeks na istniejących duplikatach padnie; najpierw sprawdź dane |
| Nowy `CHECK` | expand, jeśli istniejące dane go spełniają | `AddCheckConstraint` | `ALTER TABLE ADD CONSTRAINT` sprawdza istniejące wiersze |
| Zmiana nazwy właściwości | expand/contract w kilku wydaniach albo `HasColumnName("StaraNazwa")` | `DropColumn` + `AddColumn` | **utrata danych**; EF nie rozpoznaje zmiany nazwy |
| Zmiana nazwy wartości enuma (zapisywanego jako tekst) | migracja danych + kod czytający obie nazwy | nic (model się nie zmienia) | stare wiersze z dawną nazwą: `Enum.Parse` w read modelu rzuci wyjątek |
| Usunięcie pola | contract w osobnym wydaniu | `DropColumn` | stara wersja w trakcie rolloutu dalej czyta/pisze kolumnę |
| Zmiana nazwy unikalnego indeksu | expand | `RenameIndex` | zaktualizuj `UniqueConstraintErrors` w tym samym PR |

## Część A: nowe pole `Category.Description`

### Krok 1: domena

`Knowledge.Domain/Categories/Category.cs` (dopisane składowe; reszta klasy bez zmian)
```csharp
public const int MaxDescriptionLength = 500;

public string? Description { get; private set; }

public Result Describe(string? description)
{
    var validDescription = Text.Optional(description, MaxDescriptionLength, "knowledge.category.invalid_description", "description");
    if (validDescription.IsFailure)
    {
        return validDescription.Error;
    }

    if (string.Equals(Description, validDescription.Value, StringComparison.Ordinal))
    {
        return Result.Success();
    }

    Description = validDescription.Value;
    Raise(new CategoryChanged(Id));
    return Result.Success();
}
```

- Limit jako stała domeny: konfiguracja EF i walidator użyją tej samej stałej.
- `Text.Optional` (`Knowledge.Domain/Common/Text.cs`) przycina tekst, pusty zamienia na `null`, a za długi zwraca błąd walidacji
  z podanym kodem. Wzór: `Material.UpdateDetails`.
- Brak zmiany = sukces bez zdarzenia (idempotencja, brak zbędnego unieważnienia cache).
- `CategoryChanged` już istnieje i unieważnia cache listy kategorii po commicie (`CategoryCacheInvalidation`).
- Nowa właściwość ma prywatny setter, a istniejące wiersze będą miały `NULL`: domena musi to akceptować (`string?`). Gdyby pole
  było wymagane, dawne wiersze i tak przyszłyby z bazy z wartością domyślną z migracji, więc ją przemyśl.

### Krok 2: konfiguracja EF

`Knowledge.Infrastructure/Persistence/Write/Configurations/CategoryConfiguration.cs` (po zmianie)
```csharp
internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories");
        builder.HasKey(category => category.Id);
        builder.Property(category => category.Id).ValueGeneratedNever();
        builder.Property(category => category.Name).HasMaxLength(Category.MaxNameLength);
        builder.Property(category => category.Slug).HasMaxLength(Category.MaxSlugLength);
        builder.Property(category => category.Description).HasMaxLength(Category.MaxDescriptionLength);
        builder.HasIndex(category => category.Slug).IsUnique();
        builder.Property<byte[]>("Version").IsRowVersion();
        builder.Ignore(category => category.DomainEvents);
    }
}
```

Bez `HasMaxLength` kolumna byłaby `nvarchar(max)`: działa, ale nie pilnuje limitu w bazie i nie da się jej zaindeksować.
Uzupełnij też listę w dokumentacji XML konfiguracji.

### Krok 3: migracja

Najprościej narzędziem ([22](../22-narzedzie-superapp.md)). Polecenie buduje projekt, uruchamia `dotnet ef` z właściwym kontekstem,
nadaje plikowi nazwę `AddCategoryDescription.cs` i dodaje opis do uzupełnienia:

```bash
dotnet superapp migration add Knowledge AddCategoryDescription
dotnet superapp migration list Knowledge
```

Ręcznie:

```bash
dotnet build SuperApp.slnx
dotnet ef migrations add AddCategoryDescription \
  -p src/Services/Knowledge/Knowledge.Infrastructure -s src/Services/Knowledge/Knowledge.Infrastructure \
  --context KnowledgeWriteDbContext -o Migrations
# Build started...
# Build succeeded.
# Done. To undo this action, use 'ef migrations remove'
```

Zmiany w `Knowledge.Infrastructure/Migrations/`: nowy `{data}_AddCategoryDescription.cs`, nowy
`{data}_AddCategoryDescription.Designer.cs`, zmieniony `KnowledgeWriteDbContextModelSnapshot.cs` (nowa właściwość `Description`).

1. **Zmień nazwę** `{data}_AddCategoryDescription.cs` na `AddCategoryDescription.cs` (konwencja ADR-0032; APP005 nie sprawdza migracji). `.Designer.cs` zostaw z datą.
2. **Dopisz dokumentację** na klasie: co zmienia, która to faza, co zostaje na później.
3. **Przeczytaj `Up` i `Down`.** Oczekiwany plik po obu krokach:

`Knowledge.Infrastructure/Migrations/AddCategoryDescription.cs`
```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Knowledge.Infrastructure.Migrations
{
    /// <summary>
    /// Expand step: adds the optional column <c>knowledge.Categories.Description</c> (<c>nvarchar(500) NULL</c>) for
    /// <c>Category.Describe</c>.
    /// </summary>
    /// <remarks>
    /// The column is nullable, so the previous version of the application, which does not know it, keeps inserting and updating
    /// categories during a rolling update. Existing categories get no description. No contract step is needed.
    /// </remarks>
    public partial class AddCategoryDescription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "knowledge",
                table: "Categories",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                schema: "knowledge",
                table: "Categories");
        }
    }
}
```

   Lista kontrolna przeglądu:
   - jedna operacja na właściwej tabeli w schemacie `knowledge`, `nullable: true`, `nvarchar(500)`;
   - brak ostrzeżenia „An operation was scaffolded that may result in the loss of data”;
   - brak zmian w innych tabelach (jeśli są, ktoś zmienił model bez migracji: wyjaśnij, zanim pójdziesz dalej);
   - `Down` usuwa kolumnę (traci opisy; dla migracji expand to akceptowalne, ale napisz to w `remarks`, jeśli dane są cenne).
4. **Sprawdź snapshot:**

```bash
dotnet ef migrations has-pending-model-changes \
  -p src/Services/Knowledge/Knowledge.Infrastructure -s src/Services/Knowledge/Knowledge.Infrastructure \
  --context KnowledgeWriteDbContext
# No changes have been made to the model since the last migration.
```

5. **Obejrzyj SQL**, który dostanie DBA (od poprzedniej migracji do nowej):

```bash
dotnet ef migrations script MassTransit8OutboxModel AddCategoryDescription \
  -p src/Services/Knowledge/Knowledge.Infrastructure -s src/Services/Knowledge/Knowledge.Infrastructure \
  --context KnowledgeWriteDbContext
# (fragment) ALTER TABLE [knowledge].[Categories] ADD [Description] nvarchar(500) NULL;
# INSERT INTO [knowledge].[__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'..._AddCategoryDescription', N'10.0.12');
```

   (Pierwszy argument to ostatnia migracja przed Twoją; listę daje `dotnet ef migrations list ... --connection "..."`.)
6. **Zastosuj lokalnie** i sprawdź:

```bash
dotnet run --project src/Migrator/SuperApp.Migrator --launch-profile local
```

```sql
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'knowledge' AND TABLE_NAME = 'Categories';
```

   API uruchomione przed migracją zwraca `503` na `/health/startup`; po migracji `200`.

### Krok 4: odczyt i cache

`Knowledge.Infrastructure/Persistence/Read/Models/CategoryRow.cs`
```csharp
namespace Knowledge.Infrastructure.Persistence.Read.Models;

internal sealed class CategoryRow
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Slug { get; init; } = string.Empty;

    public string? Description { get; init; }
}
```

`Knowledge.Application/Features/Categories/ListCategories/CategoryDto.cs`: nowe pole **na końcu** rekordu
```csharp
public sealed record CategoryDto(Guid Id, string Name, string Slug, string? Description);
```

`Knowledge.Infrastructure/Features/Categories/ListCategoriesHandler.cs`: projekcja
```csharp
.Select(category => new CategoryDto(category.Id, category.Name, category.Slug, category.Description))
```

`Knowledge.Infrastructure/Caching/KnowledgeCache.cs`: **nowa wersja klucza**, bo zmienia się kształt zapisanego DTO
```csharp
public const string CategoriesKey = "knowledge:categories:v2";
```

Dlaczego wersja klucza: lista kategorii leży w Redis (L2) jako JSON. Bez zmiany klucza nowe pody w trakcie rolloutu czytałyby
wpis zapisany przez stare pody (bez `Description`) i pokazywały brak opisów aż do wygaśnięcia wpisu (do godziny w trybie
fail-safe). Tag (`knowledge:categories`) nie ma wersji, więc unieważnienie dalej usuwa wpisy wszystkich wersji (dokumentacja
`KnowledgeCache`, [11 Cache](../11-cache.md)).

Kontekst odczytu nie ma migracji: jeśli zapomnisz o `CategoryRow.Description`, nic się nie zepsuje, po prostu pole nie będzie
czytane. Odwrotnie, właściwość w read modelu bez kolumny w bazie daje błąd SQL `Invalid column name` w czasie działania:
read model zmieniaj **po** migracji, która tworzy kolumnę, i nigdy przed nią.

### Krok 5: komenda i endpoint

Pełny wzorzec komendy: [przepis 01](01-endpoint-komendy.md). Pliki dla tej zmiany:

`Knowledge.Application/Features/Categories/DescribeCategory/DescribeCategory.cs`
```csharp
using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Application.Security;

namespace Knowledge.Application.Features.Categories.DescribeCategory;

[RequiresScope(KnowledgeScopes.CatalogWrite)]
public sealed record DescribeCategory(Guid CategoryId, string? Description) : ICommand;
```

`Knowledge.Application/Features/Categories/DescribeCategory/DescribeCategoryValidator.cs`
```csharp
using FluentValidation;
using Knowledge.Application.Validation;
using Knowledge.Domain.Categories;

namespace Knowledge.Application.Features.Categories.DescribeCategory;

internal sealed class DescribeCategoryValidator : AbstractValidator<DescribeCategory>
{
    public DescribeCategoryValidator()
    {
        RuleFor(command => command.CategoryId).NotEmpty();
        RuleFor(command => command.Description).MaximumTrimmedLength(Category.MaxDescriptionLength);
    }
}
```

`Knowledge.Application/Features/Categories/DescribeCategory/DescribeCategoryHandler.cs`
```csharp
using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Domain.Results;
using Knowledge.Domain.Categories;

namespace Knowledge.Application.Features.Categories.DescribeCategory;

internal sealed class DescribeCategoryHandler(ICategoryRepository categories) : ICommandHandler<DescribeCategory>
{
    public async Task<Result> Handle(DescribeCategory command, CancellationToken cancellationToken)
    {
        if (!CategoryId.Create(command.CategoryId).TryGetValue(out var categoryId, out _))
        {
            return CategoryErrors.NotFound;
        }

        var category = await categories.GetAsync(categoryId, cancellationToken);
        return category is null ? CategoryErrors.NotFound : category.Describe(command.Description);
    }
}
```

`Knowledge.Api/Controllers/DescribeCategoryRequest.cs`
```csharp
namespace Knowledge.Api.Controllers;

public sealed record DescribeCategoryRequest(string? Description);
```

`Knowledge.Api/Controllers/CategoriesController.cs` (nowa akcja, wzór: `Rename`)
```csharp
[HttpPut("{categoryId:guid}/description")]
[ProducesResponseType(StatusCodes.Status204NoContent)]
[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
public async Task<IActionResult> Describe(Guid categoryId, DescribeCategoryRequest request, CancellationToken cancellationToken) =>
    this.ToActionResult(await sender.Send(new DescribeCategory(categoryId, request.Description), cancellationToken));
```

Handler wzorem `RenameCategoryHandler`: ładuje agregat, woła jedną metodę, zwraca jej `Result`. Zapis i commit robi
`TransactionBehavior`; `rowversion` na `Categories` chroni przed nadpisaniem zmiany wykonanej między odczytem a zapisem
(konflikt to 409 `persistence.concurrency_conflict`, stąd `[ProducesResponseType]` dla 409 jak w `Rename`).

### Krok 6: kontrakt

`dotnet build` regeneruje `Knowledge.Api/openapi/Knowledge.Api.json`. W diffie oczekujesz: nowa operacja
`PUT /v1/categories/{categoryId}/description`, nowy schemat `DescribeCategoryRequest`, w `CategoryDto` nowa właściwość
`description` (nullable). To zmiany **wstecznie zgodne**: istniejący klienci ignorują nowe pole. Usunięcie albo zmiana nazwy
pola w DTO jest zmianą łamiącą: wymaga nowej wersji API (`/v2/...`) albo okresu z oboma polami ([08 API i kontrakty](../08-api-i-kontrakty.md)).

Kontrakt serwisu czyta BFF experience. Po zmianie zregeneruj klienta (`dotnet refitter --settings-file
src/Bff/Example.Bff/Clients/Knowledge/knowledge.refitter`): nowe pole `CategoryDto` trafi wtedy do odpowiedzi BFF i do
`openapi/Example.Bff_public.json`, a nową operację wystawiasz akcją w BFF ([przepis 01, krok 9](01-endpoint-komendy.md)). Bez
regeneracji BFF nadal przekazuje odpowiedź w starym kształcie: deserializuje ją klientem wygenerowanym ze starego kontraktu.

### Krok 7: testy

| Poziom | Co dopisać |
|---|---|
| Domain (`CategoryTests`) | `Describe` przycina i zapisuje; pusty tekst = `null`; 501 znaków = `knowledge.category.invalid_description`; ten sam opis = brak zdarzenia |
| Application | handler: nieistniejąca kategoria → `CategoryErrors.NotFound`; walidator: za długi opis |
| IntegrationTests | `ListCategories` po `DescribeCategory` zwraca opis (też po wcześniejszym zapełnieniu cache: unieważnienie po commicie) |

Przykład testu domeny w stylu `CategoryTests`:

```csharp
[Fact]
public void Describing_with_the_same_text_changes_nothing_and_raises_no_event()
{
    var category = ResultAssert.Success(Category.Create("Sen", "sen"));
    Assert.True(category.Describe("Higiena snu").IsSuccess);
    category.ClearDomainEvents();

    Assert.True(category.Describe("  Higiena snu  ").IsSuccess);

    Assert.Equal("Higiena snu", category.Description);
    Assert.Empty(category.DomainEvents);
}
```

Testy integracyjne budują bazę z migracji (`ServiceFixture` → `MigrateAsync`), więc błędna albo brakująca migracja wychodzi
w `dotnet test`. EF Core 10 przy `MigrateAsync` zgłasza też błąd, gdy model ma zmiany bez migracji.

```bash
dotnet build SuperApp.slnx
dotnet test --solution SuperApp.slnx
```

## Część B: usunięcie kolumny w dwóch wydaniach (expand → contract)

Załóżmy, że po pewnym czasie opis kategorii ma zniknąć. Usunięcie właściwości z domeny i konfiguracji sprawi, że
`migrations add` wygeneruje `DropColumn`. Gdyby trafiło to do jednego wydania z kodem, to w trakcie rolloutu (a na prod od
chwili uruchomienia skryptu DBA aż do końca wdrożenia) stara wersja aplikacji wykonywałaby zapytania z kolumną `Description`,
której już nie ma: błędy `Invalid column name 'Description'` na listach i zapisach.

**Wydanie N (expand: kod przestaje używać kolumny, kolumna zostaje):**

1. Usuń `Description` i `Describe` z domeny, `HasMaxLength` z konfiguracji, pole z `CategoryRow`, DTO (to zmiana łamiąca
   kontraktu: najpierw okres przejściowy albo nowa wersja API), endpoint, klucz cache podbij do kolejnej wersji.
2. `dotnet ef migrations add StopUsingCategoryDescription ...`
3. EF wygeneruje `DropColumn` i ostrzeżenie o utracie danych. **Usuń to wywołanie z `Up` i odpowiadające mu z `Down`**
   (zostaw pustą migrację albo tylko inne, bezpieczne zmiany). Snapshot już nie zawiera kolumny i tak ma zostać.
4. Opisz to w dokumentacji klasy (wzór: `MassTransit8OutboxModel` w Knowledge i SleepDiary):

```csharp
/// <summary>
/// Expand step of removing <c>Categories.Description</c>: the model no longer maps the column, but it stays in the database so that
/// instances of the previous version keep working during the rolling update.
/// </summary>
/// <remarks>
/// The scaffolded <c>DropColumn</c> was removed on purpose. Drop the column in a later, hand-written contract migration once no instance
/// of the previous version is left (the model snapshot no longer contains it).
/// </remarks>
public partial class StopUsingCategoryDescription : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
```

   Pusta migracja i tak jest potrzebna: utrwala w snapshotcie model bez kolumny, żeby `has-pending-model-changes` było czyste.
   Kolumna nullable nie przeszkadza nowej wersji przy `INSERT` (dostaje `NULL`). Gdyby kolumna była `NOT NULL` bez wartości
   domyślnej, nowa wersja nie mogłaby wstawiać wierszy: wtedy ta migracja musi zrobić ją nullable albo dodać wartość domyślną.

**Wydanie N+1 lub późniejsze (contract, gdy żadna instancja wersji N-1 już nie działa):**

```bash
dotnet ef migrations add DropCategoryDescription \
  -p src/Services/Knowledge/Knowledge.Infrastructure -s src/Services/Knowledge/Knowledge.Infrastructure \
  --context KnowledgeWriteDbContext -o Migrations
```

Model się nie zmienił, więc `Up` i `Down` są puste. Uzupełnij je ręcznie:

```csharp
/// <summary>
/// Contract step of removing <c>Categories.Description</c>: drops the column left in place by <c>StopUsingCategoryDescription</c>.
/// </summary>
/// <remarks>Destructive: the descriptions are lost. <c>Down</c> recreates an empty column.</remarks>
public partial class DropCategoryDescription : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Description", schema: "knowledge", table: "Categories");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Description", schema: "knowledge", table: "Categories", type: "nvarchar(500)", maxLength: 500, nullable: true);
    }
}
```

Zostawiając coś na fazę contract, wpisz to do dokumentacji migracji expand **i** do backlogu. Otwarty przykład w repozytorium:
`OutboxState.BusName` i `IX_OutboxState_BusName_Created` po przejściu na MassTransit 8 (ADR-0035) czekają na migrację
contract w obu serwisach (szkic w [07, 7.10](../07-dane-i-ef-core.md#expandcontract-na-prawdziwych-przykładach)).

## Część C: migracja danych

Gdy nowa kolumna ma być wypełniona z istniejących danych (backfill), dodaj SQL do tej samej migracji expand albo osobnej:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<string>(
        name: "Description", schema: "knowledge", table: "Categories", type: "nvarchar(500)", maxLength: 500, nullable: true);

    // EXEC defers compilation: the idempotent script must also compile on databases where this migration is already applied.
    migrationBuilder.Sql(
        """
        EXEC(N'UPDATE [knowledge].[Categories] SET [Description] = [Name] WHERE [Description] IS NULL;');
        """);
}
```

Zasady migracji danych:

- **Czysty SQL.** Na prod migracje wykonuje DBA ze skryptu `--idempotent`; kod C# w `Up` działa tylko w chwili generowania
  skryptu, nie na bazie produkcyjnej.
- **`EXEC(N'...')`**, gdy SQL odwołuje się do kolumn, które ta migracja tworzy, zmienia lub usuwa: SQL Server kompiluje cały wsad,
  zanim warunek „migracja już zastosowana” go pominie (wzór i wyjaśnienie: `FixOwnedPositionKeys`).
- **Idempotentnie** (`WHERE [Description] IS NULL`): skrypt może zostać uruchomiony ponownie.
- **Tylko własny schemat.** Żadnych odwołań do innych schematów (ADR-0021).
- **Duże tabele:** jedno `UPDATE` na milionach wierszy to długa transakcja i blokady. Migrator ma timeout poleceń 10 minut; dla
  dużych zbiorów uzgodnij z DBA aktualizację partiami.
- **Test:** nietrywialna migracja danych dostaje test integracyjny, który migruje do poprzedniej migracji, wstawia dane i migruje
  do końca (wzór: `OwnedPositionTests.Migration_keeps_existing_items_and_renumbers_their_positions`).

## Typowe błędy

| Objaw | Przyczyna | Naprawa |
|---|---|---|
| Review: plik `{data}_Nazwa.cs` | nie zmieniono nazwy pliku migracji (build tego nie zgłasza) | zmień na `Nazwa.cs` |
| `has-pending-model-changes` zgłasza zmiany | zmiana modelu po wygenerowaniu migracji albo ręczna zmiana snapshotu | jeśli migracja nie wyszła poza Twoją maszynę: `migrations remove --force` i wygeneruj ponownie; inaczej nowa migracja |
| EF wygenerował `DropColumn` + `AddColumn` | zmiana nazwy właściwości | zostaw nazwę kolumny (`HasColumnName`) albo zaplanuj expand/contract |
| Migracja pada: „Cannot insert the value NULL into column” / „ALTER TABLE only allows columns to be added that can contain nulls” | nowa kolumna `NOT NULL` bez wartości domyślnej | `defaultValue` albo nullable + backfill + contract |
| Migracja pada na `CREATE UNIQUE INDEX` | istniejące duplikaty | migracja danych usuwająca/scalająca duplikaty przed indeksem (uzgodnij regułę biznesową) |
| Skrypt DBA pada na `Invalid column name` | surowy SQL bez `EXEC` w migracji, która tworzy/usuwa kolumnę | `EXEC(N'...')` |
| Po wdrożeniu stare pody: `Invalid column name 'X'` | `DropColumn` w tym samym wydaniu co kod | usuwanie tylko w kroku contract |
| Lista pokazuje stare dane do godziny po wdrożeniu | zmiana kształtu DTO bez zmiany wersji klucza cache | podbij `v1` → `v2` w kluczu |
| `/health/startup` 503 po wdrożeniu | migracja nie zastosowana na tej bazie | uruchom Migrator (dev/test) albo skrypt DBA (prod) |
| Edytujesz migrację, a na dev nic się nie zmienia | migracja już zastosowana (jest w `__EFMigrationsHistory`) | nigdy nie edytuj zastosowanej migracji: dodaj nową |

## Do zapamiętania

- Każda zmiana modelu zapisu = nowa migracja; nigdy nie edytuj migracji zastosowanej gdziekolwiek poza Twoją maszyną.
- Po `migrations add`: zmiana nazwy pliku, dokumentacja klasy (co, która faza), przegląd `Up`/`Down`, `has-pending-model-changes`.
- Stara wersja aplikacji musi działać na nowym schemacie: dodawaj w expand, usuwaj w contract w osobnym wydaniu.
- Migracja danych to idempotentny SQL w `EXEC`, bo DBA uruchamia skrypt, nie kod C#.
- Zmiana kształtu DTO = nowa wersja klucza cache i przegląd diffu kontraktu OpenAPI.

Powiązane: [07 Dane i EF Core](../07-dane-i-ef-core.md), [przepis 03](03-nowy-zbior-danych.md), [przepis 01](01-endpoint-komendy.md),
[11 Cache](../11-cache.md), [08 API i kontrakty](../08-api-i-kontrakty.md), ADR-0003, ADR-0004, ADR-0018, ADR-0021.
