# ADR-0012: Jeden token z listą audience, uprawnienia przez scope

- **Status:** Zaakceptowany (doprecyzowany przez ADR-0040: token użytkownika w wywołaniach BFF; ADR-0042: scope wywołań systemowych)
- **Data:** 2026-09-29
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §3, §4, §5; ADR-0006, ADR-0007, ADR-0016, ADR-0017

## Kontekst

Bramy przekazują do serwisów access token użytkownika, a każdy serwis sam waliduje JWT
(ADR-0007). Ruch HTTP do serwisów jest dopuszczony tylko z namespace'u bram (NetworkPolicy),
a wywołania serwis ↔ serwis używają własnych tokenów client credentials.

## Decyzja

- **Jeden access token z listą audience wszystkich serwisów**, do których klient ma dostęp.
  Audience i scope konfiguruje dział CIAM (ADR-0016).
- Bramy przekazują token do serwisów bez zmian.
- **Podział ról w tokenie:**
  - **audience** = do jakiego API jest token; każdy serwis akceptuje wyłącznie tokeny
    zawierające jego audience (`{serwis}-api`),
  - **scope** = co wolno zrobić; konwencja nazw `{serwis}.{zasób}.{akcja}`
    (np. `orders.order.read`, `orders.order.write`).
- **Autoryzacja:**
  - brama: polityki per trasa oparte o scope (gruboziarniście, ADR-0006),
  - serwis: ponowna weryfikacja scope oraz reguły drobnoziarniste (zasób, właściciel, reguły
    biznesowe) w `AuthorizationBehavior` (ADR-0017).
- Nazwy scope'ów i polityk zdefiniowane w kodzie jako stałe per serwis.

## Konsekwencje

- Brak dodatkowych wywołań do CIAM i cache tokenów per serwis w bramach.
- Użycie przechwyconego tokenu bezpośrednio wobec innego serwisu blokuje NetworkPolicy;
  ryzyko dodatkowo ograniczają krótki czas życia access tokenu i zakaz logowania tokenów (ADR-0008).
- Dodanie serwisu lub scope'u wymaga zgłoszenia działowi CIAM.
- Token użytkownika nie opuszcza granicy zaufania aplikacji; przekazanie go do systemów
  zewnętrznych wymaga nowego ADR.
