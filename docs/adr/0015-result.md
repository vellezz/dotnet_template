# ADR-0015: Sygnalizowanie naruszeń reguł przez `Result`

- **Status:** Zaakceptowany (doprecyzowany przez ADR-0044: `code` i `traceId` w każdej odpowiedzi błędu; ADR-0047: odczyt wyniku sprawdzany przez kompilator, bez `Value` i wyjątków)
- **Data:** 2026-09-29
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §6, §9, §10; ADR-0002, ADR-0005, ADR-0009, ADR-0017

## Kontekst

Agregaty, value objects i handlery muszą zgłaszać naruszenia reguł biznesowych i błędy walidacji
w sposób jawny, jednolity we wszystkich serwisach i przewidywalnie mapowany na odpowiedzi HTTP.

## Decyzja

- **`Result` / `Result<T>` w całym systemie** (wszystkie serwisy), typy we własnej implementacji
  w `SuperApp.Framework.Domain`, bez zewnętrznego pakietu.
- `Error` = `Code` (stały, np. `orders.order.already_shipped`), `Message`, `Type`:
  `Validation`, `NotFound`, `Conflict`, `Forbidden`, `BusinessRule`.
- Błędy definiowane statycznie per agregat (np. `OrderErrors.AlreadyShipped`); kody są częścią
  kontraktu API i nie zmieniają się.

### Warstwy

- **Domain:** metody agregatów, które mogą naruszyć regułę, zwracają `Result`; fabryki agregatów
  i value objects zwracają `Result<T>`.
- **Application:** komendy i zapytania zwracają `Result` / `Result<T>`.
  - `ValidationBehavior`: błędy FluentValidation zamieniane na `Result` z błędami typu
    `Validation` (bez wyjątków).
  - `AuthorizationBehavior`: odmowa jako błąd `Forbidden`.
  - `TransactionBehavior`: **commit tylko przy sukcesie**, przy błędzie rollback.
  - Behaviors tworzą nieudany `TResponse` przez statyczną fabrykę w interfejsie wyniku
    (static abstract member), bez refleksji.
- **Api:** jedno wspólne mapowanie `Error` → `ProblemDetails`:

  | `Error.Type` | HTTP |
  |---|---|
  | `Validation` | 400 (`ValidationProblemDetails`) |
  | `Forbidden` | 403 |
  | `NotFound` | 404 |
  | `Conflict` | 409 |
  | `BusinessRule` | 422 |

  Kod błędu w rozszerzeniu `code` odpowiedzi `ProblemDetails`.
- **Worker:** konsument otrzymujący nieudany `Result` nie rzuca wyjątku (błąd biznesowy nie jest
  ponawiany); loguje przez `[LoggerMessage]` i w razie potrzeby publikuje zdarzenie o niepowodzeniu.

### Wyjątki

Wyjątki wyłącznie dla błędów technicznych i programistycznych (niedostępność zależności, naruszony
kontrakt kodu). Obsługuje je globalny handler (500) oraz retry/redelivery w konsumentach.

- **Wyścigi na unikalnych indeksach** nie kończą się wyjątkiem (500): `IUnitOfWork.SaveChangesAsync` zwraca `Result`;
  naruszenie unikalnego indeksu lub klucza (SQL Server 2601/2627) staje się błędem `Conflict`. Kontekst zapisu serwisu mapuje nazwy
  indeksów na błędy swojego kontekstu (`UniqueConstraintErrors`, np. `IX_Categories_Slug` → `knowledge.category.slug_taken`), tak aby
  klient dostał ten sam błąd co przy sekwencyjnym wykryciu duplikatu; indeks bez mapowania daje `persistence.duplicate`.
  Zmiany nieudanego zapisu są porzucane (EF wycofuje zapis do punktu zapisu transakcji).

- **Konflikt współbieżności** (`rowversion` agregatu zmieniony przez inną komendę między odczytem a zapisem) w transakcji otwartej
  przez `TransactionBehavior` (żądanie HTTP) to błąd `Conflict` `persistence.concurrency_conflict` (409): klient odświeża dane
  i ponawia. W transakcji konsumenta MassTransit pozostaje wyjątkiem `DbUpdateConcurrencyException`, żeby wiadomość została
  ponowiona na świeżych danych, a nie potwierdzona jako błąd biznesowy.

- **Nieudana komenda w konsumencie** (`Result` z błędem w transakcji otwartej przez outbox MassTransit) porzuca swoje śledzone
  zmiany (`IUnitOfWork.DiscardChanges`), bo konsument po jej zakończeniu sam zapisuje i zatwierdza stan inboxu; bez tego częściowa
  zmiana agregatu zostałaby utrwalona razem z nim.

- **Odczyt wartości:** `Result<T>.TryGetValue(out value, out error)` sprawdza wynik i nazywa wartość w jednym kroku
  (`if (!Category.Create(...).TryGetValue(out var category, out var error)) return error;`). Zmienna przechowuje wtedy agregat,
  value object lub ID, a nie opakowujący je wynik, więc w kodzie nie ma łańcuchów w rodzaju `category.Value.Id.Value`.

## Konsekwencje / wymagania techniczne

- **APP001:** zignorowanie zwróconego `Result` jest błędem kompilacji; reguła w projekcie
  `src/Tools/SuperApp.Analyzers` (analizatory Roslyn uruchamiane przy kompilacji, podłączone do
  wszystkich projektów w `Directory.Build.props`), zgodna z zasadą warnings as errors.
- Testy domeny sprawdzają `Result` i kod błędu, nie typ wyjątku.
- Kody błędów dokumentowane w kontrakcie OpenAPI (ADR-0009).
