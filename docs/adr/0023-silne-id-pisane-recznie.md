# ADR-0023: Silnie typowane ID pisane ręcznie

- **Status:** Zaakceptowany
- **Data:** 2026-09-29
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §2, §6, §13; ADR-0002, ADR-0009, ADR-0014, ADR-0015

## Kontekst

Identyfikatory agregatów mają być silnie typowane (`OrderId`, `CustomerId`), żeby nie dało się
ich pomylić. Domain nie może zależeć od żadnych pakietów (ADR-0002). Samo
`readonly record struct` nie wystarcza: bez dodatkowego kodu JSON serializuje ID jako obiekt,
EF Core i binding ASP.NET nie znają konwersji, OpenAPI pokazuje obiekt zamiast `string`/`uuid`,
a `default(OrderId)` omija walidację.

## Decyzja

### Definicja ID (Domain serwisu)

- Każde ID to ręcznie napisany `readonly record struct` implementujący
  `IStronglyTypedId<TSelf, TValue>` z `SuperApp.Framework.Domain` (rozszerza
  `ISingleValueObject<TSelf, TValue>`, ADR-0024):
  ```csharp
  public readonly record struct OrderId : IStronglyTypedId<OrderId, Guid>
  {
      public Guid Value { get; }
      private OrderId(Guid value) => Value = value;

      public static OrderId New() => new(Guid.CreateVersion7());
      public static Result<OrderId> Create(Guid value) =>
          value == Guid.Empty ? OrderErrors.InvalidId : new OrderId(value);
      public static OrderId FromTrusted(Guid value) => new(value); // materializacja z bazy/JSON

      public override string ToString() => Value.ToString();
  }
  ```
- Interfejs zawiera statyczne fabryki (static abstract members).
- Wejście HTTP (trasy, zapytania, komendy) przenosi identyfikatory jako typy proste (`Guid`);
  handler tworzy ID przez `Create`, a niepoprawna wartość kończy się błędem `NotFound` lub `Validation`.
- Walidacja w fabryce `Create` zwraca `Result` (ADR-0015).

### Wspólna infrastruktura (`SuperApp.Framework.Infrastructure`)

Pisana raz, obsługuje wszystkie ID automatycznie:

- **EF Core:** konwencja w `ConfigureConventions`, mapująca każdy typ `IStronglyTypedId<,>`
  na typ bazowy (`Guid`, `int`, `string`) generycznym `ValueConverter`.
- **JSON:** fabryka konwerterów `System.Text.Json` serializująca ID jako wartość prostą.
- **OpenAPI:** transformer schematu opisujący ID jako typ bazowy (np. `string`/`uuid`), żeby
  kontrakt i klienci generowani z niego (ADR-0009, ADR-0014) widzieli prymityw.

### Reguła analizatora (`SuperApp.Analyzers`)

- **APP002:** użycie `default(T)` lub `new T()` dla typu `IStronglyTypedId<,>` jest błędem
  kompilacji. ID powstaje wyłącznie przez fabryki.

## Konsekwencje

- Domain nie ma żadnych zależności od pakietów; test architektury może to wymusić bez wyjątków.
- Nowe ID = kilka linii w Domain serwisu; konwersje działają bez dodatkowej konfiguracji.
- Jednorazowy koszt: interfejs, konwencja EF, konwerter JSON, transformer OpenAPI i reguła APP002
  (orientacyjnie 150–250 linii) wraz z testami.
- `FromTrusted` omija walidację i jest przeznaczone wyłącznie dla infrastruktury (EF, JSON);
  użycie poza Infrastructure wykrywa test architektury.
