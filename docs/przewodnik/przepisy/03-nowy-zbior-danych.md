# Przepis 03: nowy zbiór danych w kontekście (nowy agregat i tabela)

**Kiedy:** kontekst potrzebuje nowego rodzaju danych z własną tożsamością, cyklem życia i regułami, czyli nowego agregatu,
nowej tabeli w schemacie serwisu, zapisu przez komendę i odczytu przez zapytanie. Najpierw sprawdź
[wybór kontekstu](../04-wybor-kontekstu.md): czy dane na pewno należą do tego serwisu i czy to nie jest część istniejącego agregatu.

**Przykład:** czytelnik ocenia opublikowany materiał w skali 1–5; jedna ocena na użytkownika i materiał, można ją zmienić;
wszyscy czytelnicy widzą średnią i liczbę ocen. Funkcji (`MaterialRating`) **nie ma** w repozytorium: to ćwiczenie. Kod poniżej
został zbudowany i przetestowany w całości (build 0/0, wszystkie testy zielone), a potem wycofany. Krok po kroku z wyjaśnieniami
buduje go [samouczek](../16-samouczek-pelna-funkcja.md); tu jest lista kontrolna z kompletnym kodem. Mechanizmy w tle: [07 Dane i EF Core](../07-dane-i-ef-core.md).

W blokach kodu pomijamy komentarze dokumentacji XML (`///`), żeby skrócić listingi. W Twoim kodzie są obowiązkowe dla typów
publicznych (CS1591, APP006, ADR-0033): wzorem są pliki w repozytorium.

## Szybki start: `dotnet superapp add aggregate`

```bash
dotnet superapp add aggregate Knowledge Ratings MaterialRating      # --table, by zmienić nazwę tabeli (domyślnie MaterialRatings)
dotnet superapp migration add Knowledge AddMaterialRatings          # po zamodelowaniu stanu i konfiguracji EF
```

Tworzy:

- agregat z fabryką `Create` i silnym ID;
- `{Agregat}Errors` i port `I{Agregat}Repository`;
- konfigurację EF z `rowversion` i repozytorium zarejestrowane w `Add{Serwis}Core`;
- pierwszy test domeny.

Szkielet się kompiluje. Stan, reguły, value objects, read model i migracja to kroki poniżej. Odwrotność:
`dotnet superapp remove aggregate … --yes`; odmawia, dopóki agregatu używa inny kod (także migracja).
[22 Narzędzie](../22-narzedzie-superapp.md).

## Pliki

```
src/Services/Knowledge/
  Knowledge.Domain/Library/Ratings/
    RatingScore.cs                           value object 1–5
    MaterialRatingId.cs                      silne ID
    MaterialRating.cs                        agregat
    IMaterialRatingRepository.cs             port repozytorium (jeden na agregat)
    Events/MaterialRated.cs                  zdarzenie domenowe (tu: unieważnienie cache)
  Knowledge.Domain/Library/LibraryErrors.cs  + RatingRecordedConcurrently, InvalidRatingScore
  Knowledge.Application/Features/Library/RateMaterial/
    RateMaterial.cs, RateMaterialValidator.cs, RateMaterialHandler.cs
  Knowledge.Application/Features/Materials/GetMaterialRating/
    GetMaterialRating.cs, MaterialRatingDto.cs
  Knowledge.Infrastructure/
    Persistence/Write/Configurations/MaterialRatingConfiguration.cs
    Persistence/Write/Repositories/MaterialRatingRepository.cs
    Persistence/Write/KnowledgeWriteDbContext.cs          + wpis w UniqueConstraintErrors
    Persistence/Read/Models/MaterialRatingRow.cs
    Persistence/Read/Configurations/MaterialRatingRowConfiguration.cs
    Persistence/Read/KnowledgeReadDbContext.cs            + IQueryable<MaterialRatingRow>
    Features/Materials/GetMaterialRatingHandler.cs        handler zapytania (ADR-0026)
    Caching/KnowledgeCache.cs                             + klucz, tag, ustawienia
    Caching/MaterialRatingCacheInvalidation.cs            handler zdarzenia domenowego
    InfrastructureServiceCollectionExtensions.cs          + rejestracja repozytorium
    Migrations/AddMaterialRatings.cs (+ {data}_AddMaterialRatings.Designer.cs, snapshot)
  Knowledge.Api/Controllers/
    RateMaterialRequest.cs, LibraryController.cs (PUT), MaterialsController.cs (GET)
  Knowledge.Api/openapi/Knowledge.Api.json                generowany przy buildzie, commitowany
  tests/Knowledge.Domain.Tests/MaterialRatingTests.cs
  tests/Knowledge.Application.Tests/Fakes/FakeMaterialRatingRepository.cs, RateMaterialHandlerTests.cs
  tests/Knowledge.IntegrationTests/MaterialRatingTests.cs
```

## Krok 0: decyzje przed kodem

