# ADR-0025: Testy architektury w jednym projekcie

- **Status:** Zaakceptowany (do ponownej oceny)
- **Data:** 2026-09-29
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §6, §13, §15; ADR-0002, ADR-0004, ADR-0017, ADR-0023, ADR-0024

## Kontekst

Reguła zależności między warstwami jest w dużej mierze wymuszana przez referencje między
projektami. Kompilator i analizatory (`SuperApp.Analyzers`) nie pilnują jednak m.in. granic między
serwisami w monorepo ani reguł dotyczących użycia typów. Utrzymanie testów ma być minimalne.

## Decyzja

- **Jeden projekt `tests/SuperApp.ArchitectureTests`** dla całego rozwiązania, oparty na
  **ArchUnitNET**. Assembly serwisów wykrywane po konwencji nazw, więc nowy serwis podlega
  regułom bez zmian w testach.
- Reguły (początkowy zestaw):
  1. serwis nie referuje innego serwisu (`{A}.*` → `{B}.*`),
  2. Domain zależy wyłącznie od `SuperApp.Framework.Domain`,
  3. Application nie zależy od Infrastructure, EF Core ani MassTransit,
  4. `Contracts` nie zależy od Domain i zawiera tylko typy prymitywne (ADR-0024),
  5. `FromTrusted` używane wyłącznie w Infrastructure (ADR-0023, ADR-0024),
  6. Api odwołuje się do Infrastructure wyłącznie w composition root,
  7. nikt nie referuje `SuperApp.Migrator` (ADR-0004),
  8. handlery `internal sealed`; handlery komend tylko w Application, handlery zapytań tylko
     w Infrastructure (ADR-0026); handlery zdarzeń domenowych w Application (tłumaczenie na zdarzenia
     integracyjne, ADR-0027) lub Infrastructure (techniczne, np. unieważnianie cache, ADR-0020),
  9. kolejność rejestracji behaviors zgodna z ADR-0017.
- Nowa reguła dopisywana tylko wtedy, gdy ADR wprowadza zasadę, której nie pilnuje kompilator
  ani `SuperApp.Analyzers`.

## Przegląd decyzji

Decyzja do ponownej oceny po uruchomieniu kilku pierwszych serwisów. Jeśli koszt utrzymania
przewyższy korzyść, testy zostaną usunięte, a pilnowanie referencji przejmie sprawdzenie
w `Directory.Build.props` (np. Domain bez `PackageReference`, brak referencji między serwisami);
zmiana wymaga nowego ADR.

## Konsekwencje

- Testy uruchamiane w CI razem z pozostałymi testami; naruszenie blokuje merge.
- Nowy pakiet: `TngTech.ArchUnitNET.xUnit`.
