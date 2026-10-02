# ADR-0014: Klienci HTTP .NET: Refit generowany z OpenAPI przez Refitter

- **Status:** Zaakceptowany (doprecyzowany przez ADR-0040: przekazywanie tokenu użytkownika; ADR-0042: client credentials tylko systemowo)
- **Data:** 2026-09-29
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §2, §3, §6, §10; ADR-0009, ADR-0011, ADR-0015, ADR-0019

## Kontekst

Synchroniczne wywołania serwis ↔ serwis i systemów zewnętrznych są wyjątkiem (domyślnie zdarzenia)
i zawsze przechodzą przez ACL w Infrastructure. Klienci mają być generowani z kontraktu OpenAPI,
tak żeby zmiana kontraktu była wykrywana przy kompilacji, i mieć resilience (circuit breaker,
retry, timeouty).

## Decyzja

- **Refit** jako klient HTTP w .NET: interfejs z atrybutami, implementacja generowana przez
  source generator Refit.
- **Refitter** generuje interfejsy Refit i DTO z **commitowanego pliku OpenAPI dostawcy**:
  - konfiguracja w pliku `.refitter` obok projektu `{Serwis}.Infrastructure` konsumenta,
  - generowane tylko operacje używane przez konsumenta (filtrowanie po tagu/ścieżce),
  - typy `internal`, `CancellationToken` w każdej metodzie,
  - odpowiedzi jako `ApiResponse<T>`, mapowane w ACL na `Result` (ADR-0015).
- Wygenerowany kod commitowany do repo; zmiana kontraktu dostawcy = aktualizacja kopii kontraktu
  + regeneracja, widoczna w diffie.
- **Rejestracja:** `IHttpClientFactory` (`AddRefitClient<T>()`):
  - resilience: `AddStandardResilienceHandler()` (circuit breaker, retry, timeouty) z
    `Microsoft.Extensions.Http.Resilience`; retry wyłącznie dla operacji idempotentnych,
  - token client credentials dołączany przez `DelegatingHandler` (ADR-0011).
- Wygenerowane interfejsy i DTO są używane wyłącznie w implementacji portu ACL
  (`I{Obcy}Gateway`); obce DTO nie wychodzą poza Infrastructure.

## Konsekwencje / wymagania techniczne

- **Zmiany łamiące po stronie dostawcy** wykrywane w jego CI przez porównanie nowego kontraktu
  OpenAPI z poprzednim; zmiana łamiąca bez nowej wersji API blokuje merge.
- Kontrakty w OpenAPI 3.0 (ADR-0019), w pełni obsługiwanym przez Refitter.
- Testy ACL: fake wygenerowanego interfejsu (mapowanie, obsługa błędów); integracyjnie przez
  `WebApplicationFactory` dostawcy lub zaślepkę HTTP.
- Nowe pakiety w `Directory.Packages.props`: `Refit`, `Refit.HttpClientFactory`,
  `Refitter.SourceGenerator` (lub narzędzie CLI `refitter` w `dotnet-tools.json`),
  `Microsoft.Extensions.Http.Resilience`.
