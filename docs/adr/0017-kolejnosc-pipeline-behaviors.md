# ADR-0017: Kolejność pipeline behaviors

- **Status:** Zaakceptowany
- **Data:** 2026-09-29
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §6; ADR-0005, ADR-0010, ADR-0012, ADR-0015

## Kontekst

Komendy i zapytania przechodzą przez wspólny pipeline behaviors. Użytkownik bez uprawnień nie
powinien otrzymywać szczegółowych błędów walidacji, bo mogą one ujawniać reguły biznesowe
i istnienie zasobów.

## Decyzja

Kolejność behaviors:

1. **Logowanie / telemetria**
2. **Autoryzacja**: sprawdzenie scope (ADR-0012) i reguł niezależnych od stanu zasobu;
   odmowa jako `Result` z błędem `Forbidden` (ADR-0015).
3. **Walidacja** (FluentValidation): błędy jako `Result` z błędami `Validation`.
4. **Transakcja** (tylko komendy): commit tylko przy sukcesie.

## Konsekwencje

- Autoryzacja zależna od stanu zasobu (np. właściciel zamówienia) wymaga jego załadowania,
  więc odbywa się w handlerze lub w agregacie, po walidacji.
- Behavior transakcyjny pomija otwarcie transakcji, gdy już istnieje (outbox konsumenta, ADR-0005).
- Kolejność rejestracji behaviors w DI odpowiada powyższej liście i jest sprawdzana testem.
