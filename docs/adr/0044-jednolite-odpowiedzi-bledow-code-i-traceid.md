# ADR-0044: Jednolite odpowiedzi błędów: zawsze `code` i `traceId`

- **Status:** Zaakceptowany (doprecyzowuje ADR-0015 w zakresie odpowiedzi HTTP)
- **Data:** 2026-10-01
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §10, §11; ADR-0008, ADR-0015, ADR-0038, ADR-0039

## Kontekst

ADR-0015 ustala, że błędy biznesowe i walidacji zwracamy jako `ProblemDetails` ze stałym `code`. Część odpowiedzi błędów nie
powstaje jednak w naszym kodzie, tylko w ASP.NET Core:

- 400 z wiązania modelu (np. nieznana wartość enuma w zapytaniu w BFF, błędny JSON);
- 401 z uwierzytelnienia (brak lub nieważny token) i puste 403/404 uzupełniane przez `UseStatusCodePages`;
- 500 z `UseExceptionHandler`.

Takie odpowiedzi nie miały `code`, a `traceId` miał inny format (`00-{trace}-{span}-01`) niż w odpowiedziach z `Result`
(32 znaki hex). Klient (moduł, BFF innej experience) nie mógł rozgałęziać obsługi po `code`, a zespół wsparcia musiał rozpoznawać dwa
formaty identyfikatora przy szukaniu w Tempo i Loki.

## Decyzja

- **Każda** odpowiedź błędu API serwisu domenowego i BFF to `application/problem+json` z:
  - `status`, `title`, `instance` (ścieżka żądania);
  - `code`: stały, maszynowy kod błędu;
  - `traceId`: identyfikator śladu W3C, **32 znaki hex** (`Activity.Current.TraceId`), a bez aktywności `HttpContext.TraceIdentifier`.
- Odpowiedzi z naszego kodu ustawiają `code` z `Error.Code` (ADR-0015) lub własny kod frameworka (`auth.missing_scope`,
  `downstream.unavailable`, `downstream.timeout`), a `traceId` przez `ProblemDetailsConventions.TraceId`.
- Odpowiedzi tworzone przez ASP.NET Core uzupełnia konwencja `ProblemDetailsConventions.Apply`, rejestrowana przez
  `AddAppServiceDefaults` jako `CustomizeProblemDetails`. Istniejącego `code` nie nadpisuje; brakujący wyznacza ze statusu:

  | Status | `code` |
  |---|---|
  | 400 (żądania nie da się odczytać ani powiązać: zepsuty JSON, liczba w cudzysłowie, nieznany enum w zapytaniu, zły format daty) | `request.malformed` |
  | 401 | `auth.invalid_token` (brama bez nagłówka CSRF: `auth.csrf_header_missing`) |
  | 403 | `auth.forbidden` |
  | 404 | `http.not_found` |
  | 405 | `http.method_not_allowed` |
  | 415 | `http.unsupported_media_type` |
  | 429 | `http.too_many_requests` |
  | 5xx | `server.error` |
  | pozostałe | `http.{status}` |

- `request.malformed` (błąd klienta: żądanie niezgodne z kontraktem) celowo różni się od `validation.failed` (walidator: dane
  wpisane przez użytkownika łamią reguły). Klient rozróżnia je po samym `code`; szczegóły pól są w `errors` w obu przypadkach.
- BFF przekazuje odpowiedzi błędów serwisów domenowych bez zmian (ADR-0038), więc `code` i `traceId` serwisu trafiają do modułu.
  Dzięki propagacji `traceparent` `traceId` serwisu jest tym samym śladem co żądanie do BFF.
- Host API wywołuje `UseExceptionHandler()` i `UseStatusCodePages()` (szablony serwisu i BFF to robią), żeby puste odpowiedzi 401,
  403 i 404 dostały treść.

## Konsekwencje

- Klienci obsługują błędy w jeden sposób: rozgałęziają po `code`, a `traceId` pokazują w komunikacie dla wsparcia.
- Kody z tabeli są częścią kontraktu: zmiana kodu to zmiana łamiąca (ADR-0019).
- Brama YARP nie modyfikuje odpowiedzi przekazanych z BFF. Jej **własne** błędy mają ten sam format:
  - puste 401, 403, 404, 502 i 504 uzupełnia `GatewayStatusCodePages` (`UseStatusCodePages`); pomija odpowiedzi, które przeszły
    przez proxy, chyba że przekazanie się nie udało;
  - brak nagłówka CSRF daje `401 auth.csrf_header_missing`;
  - przekroczenie limitu żądań daje `429 http.too_many_requests` (`GatewayRateLimits`).

  Odpowiedź bramy rozpoznaje się po `instance` ze ścieżką bramy (`/api/{experience}/...`). Odpowiedzi BFF i serwisów mają ścieżkę
  bez tego prefiksu. Format błędów wspólnej bramy brzegowej to wymaganie wobec jej właściciela (ADR-0037).
- Testy: `ProblemDetailsConventionsTests` (kody i format `traceId`) oraz sprawdzenia E2E dla 401, 404 i 400 z wiązania w BFF.