| Pytanie | Odpowiedź w przykładzie | Skutek |
|---|---|---|
| Osobny agregat czy część istniejącego? | osobny: ocena ma własny cykl życia i właściciela (użytkownika), a `Material` zmienia redaktor | brak FK do `Materials`, spójność przez reguły w handlerze i zdarzenia |
| Klucz naturalny? | (użytkownik, materiał) | unikalny indeks + mapowanie wyścigu na błąd |
| Powtórzenie operacji? | zmienia ocenę; ta sama ocena = brak zmian | `PUT` idempotentny (204) |
| Współbieżność tego samego agregatu? | ocena należy do jednego użytkownika, „ostatni zapis wygrywa” jest poprawny | **bez** `rowversion` (świadomie, opisane w konfiguracji) |
| Kto reaguje na zmianę? | cache podsumowania | zdarzenie domenowe `MaterialRated`, bez zdarzenia integracyjnego |
| Co przy archiwizacji materiału? | oceny zostają w bazie, podsumowanie znika (zapytanie zwraca tylko opublikowane) | brak migracji danych ani konsumenta |

## Krok 1: domena

`Knowledge.Domain/Library/Ratings/RatingScore.cs`
```csharp
using SuperApp.Framework.Domain.Results;
using SuperApp.Framework.Domain.ValueObjects;

namespace Knowledge.Domain.Library.Ratings;

public readonly record struct RatingScore : ISingleValueObject<RatingScore, int>
{
    public const int Min = 1;

    public const int Max = 5;

    private RatingScore(int value) => Value = value;

    public int Value { get; }

    public static Result<RatingScore> Create(int value) =>
        value is < Min or > Max ? LibraryErrors.InvalidRatingScore : new RatingScore(value);

    public static RatingScore FromTrusted(int value) => new(value);

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
```

`Knowledge.Domain/Library/Ratings/MaterialRatingId.cs`
```csharp
using SuperApp.Framework.Domain.Results;
using SuperApp.Framework.Domain.ValueObjects;

namespace Knowledge.Domain.Library.Ratings;

public readonly record struct MaterialRatingId : IStronglyTypedId<MaterialRatingId, Guid>
{
    private MaterialRatingId(Guid value) => Value = value;

    public Guid Value { get; }

    public static MaterialRatingId New() => new(Guid.CreateVersion7());

    public static Result<MaterialRatingId> Create(Guid value) =>
        value == Guid.Empty ? Error.Validation("knowledge.rating.invalid_id", "Niepoprawny identyfikator.") : new MaterialRatingId(value);

    public static MaterialRatingId FromTrusted(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
```

`Knowledge.Domain/Library/Ratings/MaterialRating.cs`
```csharp
using SuperApp.Framework.Domain.Aggregates;
using Knowledge.Domain.Common;
using Knowledge.Domain.Library.Ratings.Events;
using Knowledge.Domain.Materials;

namespace Knowledge.Domain.Library.Ratings;

public sealed class MaterialRating : AggregateRoot<MaterialRatingId>
{
    private MaterialRating(MaterialRatingId id)
        : base(id)
    {
    }

    public UserId UserId { get; private set; }

    public MaterialId MaterialId { get; private set; }

    public RatingScore Score { get; private set; }

    public DateTimeOffset RatedAt { get; private set; }

    public static MaterialRating Rate(UserId userId, MaterialId materialId, RatingScore score, DateTimeOffset now)
    {
        var rating = new MaterialRating(MaterialRatingId.New()) { UserId = userId, MaterialId = materialId, Score = score, RatedAt = now };
        rating.Raise(new MaterialRated(rating.Id, materialId, score, now));
        return rating;
    }

    public void ChangeScore(RatingScore score, DateTimeOffset now)
    {
        if (Score == score)
        {
            return;
        }

        Score = score;
        RatedAt = now;
        Raise(new MaterialRated(Id, MaterialId, score, now));
    }
}
```

`Knowledge.Domain/Library/Ratings/Events/MaterialRated.cs`
```csharp
using SuperApp.Framework.Domain.Events;
using Knowledge.Domain.Materials;

namespace Knowledge.Domain.Library.Ratings.Events;

public sealed record MaterialRated(MaterialRatingId RatingId, MaterialId MaterialId, RatingScore Score, DateTimeOffset RatedAt) : IDomainEvent;
```

`Knowledge.Domain/Library/Ratings/IMaterialRatingRepository.cs`
```csharp
using Knowledge.Domain.Common;
using Knowledge.Domain.Materials;

namespace Knowledge.Domain.Library.Ratings;

public interface IMaterialRatingRepository
{
    Task<MaterialRating?> FindAsync(UserId userId, MaterialId materialId, CancellationToken cancellationToken);

    void Add(MaterialRating rating);
}
```

`Knowledge.Domain/Library/LibraryErrors.cs` (dopisane pola)
```csharp
public static readonly Error RatingRecordedConcurrently =
    Error.Conflict("knowledge.library.rating_recorded_concurrently", "Materiał został równolegle oceniony.");

public static readonly Error InvalidRatingScore =
    Error.Validation("knowledge.rating.invalid_score", $"Ocena musi mieć wartość {RatingScore.Min}–{RatingScore.Max}.");
```

