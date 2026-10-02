# ADR-0003: Rozdzielone `WriteDbContext` i `ReadDbContext`

- **Status:** Zaakceptowany
- **Data:** 2026-09-29
- **Decydenci:** zespół architektury (zapis przyjętych zasad architektury)
- **Powiązane:** zasady architektury §7; ADR-0002, ADR-0004, ADR-0016

## Kontekst

Strona zapisu potrzebuje bogatego modelu domeny i śledzenia zmian; strona odczytu potrzebuje
płaskich projekcji pod ekran/API, bez kosztu materializacji agregatów.

## Decyzja

- `WriteDbContext`: encje domenowe, konfiguracja wyłącznie w `IEntityTypeConfiguration<T>`,
  implementuje `IUnitOfWork`, **jedyny właściciel schematu i migracji**.
- `ReadDbContext`: własne klasy read modeli (nigdy encje domenowe), `NoTracking`,
  osobny connection string z loginem tylko do odczytu, **bez migracji**.
- Handlery zapytań w Infrastructure używają bezpośrednio `ReadDbContext` (ADR-0026).
- Ciężkie raporty: dopuszczalny Dapper w handlerze zapytania.

## Konsekwencje

- Widoki mapowane przez `ReadDbContext` (`ToView`) są tworzone **migracjami `WriteDbContext`**
  (surowy SQL w migracji) i podlegają zasadom expand/contract (ADR-0004).
- Zmiana schematu wymaga przeglądu read modeli: `ReadDbContext` nie wykryje niezgodności
  w czasie budowania; potrzebne testy integracyjne (Testcontainers) dla zapytań.
- `ApplicationIntent=ReadOnly` z repliką do odczytu (jeśli dostarczona, ADR-0016) oznacza **opóźnienie
  replikacji**: brak gwarancji read-your-writes. Ekrany wymagające natychmiastowej spójności
  po komendzie czytają z primary lub zwracają dane z odpowiedzi komendy.
