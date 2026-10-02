# ADR-0005: Komunikacja asynchroniczna przez RabbitMQ z outbox i inbox

- **Status:** Zaakceptowany (wybór wersji biblioteki: patrz ADR-0035)
- **Data:** 2026-09-29
- **Decydenci:** zespół architektury (zapis przyjętych zasad architektury)
- **Powiązane:** zasady architektury §6, §9; ADR-0010

## Kontekst

Konteksty muszą wymieniać informacje bez ścisłego sprzężenia czasowego. Zmiana stanu agregatu
i publikacja zdarzenia muszą być atomowe.

## Decyzja

- Domyślnie zdarzenia integracyjne (publish/subscribe) przez RabbitMQ (dostarczany przez inny
  dział, ADR-0016), **quorum queues**.
- Zdarzenia integracyjne w `{Serwis}.Contracts` = published language, wersjonowany pakiet NuGet.
- **Outbox** (EF, `WriteDbContext`) po stronie publikującej, **inbox/idempotencja** po stronie
  konsumenta.
- Publikacja przez port `IIntegrationEventPublisher` (wymiana biblioteki bez zmian w Application).
- Zdarzenia domenowe dispatchowane w `IUnitOfWork.SaveChangesAsync()` przed zapisem, w tej samej
  transakcji (ADR-0027).
- Sagi (state machine) z persystencją w MSSQL dla procesów wieloetapowych.
- Konsumenci w `{Serwis}.Worker`, cienkie, delegują do komendy.
- Retry/redelivery jawnie; błędy trwałe do kolejki `_error` z alertem.

## Konsekwencje / wymagania techniczne

- **Transakcje:** outbox konsumenta sam otwiera transakcję na `WriteDbContext`. Behavior
  transakcyjny komend musi pomijać otwarcie transakcji, gdy `Database.CurrentTransaction != null`,
  inaczej konflikt przy obsłudze komendy z konsumenta.
- Handlery zdarzeń domenowych nie wywołują zapisu i nie modyfikują innych agregatów; jedynie
  przekazują zdarzenia integracyjne do outboxa (ADR-0027).
- Eventual consistency między kontekstami musi być uwzględniona w UX i testach.