Reguły, które ten kod spełnia:

- Prywatny konstruktor (używa go też EF przy materializacji), fabryka statyczna `Rate`, prywatne settery, zmiana stanu tylko
  metodą z nazwą z języka domeny (`ChangeScore`).
- Metody, które nie mogą się nie udać (wartość jest już poprawnym `RatingScore`), nie zwracają `Result`. Walidacja wartości jest
  w `RatingScore.Create`.
- Ponowienie z tą samą oceną nic nie zmienia i nie zgłasza zdarzenia (idempotencja, brak zbędnego unieważnienia cache).
- Agregat nie widzi innych agregatów: „materiał jest opublikowany” i „jedna ocena na użytkownika” sprawdza handler.
- Silnych ID i value objectów nie mapujesz w EF ręcznie: konwencja `SuperApp.Framework` zapisze `RatingScore` jako `int`,
  `MaterialRatingId` jako `uniqueidentifier`, a `UserId` jako `nvarchar` (długość podasz w konfiguracji).

## Krok 2: zapis (EF Core)

`Knowledge.Infrastructure/Persistence/Write/Configurations/MaterialRatingConfiguration.cs`
```csharp
using Knowledge.Domain.Common;
using Knowledge.Domain.Library.Ratings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Knowledge.Infrastructure.Persistence.Write.Configurations;

internal sealed class MaterialRatingConfiguration : IEntityTypeConfiguration<MaterialRating>
{
    internal const string UserMaterialIndexName = "IX_MaterialRatings_UserId_MaterialId";

    public void Configure(EntityTypeBuilder<MaterialRating> builder)
    {
        builder.ToTable("MaterialRatings", table =>
            table.HasCheckConstraint("CK_MaterialRatings_Score", $"[Score] BETWEEN {RatingScore.Min} AND {RatingScore.Max}"));
        builder.HasKey(rating => rating.Id);
        builder.Property(rating => rating.Id).ValueGeneratedNever();
        builder.Property(rating => rating.UserId).HasMaxLength(UserId.MaxLength);
        builder.HasIndex(rating => new { rating.UserId, rating.MaterialId }).IsUnique().HasDatabaseName(UserMaterialIndexName);
        builder.Ignore(rating => rating.DomainEvents);
    }
}
```

- Przestrzeń nazw `...Persistence.Write.Configurations` jest warunkiem: kontekst zapisu stosuje tylko konfiguracje ze swojej
  przestrzeni nazw i podrzędnych. Klasa w innej przestrzeni zostanie po cichu pominięta.
