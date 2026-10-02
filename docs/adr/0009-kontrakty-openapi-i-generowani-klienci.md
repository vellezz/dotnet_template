# ADR-0009: Kontrakty OpenAPI i generowani klienci

- **Status:** Zaakceptowany (generator .NET: ADR-0014; wersja OpenAPI: ADR-0019; doprecyzowany przez ADR-0039: dwa kontrakty BFF, publiczny i wewnętrzny)
- **Data:** 2026-09-29
- **Decydenci:** zespół architektury (zapis przyjętych zasad architektury)
- **Powiązane:** zasady architektury §10; ADR-0014, ADR-0019

## Kontekst

Klienci na czterech platformach (Angular, Android, iOS, .NET) muszą korzystać z tego samego
kontraktu, a zmiany kontraktu muszą być widoczne w przeglądzie kodu.

## Decyzja

- Jeden kontrakt OpenAPI per serwis, generowany z API (`Microsoft.AspNetCore.OpenApi`),
  commitowany do repo i wersjonowany.
- Wersjonowanie API w ścieżce (`/v1/...`) lub przez Asp.Versioning.
- Generowani klienci: Angular (`ng-openapi-gen` / `openapi-generator` typescript-angular),
  Android (`openapi-generator` Kotlin), iOS (`swift-openapi-generator`), .NET: Refit generowany
  przez Refitter (ADR-0014).
- Klienci .NET rejestrowani przez `IHttpClientFactory` z resilience (ADR-0014).
- Kontrolery cienkie, błędy jako `ProblemDetails`.

## Konsekwencje

- Zmiana łamiąca kontrakt = nowa wersja API; CI powinien wykrywać różnice w pliku kontraktu.
- Kontrakty w OpenAPI 3.0 (ADR-0019).
