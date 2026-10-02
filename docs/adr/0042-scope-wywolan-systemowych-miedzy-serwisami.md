# ADR-0042: Scope systemowych wywołań między serwisami jednej experience

- **Status:** Zaakceptowany (uzupełnia ADR-0040)
- **Data:** 2026-10-01
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §5; ADR-0012, ADR-0014, ADR-0038, ADR-0040, ADR-0041

## Kontekst

ADR-0040 dopuszcza wywołania **systemowe** (bez użytkownika, token client credentials) jako wyjątek. Nazwę scope określa tylko dla
wywołań API wewnętrznego BFF przez BFF innej experience: `{experience}.internal.system.*`.

Brakuje reguły dla wywołania systemowego **między serwisami domenowymi tej samej experience**. Przykład: nocne zadanie serwisu planów
pyta serwis katalogu o listę aktywnych ćwiczeń. Bez reguły każdy serwis nazwałby taki dostęp inaczej.

## Decyzja

- Wywołanie systemowe serwis → serwis w obrębie experience wymaga scope **`{serwis}.system.{akcja}`**, gdzie `{serwis}` to serwis
  docelowy, np. `katalog.system.read`. Format jest zgodny z konwencją `{serwis}.{zasób}.{akcja}` (ADR-0012); zasobem jest `system`.
- Operacje dostępne systemowo:
  - są jawnie oznaczone: `[RequiresScope("{serwis}.system.{akcja}")]` na komendzie lub zapytaniu, stała w `{Serwis}Scopes`;
  - mają w dokumentacji uzasadnienie, dlaczego nie działają w kontekście użytkownika;
  - nigdy nie zwracają danych konkretnej osoby wskazanej wyłącznie identyfikatorem z payloadu (ADR-0040).
- Scope `{serwis}.system.*` dostaje **tylko** klient techniczny (`{serwis}-client`) serwisów tej samej experience. Nie dostają go
  klienci użytkowników (web, mobile) ani klienci innych experience; to wymaganie wobec działu CIAM (ADR-0016).
- Odbiorca loguje wołającego (`azp`) przy każdym wywołaniu systemowym (audyt).
- Pozostałe reguły ADR-0040 bez zmian: domyślnie wywołanie w kontekście użytkownika z przekazanym tokenem; między experience tylko
  przez API wewnętrzne BFF (`{experience}.internal.system.*` dla wywołań systemowych).

## Konsekwencje

- Jedna, przewidywalna konwencja; operacje systemowe łatwo wyszukać i przejrzeć (grep po `.system.`).
- Każdy nowy scope systemowy wymaga zgłoszenia do działu CIAM (przypisanie do klientów technicznych).
- Lokalny realm dostaje scope `{serwis}.system.*` przy pierwszym takim wywołaniu (dziś żadne nie istnieje).