- `HasMaxLength(UserId.MaxLength)` jest obowiązkowe: bez niego kolumna byłaby `nvarchar(max)`, a takiej nie da się użyć w indeksie.
- `CHECK` z tych samych stałych co value object: baza odrzuci ocenę 6 nawet przy ręcznym `INSERT`.
- Nazwa indeksu ustawiona stałą, bo ta sama stała jest kluczem mapy błędów (niżej).
- Brak `Property<byte[]>("Version").IsRowVersion()`: decyzja z kroku 0. Agregat edytowany przez wiele osób (jak `Category`)
  powinien mieć `rowversion` ([07, 7.7](../07-dane-i-ef-core.md#77-współbieżność-optymistyczna-rowversion)).
- Brak FK do `Materials`: to inny agregat.

`Knowledge.Infrastructure/Persistence/Write/Repositories/MaterialRatingRepository.cs`
```csharp
using Knowledge.Domain.Common;
using Knowledge.Domain.Library.Ratings;
using Knowledge.Domain.Materials;
using Microsoft.EntityFrameworkCore;

namespace Knowledge.Infrastructure.Persistence.Write.Repositories;

internal sealed class MaterialRatingRepository(KnowledgeWriteDbContext context) : IMaterialRatingRepository
{
    public Task<MaterialRating?> FindAsync(UserId userId, MaterialId materialId, CancellationToken cancellationToken) =>
        context.Set<MaterialRating>().FirstOrDefaultAsync(
            rating => rating.UserId == userId && rating.MaterialId == materialId,
            cancellationToken);

    public void Add(MaterialRating rating) => context.Add(rating);
}
```

`Add` jest synchroniczne i nie zapisuje; zmianę oceny zapisze Unit of Work, bo `FindAsync` zwraca śledzony agregat
(nie ma metody `Update`).

`Knowledge.Infrastructure/Persistence/Write/KnowledgeWriteDbContext.cs`: nowy wpis w mapie
```csharp
protected override IReadOnlyDictionary<string, Error> UniqueConstraintErrors { get; } = new Dictionary<string, Error>
{
    ["IX_Categories_Slug"] = CategoryErrors.SlugTaken,
    ["IX_Favorites_UserId_ItemType_ItemId"] = LibraryErrors.FavoriteAddedConcurrently,
    ["IX_MaterialCompletions_UserId_MaterialId"] = LibraryErrors.CompletionRecordedConcurrently,
    [MaterialRatingConfiguration.UserMaterialIndexName] = LibraryErrors.RatingRecordedConcurrently,
};
```

Bez tego wpisu dwie równoległe pierwsze oceny tego samego materiału przez tego samego użytkownika dałyby drugiemu żądaniu
ogólny `persistence.duplicate` (409) zamiast błędu opisanego w kontrakcie. Sekwencyjna druga ocena nie trafia na indeks, bo
handler znajduje istniejącą i ją zmienia. Uzupełnij też tabelę w dokumentacji XML właściwości.

`Knowledge.Infrastructure/InfrastructureServiceCollectionExtensions.cs` (w `AddKnowledgeCore`)
```csharp
services.AddScoped<IMaterialRatingRepository, MaterialRatingRepository>();
```

## Krok 3: migracja

```bash
dotnet build SuperApp.slnx
dotnet ef migrations add AddMaterialRatings \
  -p src/Services/Knowledge/Knowledge.Infrastructure -s src/Services/Knowledge/Knowledge.Infrastructure \
  --context KnowledgeWriteDbContext -o Migrations
```

Narzędzie tworzy trzy zmiany w `Knowledge.Infrastructure/Migrations/`:
`{yyyyMMddHHmmss}_AddMaterialRatings.cs`, `{yyyyMMddHHmmss}_AddMaterialRatings.Designer.cs` i zmieniony
`KnowledgeWriteDbContextModelSnapshot.cs`.

1. Zmień nazwę `{data}_AddMaterialRatings.cs` na `AddMaterialRatings.cs` (konwencja ADR-0032; APP005 nie sprawdza migracji). `.Designer.cs` zostaw.
2. Zastąp `/// <inheritdoc />` na klasie opisem: co zmienia, która faza, czy coś zostaje na contract.
3. Przeczytaj `Up`/`Down`. Oczekiwany wynik:

`Knowledge.Infrastructure/Migrations/AddMaterialRatings.cs`
```csharp
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Knowledge.Infrastructure.Migrations
{
    /// <summary>
    /// Expand step: creates the table <c>knowledge.MaterialRatings</c> for the <c>MaterialRating</c> aggregate (a user's 1–5 score of a
    /// material) with the check constraint <c>CK_MaterialRatings_Score</c> and the unique index <c>IX_MaterialRatings_UserId_MaterialId</c>.
    /// </summary>
    /// <remarks>
    /// A new table only: the previous version of the application ignores it, so the migration is safe before a rolling update and needs no
    /// contract step. <c>Down</c> drops the table together with all ratings.
    /// </remarks>
    public partial class AddMaterialRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MaterialRatings",
                schema: "knowledge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MaterialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    RatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialRatings", x => x.Id);
                    table.CheckConstraint("CK_MaterialRatings_Score", "[Score] BETWEEN 1 AND 5");
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaterialRatings_UserId_MaterialId",
                schema: "knowledge",
                table: "MaterialRatings",
                columns: new[] { "UserId", "MaterialId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaterialRatings",
                schema: "knowledge");
        }
    }
}
```

Na co patrzysz w przeglądzie:

| Element | Oczekiwane | Gdy jest inaczej |
|---|---|---|
| `schema` | `"knowledge"` wszędzie | kontekst bez `HasDefaultSchema`? sprawdź, czy dziedziczy po `WriteDbContextBase` |
| `UserId` | `nvarchar(200)` | `nvarchar(max)`: brak `HasMaxLength` |
| `Id` | bez `SqlServer:Identity` | brak `ValueGeneratedNever()` |
| `CheckConstraint` | `[Score] BETWEEN 1 AND 5` | brak: konfiguracja w złej przestrzeni nazw (cała konfiguracja pominięta) |
| Indeks | nazwa `IX_MaterialRatings_UserId_MaterialId`, `unique: true` | nazwa inna niż stała w mapie błędów |
| Inne tabele | brak zmian | zmiany w cudzych tabelach = ktoś zmienił model bez migracji albo snapshot jest nieaktualny; wyjaśnij przed commitem |
| Ostrzeżenie „may result in the loss of data” | brak | nowa tabela nie może go wywołać; jeśli jest, migracja zawiera coś więcej |

4. Sprawdź zgodność modelu i snapshotu:

```bash
dotnet ef migrations has-pending-model-changes \
  -p src/Services/Knowledge/Knowledge.Infrastructure -s src/Services/Knowledge/Knowledge.Infrastructure \
  --context KnowledgeWriteDbContext
# No changes have been made to the model since the last migration.
```

5. Zastosuj lokalnie: `dotnet run --project src/Migrator/SuperApp.Migrator --launch-profile local` (tryb hybrydowy) albo
   `docker compose -f deploy/local/docker-compose.yml --profile app run --rm migrator`. Sprawdź w bazie:

```sql
SELECT MigrationId FROM knowledge.__EFMigrationsHistory ORDER BY MigrationId;
SELECT name, definition FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('knowledge.MaterialRatings');
```

Nowa tabela to bezpieczny krok **expand**: poprzednia wersja aplikacji jej nie zna i nie używa. Prod: DBA uruchomi ją ze skryptu
idempotentnego razem z resztą migracji wydania (ADR-0004).

## Krok 4: odczyt

`Knowledge.Infrastructure/Persistence/Read/Models/MaterialRatingRow.cs`
```csharp
namespace Knowledge.Infrastructure.Persistence.Read.Models;

internal sealed class MaterialRatingRow
{
    public Guid Id { get; init; }

    public string UserId { get; init; } = string.Empty;

    public Guid MaterialId { get; init; }

    public int Score { get; init; }

    public DateTimeOffset RatedAt { get; init; }
}
```

`Knowledge.Infrastructure/Persistence/Read/Configurations/MaterialRatingRowConfiguration.cs`
```csharp
using Knowledge.Infrastructure.Persistence.Read.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Knowledge.Infrastructure.Persistence.Read.Configurations;

internal sealed class MaterialRatingRowConfiguration : IEntityTypeConfiguration<MaterialRatingRow>
{
    public void Configure(EntityTypeBuilder<MaterialRatingRow> builder) => builder.ToTable("MaterialRatings").HasKey(row => row.Id);
}
```

`Knowledge.Infrastructure/Persistence/Read/KnowledgeReadDbContext.cs` (nowa właściwość)
```csharp
internal IQueryable<MaterialRatingRow> MaterialRatings => Set<MaterialRatingRow>();
```

Read model ma prymitywne typy, mapuje istniejącą tabelę i nie generuje migracji. Bez konfiguracji `Set<MaterialRatingRow>()`
rzuca w czasie działania („type is not included in the model”); wykryje to dopiero test integracyjny.

## Krok 5: przypadki użycia (Application)

`Knowledge.Application/Features/Library/RateMaterial/RateMaterial.cs`
```csharp
using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Application.Security;

namespace Knowledge.Application.Features.Library.RateMaterial;

[RequiresScope(KnowledgeScopes.LibraryWrite)]
public sealed record RateMaterial(Guid MaterialId, int Score) : ICommand;
```

`Knowledge.Application/Features/Library/RateMaterial/RateMaterialValidator.cs`
```csharp
using FluentValidation;
using Knowledge.Domain.Library.Ratings;

namespace Knowledge.Application.Features.Library.RateMaterial;

internal sealed class RateMaterialValidator : AbstractValidator<RateMaterial>
{
    public RateMaterialValidator()
    {
        RuleFor(command => command.MaterialId).NotEmpty();
        RuleFor(command => command.Score).InclusiveBetween(RatingScore.Min, RatingScore.Max);
    }
}
```

`Knowledge.Application/Features/Library/RateMaterial/RateMaterialHandler.cs`
```csharp
using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Time;
using SuperApp.Framework.Domain.Results;
using Knowledge.Domain.Library;
using Knowledge.Domain.Library.Ratings;
using Knowledge.Domain.Materials;

namespace Knowledge.Application.Features.Library.RateMaterial;

internal sealed class RateMaterialHandler(
    IMaterialRatingRepository ratings,
    IMaterialRepository materials,
    ICurrentUser currentUser,
    IClock clock) : ICommandHandler<RateMaterial>
{
    public async Task<Result> Handle(RateMaterial command, CancellationToken cancellationToken)
    {
        if (!currentUser.RequireUserId().TryGetValue(out var userId, out var userError))
        {
            return userError;
        }

        if (!RatingScore.Create(command.Score).TryGetValue(out var score, out var scoreError))
        {
            return scoreError;
        }

        if (!MaterialId.Create(command.MaterialId).TryGetValue(out var materialId, out _)
            || !await materials.IsPublishedAsync(materialId, cancellationToken))
        {
            return LibraryErrors.ItemNotAvailable;
        }

        var rating = await ratings.FindAsync(userId, materialId, cancellationToken);
        if (rating is null)
        {
            ratings.Add(MaterialRating.Rate(userId, materialId, score, clock.UtcNow));
        }
        else
        {
            rating.ChangeScore(score, clock.UtcNow);
        }

        return Result.Success();
    }
}
```

Handler kończy się na `Add` albo na metodzie agregatu. Zapis, dispatch `MaterialRated`, commit i unieważnienie cache po commicie
wykonują `TransactionBehavior` i `IUnitOfWork` ([07, 7.5](../07-dane-i-ef-core.md#75-unit-of-work-savechangesasync-zwraca-result)).
Użytkownik pochodzi z tokenu (`RequireUserId`), nigdy z ciała żądania.

`Knowledge.Application/Features/Materials/GetMaterialRating/GetMaterialRating.cs`
```csharp
using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Domain.Results;

namespace Knowledge.Application.Features.Materials.GetMaterialRating;

[RequiresScope(KnowledgeScopes.CatalogRead)]
public sealed record GetMaterialRating(Guid MaterialId) : IQuery<Result<MaterialRatingDto>>;
```

`Knowledge.Application/Features/Materials/GetMaterialRating/MaterialRatingDto.cs`
```csharp
namespace Knowledge.Application.Features.Materials.GetMaterialRating;

public sealed record MaterialRatingDto(Guid MaterialId, double? AverageScore, int RatingCount);
```

## Krok 6: handler zapytania i cache (Infrastructure)

`Knowledge.Infrastructure/Features/Materials/GetMaterialRatingHandler.cs`
```csharp
using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Domain.Results;
using SuperApp.Framework.Infrastructure.Caching;
using Knowledge.Application.Features.Materials.GetMaterialRating;
using Knowledge.Domain.Common;
using Knowledge.Domain.Materials;
using Knowledge.Infrastructure.Caching;
using Knowledge.Infrastructure.Persistence.Read;
using Microsoft.EntityFrameworkCore;

namespace Knowledge.Infrastructure.Features.Materials;

internal sealed class GetMaterialRatingHandler(KnowledgeReadDbContext db, FailSafeCache cache)
    : IQueryHandler<GetMaterialRating, Result<MaterialRatingDto>>
{
    private static readonly string Published = nameof(PublicationStatus.Published);

    public async Task<Result<MaterialRatingDto>> Handle(GetMaterialRating query, CancellationToken cancellationToken)
    {
        var summary = await cache.GetOrCreateAsync(
            KnowledgeCache.MaterialRatingKey(query.MaterialId),
            token => new ValueTask<MaterialRatingDto?>(LoadAsync(query.MaterialId, token)),
            KnowledgeCache.MaterialRating,
            [KnowledgeCache.MaterialRatingTag(query.MaterialId), KnowledgeCache.MaterialTag(query.MaterialId)],
            cancellationToken);

        return summary is null ? MaterialErrors.NotFound : summary;
    }

    // Null when the material does not exist or is not published.
    private async Task<MaterialRatingDto?> LoadAsync(Guid materialId, CancellationToken cancellationToken)
    {
        var row = await db.Materials
            .Where(material => material.Id == materialId && material.Status == Published)
            .Select(material => new
            {
                Count = db.MaterialRatings.Count(rating => rating.MaterialId == material.Id),
                Average = db.MaterialRatings.Where(rating => rating.MaterialId == material.Id).Average(rating => (double?)rating.Score),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new MaterialRatingDto(materialId, row.Average is { } average ? Math.Round(average, 2) : null, row.Count);
    }
}
```

- Jedno zapytanie SQL: status materiału i dwa podzapytania agregujące. `Average` po `(double?)`, bo `AVG` z zera wierszy to
  `NULL`; rzutowanie na `double` przed uśrednieniem zapobiega dzieleniu całkowitemu w SQL.
- Bez agregatów i repozytoriów: kontekst odczytu, read modele, projekcja (ADR-0026).
- Cache z dwoma tagami: ocena (`MaterialRatingTag`) i zmiana materiału (`MaterialTag`), bo archiwizacja ma od razu ukryć podsumowanie.
  Zasady cache: [11 Cache](../11-cache.md), [przepis 05](05-zdarzenia-i-cache.md).
- Indeksu na samym `MaterialId` w tabeli nie ma (unikalny indeks zaczyna się od `UserId`, więc nie pomaga w `WHERE MaterialId = ...`).
  Przy małej liczbie ocen i z cache to wystarcza; gdy tabela urośnie, dodaj `builder.HasIndex(rating => rating.MaterialId)` i migrację.

`Knowledge.Infrastructure/Caching/KnowledgeCache.cs` (dopisane składowe)
```csharp
public static readonly FailSafeOptions MaterialRating = new(Fresh: TimeSpan.FromMinutes(1), MaxStale: TimeSpan.FromMinutes(10));

public static string MaterialRatingKey(Guid materialId) => $"knowledge:material-rating:v1:{materialId}";

public static string MaterialRatingTag(Guid materialId) => $"knowledge:material-rating:{materialId}";
```

`Knowledge.Infrastructure/Caching/MaterialRatingCacheInvalidation.cs`
```csharp
using SuperApp.Framework.Application.Events;
using SuperApp.Framework.Application.Persistence;
using SuperApp.Framework.Infrastructure.Caching;
using Knowledge.Domain.Library.Ratings.Events;

namespace Knowledge.Infrastructure.Caching;

internal sealed class MaterialRatingCacheInvalidation(IUnitOfWork unitOfWork, FailSafeCache cache) : IDomainEventHandler<MaterialRated>
{
    public Task HandleAsync(MaterialRated domainEvent, CancellationToken cancellationToken)
    {
        var tag = KnowledgeCache.MaterialRatingTag(domainEvent.MaterialId.Value);
        unitOfWork.OnCommitted(token => cache.RemoveByTagAsync(tag, token).AsTask());
        return Task.CompletedTask;
    }
}
```

Handlery zdarzeń domenowych i handlery zapytań z Infrastructure są rejestrowane automatycznie (`AddAppApplication` skanuje
assembly Application i Infrastructure), więc nie dopisujesz ich do DI.

## Krok 7: API

`Knowledge.Api/Controllers/RateMaterialRequest.cs`
```csharp
namespace Knowledge.Api.Controllers;

public sealed record RateMaterialRequest(int Score);
```

`Knowledge.Api/Controllers/LibraryController.cs` (nowa akcja; kontroler ma `[Route("v1/me")]`)
```csharp
[HttpPut("ratings/{materialId:guid}")]
[ProducesResponseType(StatusCodes.Status204NoContent)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
public async Task<IActionResult> Rate(Guid materialId, RateMaterialRequest request, CancellationToken cancellationToken) =>
    this.ToActionResult(await sender.Send(new RateMaterial(materialId, request.Score), cancellationToken));
```

`Knowledge.Api/Controllers/MaterialsController.cs` (nowa akcja; kontroler ma `[Route("v1/materials")]`)
```csharp
[HttpGet("{materialId:guid}/rating")]
[ProducesResponseType<MaterialRatingDto>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
public async Task<IActionResult> GetRating(Guid materialId, CancellationToken cancellationToken) =>
    this.ToActionResult(await sender.Send(new GetMaterialRating(materialId), cancellationToken));
```

Dokumentacja XML akcji (summary, remarks ze scope, `param`, `returns`, `response` dla każdego statusu) trafia do kontraktu
OpenAPI; skopiuj wzór z repozytorium. Build regeneruje `Knowledge.Api/openapi/Knowledge.Api.json`: obejrzyj diff (dwie nowe
operacje, schematy `RateMaterialRequest` i `MaterialRatingDto`) i commituj go razem z kodem ([08 API i kontrakty](../08-api-i-kontrakty.md)).

Sprawdzenie lokalne bezpośrednio na serwisie (port 5101, tylko do debugowania; token `dev-cli` jak w [01 Start](../01-start.md),
z scope `knowledge.library.write knowledge.catalog.read`). Moduł zobaczy nowe operacje dopiero po wystawieniu ich w BFF experience
([przepis 01, krok 9](01-endpoint-komendy.md)); bez tego `/api/example/v1/knowledge/...` przez bramę zwraca 404.

```bash
curl -s -i -X PUT http://localhost:5101/v1/me/ratings/$MATERIAL_ID \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -d '{"score":4}'
# HTTP/1.1 204 No Content

curl -s http://localhost:5101/v1/materials/$MATERIAL_ID/rating -H "Authorization: Bearer $TOKEN"
# {"materialId":"...","averageScore":4,"ratingCount":1}

curl -s -i -X PUT http://localhost:5101/v1/me/ratings/$MATERIAL_ID \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -d '{"score":7}'
# HTTP/1.1 400 Bad Request
# Content-Type: application/problem+json
# {"title":"Żądanie zawiera niepoprawne dane.","status":400,"instance":"/v1/me/ratings/...",
#  "errors":{"Score":["..."]},"code":"validation.failed","traceId":"..."}
```

## Krok 8: testy

| Poziom | Plik | Co sprawdza |
|---|---|---|
| Domain | `src/Services/Knowledge/tests/Knowledge.Domain.Tests/MaterialRatingTests.cs` | granice skali (`Min`, `Max`, poza zakresem), zdarzenie przy ocenie, brak zmian i zdarzeń przy tej samej ocenie, zmiana oceny |
| Application | `src/Services/Knowledge/tests/Knowledge.Application.Tests/RateMaterialHandlerTests.cs` + `Fakes/FakeMaterialRatingRepository.cs` | szkic → `ItemNotAvailable`; druga ocena zmienia pierwszą; różni użytkownicy; ocena 6 odrzucona przez domenę bez walidatora; anonim → `Forbidden`; walidator |
| Integration | `src/Services/Knowledge/tests/Knowledge.IntegrationTests/MaterialRatingTests.cs` | średnia i liczba przez cały pipeline, unieważnienie cache po zmianie, archiwizacja ukrywa podsumowanie, wyścig → `RatingRecordedConcurrently`, `CHECK` w bazie |

Fake repozytorium (wzór dla każdego nowego portu):

`src/Services/Knowledge/tests/Knowledge.Application.Tests/Fakes/FakeMaterialRatingRepository.cs`
```csharp
using Knowledge.Domain.Common;
using Knowledge.Domain.Library.Ratings;
using Knowledge.Domain.Materials;

namespace Knowledge.Application.Tests.Fakes;

internal sealed class FakeMaterialRatingRepository : IMaterialRatingRepository
{
    public List<MaterialRating> Items { get; } = [];

    public Task<MaterialRating?> FindAsync(UserId userId, MaterialId materialId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.FirstOrDefault(rating => rating.UserId == userId && rating.MaterialId == materialId));

    public void Add(MaterialRating rating) => Items.Add(rating);
}
```

Test wyścigu i ograniczenia bazy (fragment `MaterialRatingTests.cs` z testów integracyjnych):

```csharp
[Fact]
public async Task Concurrent_first_ratings_are_reported_as_recorded_concurrently()
{
    var userId = ResultAssert.Success(UserId.Create($"race-{Guid.NewGuid():N}"));
    var materialId = MaterialId.New();

    await using var scope = fixture.Services.CreateAsyncScope();
    var ratings = scope.ServiceProvider.GetRequiredService<IMaterialRatingRepository>();
    ratings.Add(MaterialRating.Rate(userId, materialId, ResultAssert.Success(RatingScore.Create(4)), DateTimeOffset.UtcNow));
    ratings.Add(MaterialRating.Rate(userId, materialId, ResultAssert.Success(RatingScore.Create(5)), DateTimeOffset.UtcNow));

    var result = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);

    Assert.Equal(LibraryErrors.RatingRecordedConcurrently, result.Error);
}

[Fact]
public async Task Database_rejects_score_outside_the_scale_even_bypassing_domain()
{
    await using var scope = fixture.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<KnowledgeWriteDbContext>();

    var exception = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(
        "INSERT INTO [knowledge].[MaterialRatings] ([Id], [UserId], [MaterialId], [Score], [RatedAt]) VALUES ({0}, {1}, {2}, 6, SYSDATETIMEOFFSET())",
        [Guid.NewGuid(), $"db-{Guid.NewGuid():N}", Guid.NewGuid()],
        TestContext.Current.CancellationToken));

    Assert.Contains("CK_MaterialRatings_Score", exception.Message, StringComparison.Ordinal);
}
```

Testy integracyjne zmieniające użytkownika lub scope mają `[Collection(PipelineCollection.Name)]` i przywracają
`fixture.CurrentUser.Subject` po sobie (metoda `RateAsAsync` w pliku testu). Fixture tworzy bazę z migracji (`MigrateAsync`),
więc błąd migracji albo zapomniana migracja wychodzi w `dotnet test`.

```bash
dotnet build SuperApp.slnx
dotnet test --solution SuperApp.slnx
```

## Typowe błędy

| Objaw | Przyczyna | Naprawa |
|---|---|---|
| Review: plik `20261001..._AddMaterialRatings.cs` | nie zmieniono nazwy pliku migracji (build tego nie zgłasza) | zmień na `AddMaterialRatings.cs` |
| Build: CS1591 / APP006 | brak dokumentacji XML typów publicznych (domena, komenda, DTO, akcje; klasa migracji: tylko CS1591) | uzupełnij według plików wzorcowych |
| Build: APP002 | `default(RatingScore)` albo `new MaterialRatingId()` | `RatingScore.Create(...)`, `MaterialRatingId.New()` |
| Test architektury: `FromTrusted` w Application | handler użył `RatingScore.FromTrusted` | w Application zawsze `Create` |
| Migracja: `UserId nvarchar(max)` i błąd tworzenia indeksu | brak `HasMaxLength(UserId.MaxLength)` | dopisz, wygeneruj migrację od nowa (jeszcze niezastosowaną) |
| Migracja nie ma `CHECK` ani indeksu | konfiguracja w złej przestrzeni nazw | `namespace Knowledge.Infrastructure.Persistence.Write.Configurations` |
| `InvalidOperationException: Cannot create a DbSet for 'MaterialRatingRow'` | brak konfiguracji read modelu | `MaterialRatingRowConfiguration` w `Persistence/Read/Configurations` |
| `Unable to resolve service for type 'IMaterialRatingRepository'` | brak rejestracji w `AddKnowledgeCore` | `services.AddScoped<IMaterialRatingRepository, MaterialRatingRepository>()` |
| Równoległe pierwsze oceny: 409 `persistence.duplicate` | brak wpisu w `UniqueConstraintErrors` | dopisz `[MaterialRatingConfiguration.UserMaterialIndexName] = ...` |
| Ocena zmieniona, a `GET .../rating` pokazuje starą średnią | brak handlera `MaterialRated` albo unieważnienie poza `OnCommitted` | `MaterialRatingCacheInvalidation` z `unitOfWork.OnCommitted(...)` |
| `/health/startup` 503 po uruchomieniu API | migracja nie zastosowana lokalnie | uruchom Migrator |

## Do zapamiętania

- Nowy agregat = domena (ID, VO, agregat, port, błędy) → konfiguracja EF + repozytorium + mapa błędów + DI → migracja →
  read model + zapytanie → komenda → API → testy na trzech poziomach.
- Limity i zakresy tylko ze stałych domeny; baza powtarza reguły jako `CHECK` i unikalne indeksy.
- Unikalny klucz naturalny = sprawdzenie w handlerze + unikalny indeks z jawną nazwą + wpis w `UniqueConstraintErrors`.
- Brak FK do innych agregatów; brak `rowversion` tylko ze świadomym uzasadnieniem w dokumentacji konfiguracji.
- Migracja nowej tabeli to expand; przejrzyj ją i sprawdź `has-pending-model-changes` przed commitem.

Powiązane: [07 Dane i EF Core](../07-dane-i-ef-core.md), [16 Samouczek](../16-samouczek-pelna-funkcja.md),
[przepis 01](01-endpoint-komendy.md), [przepis 02](02-endpoint-zapytania.md), [przepis 04](04-zmiana-modelu-i-migracja.md),
[przepis 05](05-zdarzenia-i-cache.md), ADR-0021, ADR-0023, ADR-0024, ADR-0026, ADR-0027.
