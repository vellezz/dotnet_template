# ADR-0039: API publiczne i wewnętrzne na poziomie BFF

- **Status:** Zaakceptowany (doprecyzowuje ADR-0009 i ADR-0019 w zakresie liczby i odbiorców kontraktów)
- **Data:** 2026-10-01
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §10; ADR-0009, ADR-0014, ADR-0019, ADR-0022, ADR-0037, ADR-0038, ADR-0040, ADR-0041

## Kontekst

ADR-0009 i ADR-0019 przewidują **jeden kontrakt OpenAPI per serwis**. Lokalna brama wystawia każdy serwis trasą
`/api/{serwis}/{**rest}` (`ProxyConfigurationSeed`), więc całe API serwisów domenowych jest dziś publiczne.

W modelu experience (ADR-0038) podział na API publiczne i wewnętrzne przebiega na poziomie **BFF** (decyzja właściciela projektu).
Konsumentami API wewnętrznego są BFF-y innych experience, np. dashboard, który pokazuje widget naszej experience.

## Decyzja

| API | Ścieżka | Konsument | Dostępność | Kontrakt OpenAPI |
|---|---|---|---|---|
| **Publiczne BFF** | `/v{n}/...` | wyłącznie moduł naszej experience | przez bramę brzegową | `{Experience}.Bff.public.json`, generowany klient modułu |
| **Wewnętrzne BFF** | `/internal/v{n}/...` | BFF-y innych experience | tylko w klastrze (ADR-0041), nigdy przez bramę | `{Experience}.Bff.internal.json`, klient Refitter u konsumenta |
| **Serwisu domenowego** | `/v{n}/...` | BFF i serwisy **naszej** experience | tylko w klastrze (ADR-0041), nigdy przez bramę | `{Serwis}.Api.json` (dotychczasowy), klient Refitter w BFF |

- **Brama brzegowa** kieruje ruch tylko na `/api/{experience}/v{n}/**` do API publicznego BFF (wymaganie wobec wspólnej bramy,
  ADR-0037). Trasy do serwisów domenowych i do `internal` są zabronione. Lokalny zamiennik dostaje test, który to sprawdza.
- **API publiczne:**
  - ma jednego konsumenta, nasz moduł; ten sam zespół jest producentem i konsumentem;
  - zmian łamiących i tak unikamy, bo wersje modułu w sklepie żyją długo;
  - zmiana łamiąca oznacza nowe `/v{n+1}`, a poprzednia wersja zostaje do wygaszenia.
- **API wewnętrzne:**
  - to kontrakt z innymi zespołami: zmiany wyłącznie wstecznie zgodne, a zmiana łamiąca oznacza nową wersję i uzgodniony termin
    wygaszenia starej;
  - operacje są projektowane pod konsumenta (np. „widget postępu”), nie jako ogólny dostęp do domeny;
  - wymaga scope `{experience}.internal.*` (ADR-0040).
- **Kontrakty serwisów domenowych** zostają (ADR-0019), ale są wewnętrzne naszej experience. Mogą ewoluować razem z BFF w jednym
  wydaniu, choć zasada expand/contract nadal obowiązuje przy rolling update.
- **Dokumenty** generowane z kodu (Microsoft.AspNetCore.OpenApi) jako dwa dokumenty BFF z podziałem po ścieżce. Commitowane jak
  dotychczas.
- **CI wykrywa zmiany łamiące** w kontraktach publicznym i wewnętrznym BFF, porównując je z gałęzią główną. Wymaga pipeline'u, którego
  w repozytorium jeszcze nie ma.

## Konsekwencje

- Klient modułu zależy tylko od kontraktu BFF, a nie od kontraktów serwisów. Serwisy domenowe mogą się zmieniać bez wpływu na aplikację
  w sklepie.
- Inne experience mają jawny, wersjonowany i wąski kontrakt zamiast dostępu do naszych serwisów.
- **Do zrobienia:**
  - generowanie dwóch dokumentów w szablonie BFF;
  - zmiana tras lokalnej bramy (pakiet B2 raportu);
  - `securitySchemes` (Bearer) w dokumentach;
  - krok CI.
- **Stan przejściowy:** do czasu powstania BFF kontrakty serwisów Knowledge i SleepDiary są wystawione przez lokalną bramę (ADR-0038).
