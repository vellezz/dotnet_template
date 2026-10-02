# ADR-0026: Handlery zapytań w Infrastructure

- **Status:** Zaakceptowany
- **Data:** 2026-09-29
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §6, §7; ADR-0002, ADR-0003, ADR-0015, ADR-0017, ADR-0020, ADR-0025

## Kontekst

Zapytania omijają domenę i czytają read modele przez `ReadDbContext` (ADR-0003). Application nie
może referować EF Core (ADR-0002), a wykonanie `IQueryable` wymaga metod rozszerzających EF.
Logika zapytań to w praktyce SQL, więc sensownie testuje się ją tylko integracyjnie; dodatkowy
port pomiędzy handlerem a `ReadDbContext` nie daje korzyści w testach, a zwiększa ilość kodu.

## Decyzja

- **Application** definiuje dla zapytania: `Query`, DTO wyniku i walidator
  (`Features/{Agregat}/{PrzypadekUzycia}/`).
- **Infrastructure** zawiera handler zapytania, w odpowiadającym folderze
  `Features/{Agregat}/{PrzypadekUzycia}/`, który używa bezpośrednio `ReadDbContext`
  (ciężkie raporty: dopuszczalny Dapper) i zwraca `Result<T>` (ADR-0015).
- Handlery zapytań `internal sealed`, rejestrowane skanowaniem assembly Infrastructure.
- Pipeline behaviors (logowanie, autoryzacja, walidacja; ADR-0017) działają tak samo jak dla
  komend, niezależnie od projektu, w którym leży handler.
- Cache (`HybridCache`, ADR-0020) stosowany w handlerze zapytania.
- Handlery komend pozostają w Application.

## Konsekwencje

- Application nie zależy od EF Core ani od cache; brak portów odczytu.
- Wycinek zapytania jest rozdzielony na dwa projekty (kontrakt w Application, handler
  w Infrastructure); spójność folderów ułatwia nawigację.
- Handlery zapytań testowane integracyjnie (Testcontainers, MSSQL).
- Test architektury (ADR-0025): handlery zapytań wyłącznie w Infrastructure, handlery komend
  wyłącznie w Application.
