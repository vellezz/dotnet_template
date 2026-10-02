# ADR-0013: Session store BFF i klucze Data Protection w MSSQL

- **Status:** Zaakceptowany (doprecyzowany przez ADR-0037: własność bramy)
- **Data:** 2026-09-29
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §4, §8, §12; ADR-0004, ADR-0006, ADR-0011, ADR-0016, ADR-0020

## Kontekst

BFF przechowuje sesje po stronie serwera (`ITicketStore`), żeby ciasteczko było małe, a sesję
dało się unieważnić. Wszystkie repliki bram muszą współdzielić klucze Data Protection.
Oba rodzaje danych muszą być trwałe: ich utrata oznacza wylogowanie wszystkich użytkowników.
Redis służy wyłącznie jako cache danych odtwarzalnych (ADR-0020).

## Decyzja

- **Session store BFF:** własna implementacja `ITicketStore` na MSSQL.
- **Klucze Data Protection:** MSSQL (`PersistKeysToDbContext`), wspólna nazwa aplikacji
  dla wszystkich replik danej bramy.
- Dane bramy (sesje, klucze, konfiguracja YARP z ADR-0022) w osobnym schemacie `gateway` z własnym `DbContext` w `SuperApp.Gateway`;
  migracje uruchamia `SuperApp.Migrator` (ADR-0004).

## Konsekwencje / wymagania techniczne

- **Szyfrowanie:** ticket zawiera access i refresh token, więc przed zapisem jest szyfrowany
  (`IDataProtector`); klucze Data Protection szyfrowane w spoczynku (`ProtectKeysWith*`,
  certyfikat z Vault).
- **Wydajność:** odczyt sesji przy każdym żądaniu do `/api/*`. Tabela z kluczem głównym
  na identyfikatorze sesji. Ewentualny cache ticketu tylko w L1 (bez Redis), z TTL ≤ 60 s;
  opóźnia to unieważnienie sesji maksymalnie o ten TTL.
- **Wygasłe sesje:** kolumna z czasem wygaśnięcia + cykliczne sprzątanie (zadanie w tle bramy
  z blokadą, żeby nie uruchamiały go wszystkie repliki jednocześnie).
- **Indeks po `sid`/`sub`** na potrzeby OIDC Back-Channel Logout z CIAM (ADR-0011).
- **Współbieżność między replikami:** zapis istniejącej sesji (odnowienie ticketu, odświeżenie
  tokenów, ADR-0011) odbywa się w krótkiej transakcji z wyłączną blokadą aplikacyjną tej sesji
  (`sp_getapplock`, zasób `gateway-session:` + skrót klucza sesji, oczekiwanie do 15 s), tym samym
  mechanizmem co sprzątanie sesji. Odnowienie ticketu nigdy nie cofa tokenów: jeśli zapisane tokeny
  wygasają później niż zapisywane (inna replika je odświeżyła), zostają zapisane tokeny.
- **Dostępność:** awaria MSSQL blokuje logowanie i obsługę `/api/*` w BFF; HA zapewnia
  dostawca środowiska MSSQL (ADR-0016).
- Osobny użytkownik bramy (`gateway_app`) z uprawnieniami tylko do jej schematu (ADR-0021).
- Nowy pakiet: `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`.
