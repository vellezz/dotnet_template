# ADR-0047: Odczyt `Result` sprawdzany przez kompilator, bez wyjątków

- **Status:** Zaakceptowany (doprecyzowuje ADR-0015 w zakresie odczytu wyniku)
- **Data:** 2026-10-02
- **Decydenci:** właściciel projektu
- **Powiązane:** ADR-0015, ADR-0023, ADR-0024, ADR-0025

## Kontekst

ADR-0015 wprowadza `Result` i `Result<T>` oraz zaleca odczyt wartości przez `TryGetValue`. API typów pozwalało jednak odczytać wynik
z pominięciem sprawdzenia:

- `Result.Error` miał typ `Error` i przy wyniku zakończonym sukcesem rzucał `InvalidOperationException`; obok istniało `ErrorOrNull`
  zwracające `null`. Atrybuty nullability na `IsSuccess`/`IsFailure` dotyczyły `ErrorOrNull`, więc kompilator nie ostrzegał
  przed `result.Error` bez sprawdzenia.
- `Result<T>.Value` miał typ `T` i przy porażce rzucał `InvalidOperationException`. Kompilator również nie ostrzegał.

Błąd programisty (odczyt nieistniejącego błędu albo wartości) wychodził więc dopiero w runtime, mimo że repozytorium ma włączone
nullable reference types i `TreatWarningsAsErrors`. Dwie właściwości na jeden błąd wymagały wyjaśniania, której kiedy używać.

## Fakty

- Zweryfikowane 2026-10-02 w kodzie: w kodzie produkcyjnym każdy odczyt `Error` i `Value` był poprzedzony sprawdzeniem;
  `Value` występowało w 5 miejscach (2 we frameworku, 3 handlery Knowledge), `TryGetValue` w 37. Testy używały `Create(...).Value`
  do przygotowania danych i `.Error` do asercji.
- Wartości wyników to w dużej części struktury: `Result<Guid>` (komendy tworzące), `Result<MaterialId>`, `Result<SleepQuality>`
  (fabryki ID i value objectów, ADR-0023, ADR-0024). Dla niczym nieograniczonego `T` adnotacja `T?` przy strukturze oznacza samo `T`,
  a `[MaybeNull]`/`[MemberNotNullWhen]` ostrzegają tylko dla typów referencyjnych.

## Rozważane opcje

1. **Zostawić rzucające właściwości:** zgodne z `Nullable<T>.Value` i wieloma bibliotekami; wada: błąd wykrywany w runtime,
   dwie właściwości błędu.
2. **`Error?` i `T?` z atrybutami nullability:** dla `Error` (typ referencyjny) kompilator pilnuje sprawdzenia; dla wartości będących
   strukturami brak ostrzeżeń, a porażka dawałaby po cichu `default` (`Guid.Empty`, `SleepQuality` = 0). Gorsze niż wyjątek.
3. **`Error?` z atrybutami, bez publicznego `Value`; wartość tylko przez `TryGetValue`/`Map`:** odczytu nieistniejącej wartości
   nie da się napisać, a odczyt błędu bez sprawdzenia jest ostrzeżeniem traktowanym jak błąd. Testy potrzebują pomocnika do
   rozpakowania wyniku.

## Decyzja

Opcja 3.

- `Result.Error` ma typ `Error?`. `IsFailure` ma `[MemberNotNullWhen(true, nameof(Error))]`, `IsSuccess`
  `[MemberNotNullWhen(false, nameof(Error))]`. Użycie `Error` bez sprawdzenia to ostrzeżenie CS8602/CS8604, czyli błąd kompilacji.
  `ErrorOrNull` usunięty. Nic nie rzuca wyjątku.
- `Result<T>` nie ma publicznego `Value`. Wartość odczytuje się przez `TryGetValue(out value, out error)` albo przekształca przez `Map`.
- Testy rozpakowują wyniki przez `ResultAssert` z nowego projektu `SuperApp.Framework.Testing`: `Success(result)` zwraca wartość,
  `Failure(result)` zwraca błąd, a przy innym wyniku test kończy się `ResultAssertionException` z kodem i komunikatem błędu.
  Projekt referują wyłącznie projekty testów (także w szablonie serwisu).
- Reguła architektury 15: kod produkcyjny nie referuje `SuperApp.Framework.Testing` (ADR-0025).

## Konsekwencje

- Pozytywne: niesprawdzony odczyt wyniku jest wykrywany przy kompilacji; jedna właściwość błędu; czytelne komunikaty w testach
  („Expected a successful result, but it failed with knowledge.category.invalid_slug …”).
- Kod produkcyjny ze wzorcem `var x = Create(...); if (x.IsFailure) return x.Error; … x.Value` przechodzi na
  `if (!Create(...).TryGetValue(out var x, out var error)) return error;`.
- Filtrowanie kolekcji wyników (`Where(r => r.IsSuccess).Select(r => r.Value)`) zastępuje pętla z `TryGetValue`.
- Zmiana łamie publiczne API `SuperApp.Framework.Domain`; wszystkie użycia w repozytorium zostały przepisane w tej samej zmianie.

## Źródła

- Microsoft Learn: atrybuty statycznej analizy stanu null (`MemberNotNullWhen`, `MaybeNullWhen`, `NotNullWhen`), dostęp 2026-10-02.
