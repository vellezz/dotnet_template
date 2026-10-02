# ADR-0018: Sondy Kubernetes i health checki

- **Status:** Zaakceptowany
- **Data:** 2026-09-29
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §12; ADR-0004, ADR-0016, ADR-0020, ADR-0022

## Kontekst

Serwisy korzystają ze wspólnych zależności (MSSQL, RabbitMQ, Redis). Sonda readiness wyjmuje pod
z ruchu. Gdyby sprawdzała wspólne zależności, chwilowa awaria bazy wyjęłaby z ruchu jednocześnie
wszystkie repliki wszystkich serwisów i zamieniła się w pełną niedostępność aplikacji, blokując
także mechanizmy degradacji (cache z fail-safe, konfiguracja YARP w pamięci).

## Decyzja

| Sonda / endpoint | Sprawdza | Skutek niepowodzenia |
|---|---|---|
| **startupProbe** | MSSQL osiągalny; brak oczekujących migracji własnego kontekstu (ADR-0004); brama: konfiguracja YARP załadowana z bazy lub L2 (ADR-0022) | Pod nie dostaje ruchu; po limicie restart |
| **readinessProbe** | Wyłącznie stan poda: uruchomiony i nie w trakcie zamykania (po SIGTERM zwraca „not ready”) | Pod wyjęty z ruchu |
| **livenessProbe** | Wyłącznie responsywność procesu, bez zależności zewnętrznych | Restart poda |
| **`/health/dependencies`** | MSSQL, RabbitMQ, Redis | Nie jest sondą Kubernetes; odpytywany przez monitoring, alerty |

- Awaria zależności w trakcie działania obsługiwana przez aplikację: timeouty, circuit breaker,
  fail-safe cache, odpowiedź 503 z `ProblemDetails` tylko dla operacji wymagających zależności.
- Worker: startupProbe jak wyżej; brak ruchu HTTP, więc readiness nie wpływa na jego pracę;
  połączenie z RabbitMQ odtwarzane przez MassTransit.

## Konsekwencje

- Rollout nowej wersji zatrzymuje się, gdy baza jest niedostępna lub brakuje migracji
  (na prod: skrypt DBA nie został jeszcze uruchomiony); poprzednia wersja dalej obsługuje ruch.
- Awaria wspólnej zależności nie wyłącza całej aplikacji; sygnałem jest alert z
  `/health/dependencies`, nie zniknięcie podów z ruchu.
- Graceful shutdown: readiness „not ready” po SIGTERM, opóźnienie zamknięcia pozwalające
  Service i bramie przestać kierować ruch, potem zakończenie w `terminationGracePeriodSeconds`.
- Endpointy health bez uwierzytelnienia dostępne tylko wewnątrz klastra (nie wystawiane przez bramy).
