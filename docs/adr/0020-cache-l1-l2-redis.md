# ADR-0020: Cache dwupoziomowy (L1 w pamięci, L2 w Redis)

- **Status:** Zaakceptowany
- **Data:** 2026-09-29
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §2, §6, §7, §12; ADR-0003, ADR-0008

## Kontekst

Serwisy i bramy działają w wielu replikach. Powtarzalne odczyty (read modele, dane słownikowe,
odpowiedzi obcych kontekstów za ACL) obciążają MSSQL i inne serwisy. Cache musi przetrwać
chwilową niedostępność źródła danych i nie może dopuścić do lawiny równoległych przeliczeń
tego samego klucza.

## Decyzja

- **`HybridCache`** (`Microsoft.Extensions.Caching.Hybrid`):
  - **L1:** pamięć procesu,
  - **L2:** Redis (`Microsoft.Extensions.Caching.StackExchangeRedis`),
  - **ochrona przed stampede:** wbudowana w `GetOrCreateAsync` (jedno wywołanie fabryki
    na klucz w procesie).
- **Fail-safe:** własna implementacja w `SuperApp.Framework.Infrastructure` na `HybridCache`
  (`HybridCache` nie ma jej wbudowanej), opisana niżej.
- Serializacja L2: `System.Text.Json`.
- Konfiguracja (TTL, fail-safe, timeouty) wspólna dla serwisów, w `SuperApp.Framework`.

### Fail-safe: projekt

- Wartość w cache to koperta `{ Value, FreshUntil }`.
  Fizyczny TTL wpisu = czas świeżości + **maksymalny wiek przeterminowanej wartości**.
- Odczyt:
  1. `GetOrCreateAsync` z fabryką zwracającą kopertę (brak wpisu: normalna ścieżka
     z ochroną przed stampede).
  2. Wpis świeży (`FreshUntil > now`): zwracamy wartość.
  3. Wpis przeterminowany: odświeżenie pod lokalną blokadą per klucz (tylko jedno odświeżenie
     w procesie; pozostałe wywołania od razu dostają starą wartość). Sukces: `SetAsync` nowej
     koperty. **Błąd przejściowy** (timeout, błąd sieci, przejściowy błąd SQL): zwracamy starą
     wartość.
- Błędy nieprzejściowe (walidacja, 4xx, błędy programistyczne) nie uruchamiają fail-safe.
- Użycie fail-safe logowane przez `[LoggerMessage]` i zliczane w metryce OTel.
- API: metoda rozszerzająca / serwis w `SuperApp.Framework` (np. `GetOrCreateWithFailSafeAsync`),
  stosowana wszędzie tam, gdzie fail-safe jest pożądany.

### Gdzie wolno cache'ować

- Strona odczytu: handlery zapytań w Infrastructure (ADR-0026).
- ACL: odpowiedzi obcych kontekstów i systemów zewnętrznych (`I{Obcy}Gateway`).
- Bramy: dane pomocnicze (np. `/bff/user`), nigdy odpowiedzi proxowane z serwisów.
- **Nigdy** strona zapisu: agregaty i repozytoria zawsze czytają z `WriteDbContext`.
- Application nie zależy od cache; cache jest szczegółem Infrastructure.

### Klucze i unieważnianie

- Klucze z prefiksem serwisu i wersją schematu wartości: `{serwis}:{typ}:v{n}:{id}`.
- Unieważnianie tagami (`RemoveByTagAsync`) po zdarzeniach domenowych/integracyjnych
  zmieniających dane; TTL jest zabezpieczeniem, nie głównym mechanizmem spójności.
- **Unieważnianie dopiero po commicie:** handler zdarzenia domenowego rejestruje unieważnienie przez
  `IUnitOfWork.OnCommitted`; interceptor transakcji EF uruchamia je po zatwierdzeniu transakcji (także transakcji konsumenta
  MassTransit) i porzuca przy wycofaniu. Unieważnienie przed commitem pozwalało równoległemu odczytowi zapisać w cache stare dane.
  Błąd akcji po commicie jest logowany (EventId 210) i nie zmienia wyniku zatwierdzonej komendy.
- Zmiana kształtu cache'owanego typu = podbicie `v{n}` w kluczu.

## Konsekwencje / wymagania techniczne

- **Spójność L1 między replikami:** unieważnienie nie czyści L1 w pozostałych replikach.
  TTL w L1 musi być krótki (sekundy–minuty); dane wymagające natychmiastowej spójności
  nie trafiają do L1 (`HybridCacheEntryFlags.DisableLocalCache`).
- **Fail-safe świadomie zwraca nieaktualne dane** przez maksymalnie skonfigurowany wiek;
  wartość ustalana per typ danych.
- Własna implementacja fail-safe wymaga testów: równoległe odczyty przeterminowanego wpisu,
  błąd fabryki, przekroczenie maksymalnego wieku, awaria Redis.
- Redis (dostarczany przez inny dział, ADR-0016) w polityce `allkeys-lru`; przechowuje wyłącznie dane odtwarzalne.
- Awaria Redis nie może zatrzymać serwisu: krótki timeout L2, dalsza praca na L1 i źródle.
- Cache nie przechowuje tokenów ani sekretów; dane osobowe tylko z TTL zgodnym z polityką RODO.
- Redis: TLS, uwierzytelnienie (ACL), NetworkPolicy tylko dla serwisów, które go używają.
- Testy integracyjne z Redis w Testcontainers.
- Nowe pakiety w `Directory.Packages.props`: `Microsoft.Extensions.Caching.Hybrid`,
  `Microsoft.Extensions.Caching.StackExchangeRedis`.
