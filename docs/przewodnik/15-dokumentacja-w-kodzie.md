# 15. Dokumentacja w kodzie

Standardy dokumentacji XML w kodzie (ADR-0033): wymagania kompilatora i analizatorów (CS1591, APP006), struktura tagów (`<summary>`, `<remarks>`, `<param>`, `<returns>`), dokumentowanie typów `Result` z kodami błędów, generowanie dokumentacji OpenAPI z kontrolerów oraz reguły code review.
**Wymagania:** [03 Zasady](03-zasady.md); przydadzą się [05 Model domeny](05-model-domeny.md) i
[06 Warstwa aplikacji](06-warstwa-aplikacji.md), bo przykłady pochodzą z tych warstw.

> **W skrócie**
> - Dokumentacja XML i komentarze są **po angielsku**; literały (komunikaty błędów, wyjątków, szablony logów) mogą być po polsku.
> - Odbiorca: osoba nowa w zespole, która nie zna frameworku. Ma się dowiedzieć, do czego typ służy, jak go użyć, co gwarantuje,
>   co może pójść źle i czego nie robić. Opis powtarzający sygnaturę jest niedopuszczalny.
> - Publiczne API: `<summary>` zawsze (CS1591), `<param>`/`<typeparam>`/`<returns>` kompletne (APP006), w `src/Framework`
>   dodatkowo `<remarks>` na typach. Metoda zwracająca `Result` wymienia w `<returns>` każdy kod błędu.
> - Implementacja, która nic nie dodaje: `/// <inheritdoc />`. Typy `internal` też dokumentujesz, choć kompilator tego nie wymusza.
> - Dokumentacja kontrolera (i typów ciała żądania) trafia do commitowanego kontraktu OpenAPI: piszesz ją dla konsumenta API,
>   z `<response code>` dla każdego statusu.

---

## 15.1 Po co i dla kogo

`SuperApp.Framework.*` to wewnętrzny framework, którego nikt nie zna przed dołączeniem do zespołu, a `Contracts` i kontrolery to
kontrakty dla innych zespołów. Bez opisu kontraktu (znaczenie parametrów, zwracane błędy, wymagany scope, pułapki) użytkownik
typu musi czytać implementację, a to nie skaluje się na kilkanaście serwisów. ADR-0033 ustala więc, że dokumentacja XML jest
**podstawowym** źródłem wiedzy o kodzie, czytanym w IDE w chwili użycia typu.

Dobra dokumentacja odpowiada na pytania, które zadaje nowa osoba:

| Pytanie | Gdzie odpowiedź | Przykład z repozytorium |
|---|---|---|
| Czym to jest i jaką ma rolę w architekturze? | `<summary>` | `IUnitOfWork`: „The single way changes made by a command reach the database…” |
| Jak tego użyć? Kto to wywołuje? | `<remarks>`, `<example>` | `ICommand`: kto wysyła komendę, przez jaki pipeline przechodzi |
| Jakie są reguły i niezmienniki? | `<remarks>` | `Category`: lista niezmienników nazwy i sluga |
| Co znaczy parametr, jaki ma format, jednostkę, zakres, co znaczy `null`? | `<param>` | `RecordSleepEntry.BedTime`: czas lokalny bez strefy, `DateTimeKind.Unspecified` |
| Co dostanę i jakie błędy mogą wystąpić? | `<returns>`, `<exception>` | `Material.UpdateDetails`: lista kodów w kolejności sprawdzania |
| Co może pójść źle, czego nie robić? | `<remarks>` | `ISingleValueObject.FromTrusted`: „Do not call it on user input” |
| Co jest powiązane? | `<seealso>`, `<see cref>` | `AggregateRoot` → `Entity`, `IDomainEvent` |

Wzorcem jest `SuperApp.Framework.*`: zanim napiszesz dokumentację nowego typu, przeczytaj dokumentację jego odpowiednika we
frameworku albo w Knowledge.

## 15.2 Język

- Dokumentacja XML i komentarze `//` w kodzie: **wyłącznie po angielsku** (ADR-0033). Dokumenty w `docs/` i ADR: po polsku.
- **Literały nie są dokumentacją**: komunikaty `Error` (`"Kategoria nie istnieje."`), wyjątków
  (`"Wynik zakończony sukcesem nie zawiera błędu."`) i walidatorów są po polsku, bo trafiają do użytkowników i logów zespołu.
  Szablony logów `[LoggerMessage]` są po angielsku (konwencja w kodzie, np. `"Request {RequestName} handled in {ElapsedMs} ms"`).
- Nazwy typów i identyfikatory po angielsku, w języku domeny kontekstu (ADR-0033).

```csharp
// Źle: dokumentacja po polsku i komunikat po angielsku
/// <summary>Kategoria nie istnieje.</summary>
public static readonly Error NotFound = Error.NotFound("knowledge.category.not_found", "Category does not exist.");

// Dobrze (CategoryErrors)
/// <summary>
/// <c>knowledge.category.not_found</c> (NotFound, HTTP 404): no category with the given ID exists (or the ID is empty).
/// Returned by the rename-category handler.
/// </summary>
public static readonly Error NotFound = Error.NotFound("knowledge.category.not_found", "Kategoria nie istnieje.");
```

## 15.3 Jak build to wymusza

