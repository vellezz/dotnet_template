# ADR-0024: Value objects

- **Status:** Zaakceptowany
- **Data:** 2026-09-29
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §6, §7; ADR-0002, ADR-0005, ADR-0015, ADR-0023

## Kontekst

Pojęcia domenowe bez tożsamości (kwota, e-mail, adres, zakres dat) mają być modelowane jako
value objects, a nie typy prymitywne. Potrzebne są jednolite zasady tworzenia, walidacji,
mapowania EF i serializacji, spójne z silnymi ID (ADR-0023).

## Decyzja

### Definicja

- Value object: **niezmienny**, bez tożsamości, równość po wartościach, pisany ręcznie
  w Domain serwisu.
- Typ:
  - **jednowartościowy** (np. `Email`, `CurrencyCode`): `readonly record struct`,
  - **wielowartościowy** (np. `Money`, `Address`): `sealed record`.
- Właściwości tylko `{ get; }` (bez `init`). Dzięki temu wyrażenie `with` nie skompiluje się
  i nie da się obejść walidacji.
- Konstruktor prywatny.

### Tworzenie i walidacja

- Wyłącznie przez fabrykę `Create(...)`, zwracającą `Result<T>` z błędem `Validation`
  (ADR-0015); niezmienniki sprawdzane w fabryce.
- `FromTrusted(...)` bez walidacji tylko dla infrastruktury (EF, JSON); użycie poza
  Infrastructure wykrywa test architektury (jak w ADR-0023).
- Operacje zwracają nowe instancje; operacja, która może naruszyć regułę, zwraca `Result<T>`
  (np. `Money.Add` przy różnych walutach).
- Metody agregatów przyjmują value objects, nie prymitywy.

### Jednowartościowe value objects i ID: wspólny mechanizm

- Interfejs `ISingleValueObject<TSelf, TValue>` w `SuperApp.Framework.Domain`;
  `IStronglyTypedId<TSelf, TValue>` (ADR-0023) go rozszerza.
- Ta sama infrastruktura z `SuperApp.Framework.Infrastructure` obsługuje oba rodzaje: konwencja EF,
  konwerter JSON (wartość prosta, walidacja przez `Create`), transformer OpenAPI (typ bazowy).
- **APP002** obejmuje wszystkie typy `ISingleValueObject<,>`: `default(T)` i `new T()` są
  błędem kompilacji.

### Mapowanie EF (tylko `IEntityTypeConfiguration<T>` w Infrastructure)

- Jednowartościowe: automatycznie przez konwencję (kolumna typu bazowego).
- Wielowartościowe: **complex types** (`ComplexProperty`).
- Kolekcje value objects: `OwnsMany` z backing field.

### Granice

- Value objects należą do Domain jednego serwisu; `SuperApp.Framework` zawiera tylko interfejsy
  i infrastrukturę, żadnych konkretnych value objects biznesowych.
- **`Contracts`** (zdarzenia integracyjne): wyłącznie typy prymitywne.
- **DTO API:** ID i jednowartościowe value objects dozwolone (serializowane jako prymityw);
  wielowartościowe mapowane w Application na własne typy DTO.
- Read modele (`ReadDbContext`, ADR-0003): typy prymitywne.

## Konsekwencje

- Brak zależności Domain od pakietów; nowy jednowartościowy value object nie wymaga konfiguracji.
- Testy jednostkowe domeny obejmują fabryki (poprawne i niepoprawne wartości) i operacje.
- Niezmienność i brak `with` wymagają dyscypliny w review; `init` na właściwościach value objects
  można dodatkowo wykrywać regułą analizatora.
