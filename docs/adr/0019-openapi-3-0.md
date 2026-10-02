# ADR-0019: Kontrakty w OpenAPI 3.0

- **Status:** Zaakceptowany (doprecyzowany przez ADR-0039: dwa kontrakty BFF)
- **Data:** 2026-09-29
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §10; ADR-0009, ADR-0014

## Kontekst

Z kontraktów OpenAPI generowani są klienci na czterech platformach (Angular, Android, iOS, .NET
przez Refitter). Wersja specyfikacji musi być stabilnie obsługiwana przez wszystkie generatory.

## Decyzja

- Wszystkie kontrakty API w wersji **OpenAPI 3.0**.
- Wersja ustawiana centralnie w `SuperApp.Framework` dla wszystkich serwisów:
  ```csharp
  services.AddOpenApi(o => o.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0);
  ```
- Kontrakt generowany przy buildzie i commitowany do repo (ADR-0009).

- **Etykieta wersji w commitowanym kontrakcie: `3.0.3`.** `Microsoft.OpenApi` 2.x zapisuje dla OpenAPI 3.0 najnowszą łatkę `3.0.4`
  (październik 2024), która tylko doprecyzowuje tekst specyfikacji i nie zmienia struktury dokumentu. Część narzędzi (obsługa OpenAPI
  w JetBrains Rider, starsze generatory klientów) rozpoznaje wersje 3.0.x po dokładnej liście 3.0.0–3.0.3 i odrzuca `3.0.4`.
  Cel MSBuild `SetOpenApiContractVersionLabel` (`Directory.Build.targets`) po wygenerowaniu kontraktu ustawia `3.0.3`;
  treść dokumentu pozostaje bez zmian. Endpoint `/openapi/v1.json` działającej aplikacji podaje `3.0.4` (nie służy do generowania klientów).

## Konsekwencje

- Typy nullable opisywane w konwencji 3.0 (`nullable: true`), obsługiwanej przez wszystkie
  używane generatory.
- CI sprawdza, że commitowany kontrakt deklaruje wersję 3.0.
- Zmiana wersji specyfikacji w przyszłości = zmiana kontraktu wszystkich serwisów i nowy ADR.