| Mechanizm | Co sprawdza | Gdzie skonfigurowane |
|---|---|---|
| `GenerateDocumentationFile=true` | kompilator czyta i weryfikuje komentarze XML we wszystkich projektach | `Directory.Build.props` |
| **CS1591** | publiczny (widoczny na zewnątrz) typ lub składowa **bez żadnej** dokumentacji | kompilator; ostrzeżenie = błąd (`TreatWarningsAsErrors`) |
| CS1570, CS1572, CS1573, CS1574 | źle sformułowany XML, `param` do nieistniejącego parametru, brak `param` przy częściowej dokumentacji, nierozwiązany `cref` | kompilator |
| **APP006** | dokumentacja jest, ale niekompletna (szczegóły niżej) | `src/Tools/SuperApp.Analyzers/DocumentationCompletenessAnalyzer.cs` |
| `app_documentation_require_remarks = true` | APP006 wymaga też `<remarks>` na typach publicznych | `.editorconfig`, sekcja dla `src/Framework` |
| wyłączenia | projekty testowe: `NoWarn` CS1591 i APP006 | `Directory.Build.props` (`IsTestProject`) |

Fragment `Directory.Build.props`:

```xml
<!-- Dokumentacja XML publicznego API: brak summary = CS1591, niekompletne tagi = APP006 (ADR-0033). -->
<GenerateDocumentationFile>true</GenerateDocumentationFile>
...
<!-- Testy nie są API: bez wymogu dokumentacji XML (ADR-0033). -->
<PropertyGroup Condition="'$(IsTestProject)' == 'true'">
  <NoWarn>$(NoWarn);CS1591;APP006</NoWarn>
</PropertyGroup>
```

### Co dokładnie sprawdza APP006

Analizator działa na symbolach **widocznych na zewnątrz**: `public`, `protected` albo `protected internal`, i to samo dotyczy
każdego typu zawierającego. Publiczna metoda w klasie `internal` nie jest API, więc nie jest sprawdzana.

| Symbol | Wymagane |
|---|---|
| typ generyczny | `<typeparam>` dla każdego parametru typu |
| rekord pozycyjny, klasa z konstruktorem podstawowym | `<param>` dla **każdego** parametru, na typie |
| delegat | `<param>` i `<returns>` sygnatury |
| metoda, konstruktor, operator, konwersja | `<param>` dla każdego parametru, `<typeparam>`, `<returns>` (chyba że `void`, niegeneryczny `Task`/`ValueTask` albo konstruktor) |
| indeksator | `<param>` dla każdego parametru |
| typ w `src/Framework` | dodatkowo `<remarks>` |

Nie sprawdza:

- symboli **bez** dokumentacji (to robi CS1591),
- komentarzy z `<inheritdoc` (traktowane jako kompletne),
- źle sformułowanego XML (CS1570), symboli niejawnych i kodu generowanego (migracje EF, generatory źródeł).

Komunikat ma postać `The documentation of '{symbol}' does not describe: {lista} (ADR-0033)`:

```
error APP006: The documentation of 'GetAsync' does not describe: <param name="id">, <param name="cancellationToken">, <returns> (ADR-0033)
error APP006: The documentation of 'PageEnvelope' does not describe: <remarks>, <typeparam name="T">, <param name="Items"> (ADR-0033)
error CS1591: Missing XML comment for publicly visible type or member 'MaterialErrors.TooManyCategories'
```

Sprawdzenie lokalnie:

```bash
dotnet build SuperApp.slnx                       # ostrzeżenia są błędami; APP006 i CS1591 przerwą build
dotnet build src/Services/Knowledge/Knowledge.Domain   # szybciej: tylko projekt, który zmieniasz
```

Kompilator nie oceni treści. „`<summary>Gets the name.</summary>`” przejdzie build, ale nie przejdzie review (ADR-0033: „jakość
treści sprawdza code review”).

### Typy `internal`

Handlery, walidatory, repozytoria, konfiguracje EF, konsumenci i translatory są `internal`, więc CS1591 i APP006 ich nie
dotyczą. ADR-0033 i tak wymaga dla nich opisu: to w nich nowa osoba szuka odpowiedzi na „dlaczego handler zwraca 404 dla
pustego GUID-a” albo „dlaczego unieważnienie cache jest po commicie”. Repozytorium jest tu konsekwentne: każdy handler ma
`<summary>` i `<remarks>` z decyzjami, a parametry konstruktora podstawowego mają `<param>`.

## 15.4 Tagi i ich użycie

