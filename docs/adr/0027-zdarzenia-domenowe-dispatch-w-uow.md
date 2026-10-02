# ADR-0027: Zdarzenia domenowe: dispatch w Unit of Work przez własny dispatcher

- **Status:** Zaakceptowany
- **Data:** 2026-09-30
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §6, §9; ADR-0002, ADR-0005, ADR-0010, ADR-0015, ADR-0017, ADR-0025

## Kontekst

Agregaty zgłaszają zdarzenia domenowe (`IDomainEvent`, `Raise(...)`). Wybrane zdarzenia są
tłumaczone na zdarzenia integracyjne i trafiają do outboxa (ADR-0005). Zmiana stanu agregatu
i wpis w outboxie muszą być zapisane atomowo. Przepływ ma być łatwy do prześledzenia
w debuggerze.

## Decyzja

### Mechanizm

- `SuperApp.Framework.Domain`: `IDomainEvent`; `AggregateRoot` z `Raise(...)`, `DomainEvents`,
  `ClearDomainEvents()`.
- `SuperApp.Framework.Application`: `IDomainEventHandler<TEvent>` i port `IDomainEventDispatcher`.
- `SuperApp.Framework.Infrastructure`:
  - implementacja `IDomainEventDispatcher`: pobiera handlery z DI (ten sam scope co
    `WriteDbContext`) i wywołuje je kolejno; każde zdarzenie logowane przez `[LoggerMessage]`
    i opatrzone spanem OTel,
  - **`WriteDbContextBase`** (implementuje `IUnitOfWork`), klasa bazowa `WriteDbContext`
    każdego serwisu.
- **Dispatch w `IUnitOfWork.SaveChangesAsync()`** klasy `WriteDbContextBase`:
  1. zebranie zdarzeń z agregatów śledzonych przez `ChangeTracker`,
  2. wyczyszczenie zdarzeń w agregatach,
  3. wywołanie handlerów przez `IDomainEventDispatcher`,
  4. `base.SaveChangesAsync()`: stan agregatu i wpisy outboxa w jednym zapisie, w transakcji
     otwartej przez `TransactionBehavior` (ADR-0017).
- Handlery zdarzeń domenowych leżą w Application i tłumaczą zdarzenia na zdarzenia integracyjne
  przekazywane do `IIntegrationEventPublisher` (outbox).
- MediatR służy wyłącznie do komend i zapytań (`ISender`); zdarzenia domenowe nie przechodzą
  przez MediatR.

### Reguły

1. Handler zdarzenia domenowego **nie modyfikuje innych agregatów** (jedna transakcja modyfikuje
   jeden agregat); skutki dla innych agregatów i kontekstów wyłącznie przez zdarzenia
   integracyjne.
2. Handler zdarzenia domenowego **nie wywołuje zapisu** (`SaveChangesAsync`); działa wewnątrz
   trwającego zapisu.
3. Zapis wyłącznie przez `IUnitOfWork.SaveChangesAsync()`; synchroniczne `SaveChanges` zabronione.
4. Zdarzenia są czyszczone z agregatów **przed** dispatchem, żeby ponowny zapis nie opublikował
   ich drugi raz.

### Wymuszenie: APP003 w `SuperApp.Analyzers`

Błąd kompilacji przy użyciu: `MediatR.INotification`, `MediatR.INotificationHandler<T>`,
`MediatR.IPublisher`, `MediatR.IMediator` (wstrzykiwany jest `ISender`) oraz synchronicznego
`DbContext.SaveChanges`.

## Konsekwencje

- Cały przepływ zapisu widoczny w jednej metodzie (`WriteDbContextBase.SaveChangesAsync`).
- Pipeline behaviors MediatR nie obejmują handlerów zdarzeń domenowych; działają one wewnątrz
  komendy, która przeszła już autoryzację i walidację.
- `TransactionBehavior` pomija otwarcie transakcji, gdy już istnieje (outbox konsumenta, ADR-0005).
- Handlery zdarzeń domenowych testowane z fake'iem `IIntegrationEventPublisher`; pełny przepływ
  (agregat → outbox) integracyjnie z Testcontainers.
