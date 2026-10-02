# ADR-0043: Dopiski o doprecyzowaniu w nagłówkach zaakceptowanych ADR

- **Status:** Zaakceptowany (zmienia ADR-0001)
- **Data:** 2026-10-01
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §14, §15; ADR-0001

## Kontekst

ADR-0001 zakazuje merytorycznej edycji zaakceptowanego ADR; zmianę decyzji zapisuje nowy ADR, który **zastępuje** stary
(status „Zastąpiony przez ADR-XXXX”). ADR-y 0037–0042 nie zastępują jednak starszych decyzji, tylko je **doprecyzowują**: zawężają
zakres (np. brama jest lokalnym zamiennikiem wspólnej bramy), dodają przypadek (API wewnętrzne BFF) albo ustalają szczegół
(scope wywołań systemowych). Stare ADR-y pozostają ważne.

Informacja o doprecyzowaniu była zapisana tylko w nowym ADR. Osoba, która czyta stary ADR (np. ADR-0012 o tokenach), nie wiedziała,
że część jego treści wymaga czytania razem z nowszym ADR-em, i mogła wdrożyć wersję sprzed doprecyzowania.

## Decyzja

- Obok statusów z ADR-0001 dopuszczamy w nagłówku zaakceptowanego ADR **dopisek w nawiasie przy statusie**:
  `Zaakceptowany (doprecyzowany przez ADR-XXXX: zakres)`. Kilka dopisków oddzielamy średnikiem.
- Dopisek jest jedyną dozwoloną zmianą zaakceptowanego ADR poza poprawkami redakcyjnymi (literówki, martwe odnośniki). Treść
  sekcji „Kontekst”, „Decyzja” i „Konsekwencje” nadal się nie zmienia.
- Nowy ADR, który doprecyzowuje starszy, ma w nagłówku `Zaakceptowany (doprecyzowuje ADR-XXXX …)` i **w tej samej zmianie** dodaje
  dopisek w nagłówku doprecyzowanego ADR oraz w kolumnie statusu indeksu `docs/adr/README.md`.
- Gdy doprecyzowanie zmienia decyzję tak, że stary ADR wprowadzałby w błąd, nie stosujemy dopisku: nowy ADR **zastępuje** stary
  (reguła ADR-0001 bez zmian).

## Konsekwencje

- Czytając dowolny ADR, od razu widać, które nowsze ADR-y trzeba przeczytać razem z nim.
- Historia decyzji zostaje nienaruszona: dopisek nie zmienia treści, tylko wskazuje kontynuację.
- Dopiski dodano do ADR-ów doprecyzowanych przez ADR-0037–0044 (0001, 0006, 0007, 0009, 0011, 0012, 0013, 0014, 0015, 0016, 0019,
  0022, 0031, 0036, 0040).