| Tag | Do czego | Uwagi |
|---|---|---|
| `<summary>` | jedno-dwa zdania: czym jest, jaka rola | bez powtarzania nazwy typu („Category aggregate.” niczego nie mówi) |
| `<remarks>` | jak działa, reguły, cykl życia, powiązane typy, pułapki | dziel na `<para>`, listy przez `<list type="bullet">` / `"number"` / `"table"` |
| `<param name="x">` | znaczenie, format, jednostka, zakres, znaczenie `null` | dla rekordów pozycyjnych: na typie, nazwa jak w rekordzie (`Name`) |
| `<typeparam name="T">` | czym ma być parametr typu, ograniczenia z sensem | `ISingleValueObject`: „The primitive type that is wrapped, stored in the database and serialized” |
| `<returns>` | co oznacza wynik; dla `Result` sukces i **każdy** kod błędu | patrz [15.6](#156-dokumentowanie-result-i-kodów-błędów) |
| `<exception cref="...">` | wyjątki rzucane **celowo** | `Result.Error`: `InvalidOperationException` dla sukcesu |
| `<example>` + `<code>` | użycie, gdy nie jest oczywiste; kod z repozytorium | znaki `<`, `>`, `&` jako `&lt;`, `&gt;`, `&amp;` |
| `<see cref="..."/>` | odnośnik do typu/składowej (sprawdzany przez kompilator) | `<see cref="Result{T}"/>` dla generyków |
| `<see langword="null"/>` | słowa kluczowe | także `true`, `false`, `internal`, `default` |
| `<c>...</c>` | kod w linii, kody błędów, nazwy spoza zasięgu `cref` | `<c>knowledge.material.archived</c>`, `<c>IUnitOfWork.SaveChangesAsync</c>` w Domain |
| `<paramref name="x"/>`, `<typeparamref>` | odnośnik do parametru w opisie | `<paramref name="now"/>` |
| `<seealso cref="..."/>` | typy powiązane | wyświetlane w sekcji „See also” |
| `<inheritdoc />` | dokumentacja z bazowej składowej / interfejsu | [15.8](#158-dziedziczenie-dokumentacji-inheritdoc) |
| `<response code="404">` | tylko kontrolery: opis statusu w OpenAPI | [15.9](#159-kontrolery-dokumentacja-trafia-do-openapi) |

`<see cref>` do typu z innej warstwy, do której projekt nie ma referencji (np. z Domain do `IUnitOfWork` z Application), się
nie skompiluje (CS1574). Wtedy używaj `<c>IUnitOfWork.SaveChangesAsync</c>`, jak robi to `IDomainEvent` (a `IAggregateRoot` z `<c>WriteDbContextBase</c>`).

## 15.5 Co napisać dla poszczególnych rodzajów typów

| Rodzaj | Musi zawierać | Wzór w repozytorium |
|---|---|---|
| Agregat | rola i granica; cykl życia; niezmienniki i limity (ze stałymi); zgłaszane zdarzenia; reguły egzekwowane poza agregatem (handler, indeks) | `Material`, `Category`, `SleepEntry` |
| Metoda agregatu | warunki wstępne, zmiana stanu, zgłaszane zdarzenia, idempotencja, `<returns>` z kodami w kolejności sprawdzania | `Material.UpdateDetails`, `Collection.SetItems` |
| Silne ID | mix-upy, które blokuje; kto je generuje; że `Create` nie sprawdza istnienia; zakaz `default` | `CategoryId` |
| Value object | reguła poprawności; jak przechowywany i serializowany; zakaz `default` | `WebUrl`, `SleepQuality` |
| `{Agregat}Errors` | dla każdego pola: kod, typ, HTTP, kto zwraca, kiedy; w `<remarks>` kody tworzone gdzie indziej | `MaterialErrors`, `LibraryErrors` |
| Zdarzenie domenowe | kto zgłasza i kiedy (także kiedy **nie**); kto obsługuje; znaczenie każdego pola, zwłaszcza czasu | `MaterialPublished`, `SleepEntryRecorded` |
| Repozytorium | co ładuje (z czym), że nie zapisuje; znaczenie `null`; wyjątki od reguły | `IMaterialRepository`, `IFavoriteRepository.RemoveAllForItemAsync` |
| Komenda / zapytanie | co robi; scope; reguły wejścia; co robi handler; wynik i **wszystkie** kody błędów | `CreateCategory`, `RecordSleepEntry`, `ListSleepEntries` |
| DTO | znaczenie pól z jednostkami i formatami; skąd pochodzą wartości pochodne | `SleepEntryDto`, `PagedResult<T>` |
| Handler, walidator | decyzje: dlaczego `NotFound`, dlaczego idempotentny, co z wyścigiem; zgodność z domeną | `AddFavoriteHandler`, `RecordSleepEntryValidator` |
| Kontroler i akcja | dla konsumenta API: scope, reguły, `<response code>` każdego statusu z kodami błędów | `CategoriesController`, `SleepEntriesController` |
| Zdarzenie integracyjne (`Contracts`) | kiedy publikowane (i kiedy nie); gwarancje dostarczenia; klucz idempotencji; zasady zgodności | `SleepEntryRecordedV1` |
| Typ frameworku | wszystko powyżej plus `<remarks>` (wymagane), `<example>` | `ICommand`, `Result`, `IUnitOfWork`, `FailSafeCache` |

## 15.6 Dokumentowanie `Result` i kodów błędów

Sygnatura `Result` nie mówi, **jakie** błędy mogą wystąpić. Dlatego `<returns>` metody (albo `<remarks>` komendy) wymienia
każdy kod, z typem lub statusem, najlepiej w kolejności, w jakiej są sprawdzane. Wzór z agregatu
(`Material.UpdateDetails`):

```csharp
/// <returns>
/// Success, or one of these errors:
/// <list type="bullet">
///   <item><description><see cref="MaterialErrors.Archived"/> (<c>knowledge.material.archived</c>) when the material is archived;</description></item>
///   <item><description><c>knowledge.material.invalid_title</c> (validation) when the title is blank or too long;</description></item>
///   <item><description><c>knowledge.material.invalid_description</c> (validation) when the description is too long;</description></item>
///   <item><description><see cref="MaterialErrors.MediaNotAllowedForArticle"/> (<c>knowledge.material.media_not_allowed</c>) for an article with main media;</description></item>
///   <item><description><c>knowledge.url.invalid</c> (validation) when the address is not an absolute HTTPS URL (see <see cref="WebUrl"/>);</description></item>
///   <item><description><see cref="MaterialErrors.InvalidDuration"/> (<c>knowledge.material.invalid_duration</c>) for a negative duration or a duration without media;</description></item>
///   <item><description><see cref="MaterialErrors.MainMediaRequired"/> (<c>knowledge.material.main_media_required</c>) when removing the media of a published video or podcast.</description></item>
/// </list>
/// </returns>
```

Zasady:

- **Kod zawsze w `<c>`**, także gdy jest `<see cref>` do pola błędu: czytelnik szuka kodu z odpowiedzi HTTP w IDE.
- **Błędy pipeline'u** (`auth.unauthenticated`, `auth.missing_scope`, `validation.failed`) wymieniaj w dokumentacji komend i
  zapytań, bo klient je zobaczy. W metodach agregatu ich nie ma.
- **Kolejność** opisuj, gdy ma znaczenie dla klienta (który błąd przyjdzie, gdy złamano dwie reguły). `RecordSleepEntry`
  ma w `<remarks>` listę numerowaną „Errors, in the order they are checked”.
- **Błędy wyścigu** opisz osobno: kiedy występują i co klient ma zrobić (`LibraryErrors.FavoriteAddedConcurrently`:
  „The favorite exists afterwards, so clients may treat it as success or simply retry”).
- **Fabryki ID/VO**: `<returns>` mówi, czego **nie** sprawdzają (`CategoryId.Create`: „does not check that the referenced
  object exists; that is the repository's job”).

Wzór z komendy (`RecordSleepEntry`, fragment `<remarks>`):

```csharp
/// <para>Errors, in the order they are checked:</para>
/// <list type="number">
///   <item><description>Pipeline: <c>auth.missing_scope</c> / <c>auth.unauthenticated</c> (HTTP 403).</description></item>
///   <item><description><c>RecordSleepEntryValidator</c>: <c>validation.failed</c> (HTTP 400) with field messages when <see cref="BedTime"/> or
///   <see cref="WakeTime"/> carries a time zone (<see cref="DateTime.Kind"/> other than <see cref="DateTimeKind.Unspecified"/>, i.e. it was
///   sent with <c>Z</c> or an offset), when <see cref="Quality"/>, <see cref="Awakenings"/> or <see cref="SleepLatencyMinutes"/> is out of
///   range, or when <see cref="Notes"/> is too long after trimming.</description></item>
///   <item><description><see cref="Domain.Entries.SleepEntryErrors.AlreadyExists"/> (<c>sleepdiary.entry.already_exists</c>, HTTP 409):
///   the user already has an entry for <see cref="Date"/>. Also returned when a concurrent request recorded the same day first: the unique
///   index violation on save is mapped to this error by the write context.</description></item>
///   <item><description><see cref="Domain.Entries.SleepEntryErrors.FutureDate"/> (<c>sleepdiary.entry.future_date</c>, HTTP 400):
///   <see cref="Date"/> is later than today in UTC plus one day (one day of tolerance for time zones ahead of UTC).</description></item>
///   <item><description>Any other rule of <see cref="Domain.Entries.SleepEntry.Record"/>, for example <c>sleepdiary.entry.wake_before_bed</c>,
///   <c>sleepdiary.entry.wake_date_mismatch</c>, <c>sleepdiary.entry.too_long</c> or <c>sleepdiary.entry.invalid_latency</c> (HTTP 400).</description></item>
/// </list>
```

```csharp
// Źle: nic nie mówi o błędach; czytelnik musi przejść przez handler, agregat i pipeline
/// <returns>The result.</returns>
public Result Publish(DateTimeOffset now)

// Dobrze (Material.Publish)
/// <returns>
/// Success, or <see cref="MaterialErrors.Archived"/> (<c>knowledge.material.archived</c>) for an archived material,
/// <see cref="MaterialErrors.ContentRequired"/> (<c>knowledge.material.content_required</c>) when there is no content, or
/// <see cref="MaterialErrors.MainMediaRequired"/> (<c>knowledge.material.main_media_required</c>) for a video or podcast without main media.
/// </returns>
```

## 15.7 Rekordy pozycyjne i konstruktory podstawowe

Parametry rekordu pozycyjnego (`public sealed record X(int A, string B)`) i konstruktora podstawowego
(`class Handler(IRepository repo)`) dokumentuje się **na typie** tagami `<param>` z nazwą jak w deklaracji. APP006 wymaga
każdego z nich na typach publicznych. Generator OpenAPI przenosi te opisy do opisów właściwości schematu (widać to w
`Knowledge.Api.json`: opis pola `name` schematu `CreateCategory` pochodzi z `<param name="Name">`).

Wzór (`SleepDiary.Contracts/SleepEntryRecordedV1.cs`, fragment):

```csharp
/// <summary>
/// Integration event, version 1: a user recorded a new sleep diary entry. Published by the SleepDiary service for other bounded contexts.
/// </summary>
/// <remarks>
/// <para>
/// Published once per newly recorded entry. It is NOT published when an entry is updated or deleted (ADR-0029), so a consumer that keeps
/// its own copy of the values cannot follow later corrections; treat the values as a snapshot at the moment of recording.
/// </para>
/// ...
/// </remarks>
/// <param name="EntryId">Identifier of the diary entry (a GUID version 7); unique, suitable as the idempotency key.</param>
/// <param name="UserId">Owner of the entry: the user's <c>sub</c> claim from the CIAM, an opaque string of at most 200 characters.</param>
/// <param name="Date">Entry date: the day the user woke up, in the user's local calendar (no time zone information is available).</param>
/// <param name="SleepMinutes">
/// Sleep time in whole minutes at the moment of recording: time in bed minus the time needed to fall asleep; between 0 and 1440.
/// </param>
/// <param name="Quality">Subjective sleep quality at the moment of recording, from 1 (worst) to 5 (best).</param>
/// <param name="RecordedAt">
/// UTC instant the entry was recorded: the entry's creation time stored by the SleepDiary service, taken from the service clock when the
/// recording command ran. It is not the moment of delivery.
/// </param>
public sealed record SleepEntryRecordedV1(Guid EntryId, string UserId, DateOnly Date, int SleepMinutes, int Quality, DateTimeOffset RecordedAt);
```

Każdy `<param>` mówi: znaczenie, jednostkę („whole minutes”), zakres („between 0 and 1440”), format („opaque string”),
źródło wartości („taken from the service clock”) i czego **nie** oznacza („It is not the moment of delivery”).

W opisie odwołuj się do innych parametrów przez `<paramref name="BedTime"/>`, do wygenerowanych właściwości przez
`<see cref="Date"/>` (tak robi `RecordSleepEntry`).

Konstruktor podstawowy klasy `internal` (handler) nie jest wymagany przez APP006, ale repozytorium dokumentuje go tak samo:

```csharp
/// <param name="entries">Write-side repository of diary entries.</param>
/// <param name="currentUser">The caller; the owner of the entry is always taken from it.</param>
/// <param name="clock">Source of the current UTC time, for the future-date rule and the audit timestamps.</param>
internal sealed class RecordSleepEntryHandler(ISleepEntryRepository entries, ICurrentUser currentUser, IClock clock)
```

## 15.8 Dziedziczenie dokumentacji: inheritdoc

Używaj go, gdy implementacja albo nadpisanie **niczego nie dodaje** do opisu z interfejsu lub klasy bazowej. Analizator APP006
traktuje taki komentarz jako kompletny.

```csharp
// CategoryRepository: kontrakt opisany w ICategoryRepository
/// <inheritdoc />
public Task<Category?> GetAsync(CategoryId id, CancellationToken cancellationToken) => ...

// CategoryId: Value i FromTrusted opisane w ISingleValueObject / IStronglyTypedId
/// <inheritdoc />
public Guid Value { get; }
```

Gdy implementacja ma **własną** wiedzę, dopisz ją obok. `CategoryRepository.AllExistAsync` dziedziczy opis i dodaje `<remarks>`
o duplikatach; `GetSleepEntryHandler.Handle` dziedziczy opis i doprecyzowuje `<returns>`:

```csharp
/// <inheritdoc />
/// <returns>
/// The entry; <see cref="SleepEntryErrors.NotFound"/> (<c>sleepdiary.entry.not_found</c>) when the caller has no entry for the day;
/// <see cref="AuthorizationErrors.Unauthenticated"/> (<c>auth.unauthenticated</c>) when the caller has no subject.
/// </returns>
public async Task<Result<SleepEntryDto>> Handle(GetSleepEntry query, CancellationToken cancellationToken)
```

```csharp
// Źle: inheritdoc tam, gdzie implementacja ma ważne odstępstwo, którego interfejs nie opisuje
/// <inheritdoc />
public Task<int> RemoveAllForItemAsync(...)   // a usuwa natychmiast, nie przy zapisie Unit of Work

// Dobrze: odstępstwo opisane w interfejsie (IFavoriteRepository.RemoveAllForItemAsync, <remarks>),
// implementacja może wtedy użyć inheritdoc
```

## 15.9 Kontrolery: dokumentacja trafia do OpenAPI

Projekt Api wywołuje `AddOpenApi`, a generator komentarzy XML `Microsoft.AspNetCore.OpenApi` kopiuje dokumentację akcji do
dokumentu OpenAPI, który przy buildzie zapisuje się w `src/Services/{Serwis}/{Serwis}.Api/openapi/{Serwis}.Api.json` i jest
commitowany (ADR-0019, ADR-0033). Dokumentację kontrolera piszesz więc **dla konsumenta API** (frontend, mobile, inny zespół),
nie dla programisty .NET.

| XML | OpenAPI |
|---|---|
| `<summary>` akcji | `summary` operacji |
| `<remarks>` akcji | `description` operacji |
| `<param name="categoryId">` (trasa, query) | `description` parametru |
| `<param name="request">` (ciało) | `requestBody.description` |
| `<response code="404">` | `responses.404.description` |
| `<summary>` i `<param>` rekordu ciała | `description` schematu i jego właściwości |

Akcja z repozytorium (`Knowledge.Api/Controllers/CategoriesController.cs`):

```csharp
/// <summary>Creates a new category.</summary>
/// <remarks>
/// Required scope: <c>knowledge.catalog.write</c> (editor). Name: required, at most 100 characters (leading and trailing whitespace is
/// trimmed). Slug: required, at most 100 characters, only lowercase letters, digits and single hyphens between them
/// (e.g. <c>healthy-sleep</c>), unique across all categories. The slug cannot be changed later.
/// </remarks>
/// <param name="cancellationToken">Cancellation of the HTTP request.</param>
/// <param name="command">Name and slug of the new category.</param>
/// <returns>The identifier of the created category.</returns>
/// <response code="201">The category was created; the body contains its identifier.</response>
/// <response code="400">
/// Invalid name or slug (<c>validation.failed</c>, <c>knowledge.category.invalid_name</c>, <c>knowledge.category.invalid_slug</c>).
/// </response>
/// <response code="401">Missing, expired or invalid access token.</response>
/// <response code="403">The token lacks the scope <c>knowledge.catalog.write</c> (<c>auth.missing_scope</c>).</response>
/// <response code="409">
/// A category with this slug already exists (<c>knowledge.category.slug_taken</c>), also when another request created it at the same moment.
/// </response>
[HttpPost]
[ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created, MediaTypeNames.Application.Json)]
[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
public async Task<IActionResult> Create(CreateCategory command, CancellationToken cancellationToken) =>
    this.ToActionResult(await sender.Send(command, cancellationToken), id => StatusCode(StatusCodes.Status201Created, new CreatedResponse(id)));
```

i to, co z niej powstało w commitowanym kontrakcie (`Knowledge.Api/openapi/Knowledge.Api.json`, fragment):

```json
"summary": "Creates a new category.",
"description": "Required scope: `knowledge.catalog.write` (editor). Name: required, at most 100 characters (leading and trailing whitespace is\r\ntrimmed). Slug: required, at most 100 characters, only lowercase letters, digits and single hyphens between them\r\n(e.g. `healthy-sleep`), unique across all categories. The slug cannot be changed later.",
"requestBody": {
  "description": "Name and slug of the new category.",
  ...
},
"responses": {
  "201": { "description": "The category was created; the body contains its identifier.", ... },
  "409": { "description": "A category with this slug already exists (`knowledge.category.slug_taken`), also when another request created it at the same moment.", ... }
}
```

Zasady dla kontrolerów:

- **`<response code>` dla każdego statusu** zadeklarowanego w `[ProducesResponseType]` (także na klasie: 401, 403), z kodami
  błędów w `<c>`. Statusy wspólne dla kontrolera deklaruj atrybutem na klasie, ale opis `<response>` jest per akcja.
- **`<param name="cancellationToken">` przed parametrem ciała.** Generator kopiuje opis każdego `param`, który nie jest
  parametrem trasy ani query, do opisu ciała żądania, więc wygrywa ostatni taki `param` (tak opisuje to dokumentacja
  `ServiceNameControllerBase` w szablonie serwisu). Gdy `cancellationToken` jest ostatni, ciało dostanie opis
  „Cancellation of the HTTP request.”.
- **Bez szczegółów implementacji** („handler loads the aggregate”) i bez nazw klas .NET: konsument API ich nie zna.
- **Wartości liczbowe wprost** („at most 100 characters”), bo `<see cref="Category.MaxNameLength"/>` nie ma sensu w kontrakcie.

### Pułapka: komenda jako ciało żądania

Gdy akcja przyjmuje komendę Application bezpośrednio jako ciało (`Create(CreateCategory command, ...)`), dokumentacja
**komendy** (pisana dla programisty .NET) staje się opisem schematu w kontrakcie. Generator zamienia `<see cref>` na typ i
nazwę, a `<example>` przenosi do pola `example`. W obecnym `Knowledge.Api.json` schemat `CreateCategory` wygląda tak:

```json
"description": "Creates a catalog category with a display name and a unique slug. ... Requires scope string KnowledgeScopes.CatalogWrite.",
"example": "```Result&lt;Guid&gt; result = await sender.Send(new CreateCategory(\"Healthy sleep\", \"healthy-sleep\"), cancellationToken);```",
"properties": {
  "name": { "type": "string", "description": "Display name of the category; not blank, at most int Category.MaxNameLength characters after trimming. Stored trimmed." }
}
```

Konsument API dostaje opis z kodem C# i odwołaniami „int Category.MaxNameLength”. Dlatego przy nowych endpointach stosuj
osobny rekord żądania w Api, pisany dla konsumenta, jak `RenameCategoryRequest`:

```csharp
/// <summary>Request body of <c>PUT /v1/categories/{categoryId}/name</c>: the new name of a category.</summary>
/// <param name="Name">The new name: required, at most 100 characters (trimmed). The slug of the category does not change.</param>
public sealed record RenameCategoryRequest(string Name);
```

Osobny rekord jest konieczny także wtedy, gdy część danych przychodzi z trasy (`categoryId`) albo z tokenu.

## 15.10 Przykłady w dokumentacji

`<example>` dodawaj, gdy użycie nie jest oczywiste: typy frameworku, fabryki z `Result`, klasy z niebanalną sekwencją wywołań.
Kod przykładu ma pochodzić z repozytorium (albo być do niego wiernie podobny) i się kompilować po wklejeniu w odpowiednie miejsce.

Wzór (`ICommandHandler<TCommand, TResponse>`, fragment):

```csharp
/// <example>
/// <code>
/// internal sealed class CreateCategoryHandler(ICategoryRepository categories) : ICommandHandler&lt;CreateCategory, Result&lt;Guid&gt;&gt;
/// {
///     public async Task&lt;Result&lt;Guid&gt;&gt; Handle(CreateCategory command, CancellationToken cancellationToken)
///     {
///         if (await categories.SlugExistsAsync(command.Slug, cancellationToken))
///         {
///             return CategoryErrors.SlugTaken;
///         }
///
///         if (!Category.Create(command.Name, command.Slug).TryGetValue(out var category, out var error))
///         {
///             return error;
///         }
///
///         categories.Add(category);
///         return category.Id.Value;
///     }
/// }
/// </code>
/// </example>
```

- W XML znaki `<`, `>`, `&` muszą być encjami (`&lt;`, `&gt;`, `&amp;`), inaczej CS1570.
- Przykład starzeje się razem z kodem. Gdy zmieniasz wzorzec (np. sposób unieważniania cache), wyszukaj go w przykładach
  dokumentacji (`grep -rn "RemoveByTagAsync" src --include=*.cs`) i popraw w tej samej zmianie.
- Przykład w dokumentacji komendy, która jest ciałem żądania, trafi do kontraktu OpenAPI ([15.9](#159-kontrolery-dokumentacja-trafia-do-openapi)).

## 15.11 Dobre i złe przykłady

### Typ frameworku: `ICommand` (dobrze)

```csharp
/// <summary>
/// A request to change the state of the system that reports only success or failure, without returning data.
/// Shorthand for <see cref="ICommand{TResponse}"/> with <see cref="Result"/> as the response.
/// </summary>
/// <remarks>
/// <para>
/// Most state changes are of this kind: renaming, publishing, archiving, deleting. The caller learns whether the change was applied;
/// if it needs the new state, it sends a query afterwards. The command runs through the same pipeline as every command
/// (logging, authorization, validation, transaction); see <see cref="ICommand{TResponse}"/> for the full description and conventions.
/// </para>
/// <para>
/// Handle it with an <see cref="ICommandHandler{TCommand}"/>. In a controller, <c>this.ToActionResult(result)</c> turns a successful
/// result into HTTP 204 No Content and a failure into <c>ProblemDetails</c>.
/// </para>
/// <para>
/// Commands are also sent by message consumers in the Worker. There the current user is the system identity, which has every scope,
/// so <see cref="RequiresScopeAttribute"/> does not block internal processing.
/// </para>
/// </remarks>
```

Dlaczego dobrze: `summary` mówi, czym komenda **różni się** od wariantu z wartością; `remarks` mówi, kiedy jej użyć, co zrobić,
gdy potrzebny jest stan, jaki handler zaimplementować, jak wynik zamienia się na HTTP i jaka jest pułapka w Workerze.

To samo w wersji złej:

```csharp
// Źle: powtarza sygnaturę, niczego nie uczy
/// <summary>Command without a return value.</summary>
public interface ICommand : ICommand<Result>;
```

### Typ wyniku: `Result` (dobrze)

`Result` (`SuperApp.Framework.Domain/Results/Result.cs`) w `<remarks>` ma listę „Working with results”: sprawdzaj `IsFailure` przed
`Error` (inaczej wyjątek), propaguj błąd przez `return result.Error;`, nie ignoruj wyniku (APP001), behaviory i kontrolery
polegają na wyniku. Każda składowa ma opis skutków: `Error` ma `<exception cref="InvalidOperationException">The result is
successful; check IsFailure first.</exception>`.

### Parametr z wiedzą operacyjną: `FailSafeCache.GetOrCreateAsync` (dobrze)

```csharp
/// <param name="key">
/// Cache key, unique within the service. Include every parameter the result depends on and a version segment that you bump whenever
/// the shape of <typeparamref name="T"/> changes, e.g. <c>knowledge:material:v1:{id}</c>; otherwise replicas may read old JSON after a deployment.
/// </param>
/// <typeparam name="T">Type of the cached value; must be JSON-serializable, because it is stored in Redis. Use DTOs, not EF entities.</typeparam>
```

Opis klucza zawiera regułę (wersja w kluczu), przykład i konsekwencję złamania reguły (stary JSON po wdrożeniu). Tego nie da
się wyczytać z typu `string`.

### Komenda Knowledge: `CreateCategory` (dobrze dla programisty)

Pełny tekst jest w [06 Warstwa aplikacji](06-warstwa-aplikacji.md#65-komendy): wymagany scope, reguły wejścia ze stałymi
domeny, co robi handler (przycina nazwę, zgłasza `CategoryChanged`), wynik, każdy kod błędu ze statusem, przykład i
`<param>` z formatem sluga i informacją „Cannot be changed later”.

### Błędy, które często widać w review

```csharp
// Źle: summary = nazwa; param bez treści; returns nie mówi o błędach
/// <summary>Rename.</summary>
/// <param name="name">The name.</param>
/// <returns>Result.</returns>
public Result Rename(string name)

// Dobrze (Category.Rename)
/// <summary>Changes the display name of the category and raises <see cref="CategoryChanged"/>; the slug stays unchanged.</summary>
/// <remarks>
/// Idempotent: when the trimmed name equals the current <see cref="Name"/> (ordinal, case-sensitive comparison) the call succeeds
/// without changing anything and without raising an event, so the cached category list is not invalidated needlessly.
/// A change of letter case only (<c>"sleep"</c> to <c>"Sleep"</c>) is a real rename.
/// </remarks>
/// <param name="name">New display name; trimmed, required, at most <see cref="MaxNameLength"/> characters.</param>
/// <returns>Success, or the validation error <c>knowledge.category.invalid_name</c> for a blank or too long name.</returns>
public Result Rename(string name)
```

```csharp
// Źle: dokumentacja opisuje stary mechanizm (unieważnianie bezpośrednio w handlerze zdarzenia),
// a kod robi to po commicie przez IUnitOfWork.OnCommitted – czytelnik skopiuje błędny wzorzec
/// <example>
/// <code>
/// // domain event handler reacting to CategoryChanged
/// await cache.RemoveByTagAsync(KnowledgeCache.CategoriesTag, cancellationToken);
/// </code>
/// </example>

// Dobrze (IUnitOfWork.OnCommitted, przykład)
/// <code>
/// unitOfWork.OnCommitted(token =&gt; cache.RemoveByTagAsync(KnowledgeCache.CategoriesTag, token).AsTask());
/// </code>
```

```csharp
// Źle: dane osobowe w przykładzie, odwołanie do implementacji zamiast kontraktu
/// <param name="UserId">The user id, e.g. jan.kowalski@firma.pl. Read from the Users table.</param>

// Dobrze (SleepEntryRecordedV1)
/// <param name="UserId">Owner of the entry: the user's <c>sub</c> claim from the CIAM, an opaque string of at most 200 characters.</param>
```

## 15.12 Checklista review dokumentacji

Przy każdym pull requeście sprawdź dla nowych i zmienionych typów:

- [ ] `<summary>` mówi, czym typ/składowa jest i po co istnieje, a nie powtarza nazwy.
- [ ] Agregat: cykl życia, niezmienniki z limitami (`<see cref>` do stałych), zdarzenia, reguły egzekwowane poza nim.
- [ ] Każda metoda zwracająca `Result` wymienia w `<returns>` sukces i **każdy** kod błędu w `<c>`, w kolejności sprawdzania,
      gdy ma znaczenie.
- [ ] Komenda/zapytanie: scope, reguły wejścia, wynik, kody błędów pipeline'u i domeny ze statusami HTTP.
- [ ] Każdy `<param>` ma znaczenie i, jeśli dotyczy: format, jednostkę, zakres, znaczenie `null`, źródło wartości (np. „from the token”).
- [ ] Czas: czy to UTC, czas lokalny bez strefy, skąd pochodzi (`IClock`, aggregate `now`).
- [ ] Pułapki i zakazy są nazwane („Never create it with `default`”, „Do not call it on user input”).
- [ ] `<inheritdoc />` tylko tam, gdzie implementacja nic nie dodaje; odstępstwa opisane.
- [ ] Przykłady w `<example>` zgadzają się z obecnym kodem i wzorcami (zwłaszcza po refaktoryzacji).
- [ ] Kontroler: `<response code>` dla każdego statusu z `[ProducesResponseType]`, opisy dla konsumenta API, `cancellationToken`
      udokumentowany przed parametrem ciała; diff `openapi/*.json` przejrzany.
- [ ] Ciało żądania: osobny rekord w Api z opisem dla konsumenta, nie komenda z opisem dla programisty .NET.
- [ ] Dokumentacja po angielsku, komunikaty błędów po polsku, bez danych osobowych i sekretów.
- [ ] Typy `internal` (handler, walidator, repozytorium, konfiguracja) mają opis decyzji, choć build tego nie wymusza.

## Typowe błędy

| Objaw | Przyczyna | Naprawa |
|---|---|---|
| CS1591 na publicznym typie | brak `<summary>` | dopisz dokumentację albo zmień widoczność na `internal`, jeśli typ nie jest API |
| APP006 `<param name="X">` na rekordzie | brak opisu parametru pozycyjnego | `<param name="X">` na typie, nazwa jak w deklaracji (wielkość liter ma znaczenie) |
| APP006 `<remarks>` w `src/Framework` | typ publiczny frameworku bez `<remarks>` | dopisz, jak działa i jakie ma pułapki |
| APP006 `<returns>` przy `Task<Result>` | `Task<T>` wymaga `<returns>`; tylko niegeneryczny `Task` jest zwolniony | opisz wynik i błędy |
| CS1570 | `<` albo `&` w tekście lub przykładzie | `&lt;`, `&gt;`, `&amp;` |
| CS1574 | `<see cref>` do typu spoza referencji projektu albo literówka | `<c>Nazwa</c>` albo popraw `cref`; generyki: `Result{T}` |
| CS1572 / CS1573 | `<param>` do nieistniejącego parametru po zmianie nazwy / brak `<param>` dla części parametrów | zaktualizuj dokumentację razem z sygnaturą |
| W kontrakcie OpenAPI ciało ma opis „Cancellation of the HTTP request.” | `<param name="cancellationToken">` po parametrze ciała | przenieś przed parametr ciała |
| W kontrakcie „int Category.MaxNameLength” albo kod C# w `example` | komenda Application użyta jako ciało żądania | osobny rekord żądania w Api z opisem dla konsumenta |
| Build przechodzi, a review odrzuca dokumentację | treść powtarza sygnaturę albo jest nieaktualna | checklista [15.12](#1512-checklista-review-dokumentacji) |

## Do zapamiętania

- Dokumentacja XML to główne źródło wiedzy o kodzie dla nowej osoby; pisz ją tak, żeby nie musiała czytać implementacji.
- Angielski w dokumentacji, polski w komunikatach; bez danych osobowych.
- CS1591 wymusza obecność, APP006 kompletność (`param`, `typeparam`, `returns`, `remarks` we frameworku), review jakość.
- `Result` zawsze z listą kodów błędów; komendy z kodami pipeline'u i statusami HTTP.
- Rekordy pozycyjne: `<param>` na typie; implementacje bez nowej wiedzy: `<inheritdoc />`.
- Kontrolery piszesz dla konsumenta API: trafiają do commitowanego OpenAPI; ciało żądania jako osobny rekord.
- Zmieniasz kod lub wzorzec, zmieniasz dokumentację i przykłady w tej samej zmianie.

## Powiązane

- Rozdziały: [03 Zasady](03-zasady.md), [05 Model domeny](05-model-domeny.md), [06 Warstwa aplikacji](06-warstwa-aplikacji.md),
  [08 API i kontrakty](08-api-i-kontrakty.md), [18 Checklista](18-checklista.md)
- Przepisy: [01 Endpoint komendy](przepisy/01-endpoint-komendy.md), [02 Endpoint zapytania](przepisy/02-endpoint-zapytania.md)
- ADR: [0019](../adr/0019-openapi-3-0.md), [0030](../adr/0030-szablon-serwisu.md), [0032](../adr/0032-jeden-typ-na-plik.md),
  [0033](../adr/0033-dokumentacja-xml-publicznego-api.md)
