# ADR-0021: Wspólna baza MSSQL, osobny schemat per mikroserwis

- **Status:** Zaakceptowany
- **Data:** 2026-09-29
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §3, §6, §7, §8; ADR-0002, ADR-0003, ADR-0004, ADR-0005, ADR-0013, ADR-0016

## Kontekst

Każdy bounded context ma własne dane i żaden inny kontekst nie może do nich sięgać bezpośrednio.
ADR-0002 dopuszczał bazę albo schemat per serwis. Ta decyzja wybiera wariant.

## Decyzja

- **Jedna baza MSSQL na środowisko, osobny schemat per mikroserwis** (np. `orders`, `billing`),
  plus schemat `gateway` dla bram (ADR-0013).
- Wszystkie obiekty serwisu (tabele domenowe, widoki read modeli, outbox/inbox, sagi, sekwencje,
  historia migracji) leżą **wyłącznie** w jego schemacie.
- **Zakaz dostępu między schematami:** brak zapytań, joinów, widoków, kluczy obcych, synonimów
  i procedur odwołujących się do cudzego schematu. Dane innych kontekstów tylko przez zdarzenia
  lub ACL.
- Izolację wymuszają uprawnienia, nie tylko konwencja.

### Użytkownicy i uprawnienia

| Użytkownik | Liczba | Uprawnienia |
|---|---|---|
| `superapp_migrator` | 1 | `db_ddladmin` + `db_datareader` + `db_datawriter` (DDL i migracje danych we wszystkich schematach); używany wyłącznie przez `SuperApp.Migrator`; **tylko dev i test**, na prod migracje uruchamia DBA (ADR-0004) |
| `{serwis}_app` | 1 per serwis + brama | członek roli `{serwis}_role`: `SELECT, INSERT, UPDATE, DELETE, EXECUTE` na własnym schemacie |

- Uprawnienia nadawane rolom bazodanowym, nie użytkownikom bezpośrednio.
- `WriteDbContext` i `ReadDbContext` serwisu używają tego samego użytkownika; do repliki
  odczytowej kieruje `ApplicationIntent=ReadOnly` w connection stringu `ReadDbContext`.
- Żaden użytkownik serwisu nie ma uprawnień do innych schematów ani ról `db_owner`/`db_datareader`/`db_datawriter`.
- Izolacja DDL między schematami nie jest wymuszana uprawnieniami (wspólny `superapp_migrator`);
  pilnuje jej review migracji.

### EF Core

- `modelBuilder.HasDefaultSchema("{serwis}")` w `WriteDbContext` i `ReadDbContext`.
- Historia migracji w schemacie serwisu:
  ```csharp
  services.AddDbContext<WriteDbContext>(o => o.UseSqlServer(cs.Write, x => x
      .MigrationsHistoryTable("__EFMigrationsHistory", "{serwis}")));
  ```
  Bez tego wszystkie serwisy dzieliłyby tabelę `dbo.__EFMigrationsHistory` i nadpisywały
  sobie historię.
- Tabele outbox/inbox i sag konfigurowane w `WriteDbContext`, więc dziedziczą domyślny schemat.

## Konsekwencje / wymagania techniczne

- **Skrypt bootstrap** (DBA / pipeline infrastruktury), generowany z listy serwisów, zakłada
  schematy, role, użytkowników i nadaje uprawnienia przed pierwszym uruchomieniem Migratora.
  Dodanie serwisu = jeden wpis na liście.
- **Wspólne zasoby:** serwisy dzielą CPU, pamięć, tempdb, log transakcji i mechanizm HA środowiska.
  Ciężki serwis może spowolnić pozostałe; potrzebny monitoring per schemat/login i ewentualnie
  Resource Governor.
- **Backup i restore** obejmują całą bazę: przywrócenie jednego serwisu do punktu w czasie
  cofa wszystkie. Odtworzenie danych jednego serwisu wymaga restore do osobnej bazy i
  selektywnego przeniesienia.
- **Wyodrębnienie serwisu do osobnej bazy** pozostaje tanie, dopóki obowiązuje zakaz dostępu
  między schematami: zmienia się connection string, schemat może zostać ten sam.
- Testy integracyjne: jeden kontener MSSQL (Testcontainers), schematy, role i użytkownicy tworzeni tym samym
  skryptem bootstrap co na środowiskach, żeby testy wykrywały braki uprawnień.
- Testy architektury/przegląd migracji: surowy SQL w migracjach nie może odwoływać się do
  innych schematów.
