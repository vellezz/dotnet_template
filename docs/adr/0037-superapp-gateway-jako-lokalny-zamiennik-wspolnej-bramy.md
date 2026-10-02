# ADR-0037: `SuperApp.Gateway` jako lokalny zamiennik wspólnej bramy brzegowej

- **Status:** Zaakceptowany (doprecyzowuje ADR-0006, ADR-0011, ADR-0013, ADR-0022 i ADR-0036 w zakresie własności bramy)
- **Data:** 2026-10-01
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §3, §4, §12; ADR-0006, ADR-0011, ADR-0012, ADR-0013, ADR-0016, ADR-0022, ADR-0034, ADR-0036,
  ADR-0038; raport `docs/analizy/2026-10-01-weryfikacja-wzgledem-notatki-ze-spotkania.md`

## Kontekst

ADR-0006, 0011, 0013 i 0022 opisują `SuperApp.Gateway` (YARP) jako bramę wdrażaną przez nasz zespół. Profil `bff-web` zamienia sesję w
ciasteczku na token. Profil `gateway-mobile` waliduje JWT.

Ustalenia architektoniczne organizacji (notatka ze spotkania, decyzja właściciela projektu P1) mówią co innego:

- **Super app** to całe rozwiązanie organizacji.
- **Brama brzegowa** (edge gateway = API gateway) jest **wspólna dla całej super appki** i leży **poza zakresem experience**.
  Odpowiada za:
  - zamianę sesji na JWT;
  - walidację JWT;
  - CSRF;
  - unieważnianie tokenów po wylogowaniu.
- My dostarczamy jedną experience: moduł, BFF i serwisy domenowe (ADR-0038).

## Decyzja

- **Brama brzegowa nie jest artefaktem wdrożenia naszej experience.** Na środowiskach dev, test i prod ruch do naszego BFF przychodzi
  ze wspólnej bramy organizacji.
- **`SuperApp.Gateway` zostaje w repozytorium jako lokalny zamiennik wspólnej bramy:**
  - działa w docker compose (ADR-0034), w testach E2E i przy pracy z IDE;
  - pozwala uruchomić pełny przepływ moduł → brama → BFF → serwisy bez infrastruktury organizacji.
- **Zachowanie `SuperApp.Gateway` jest specyfikacją wymagań wobec wspólnej bramy.** Przekazujemy je właścicielowi bramy (jak wymagania
  wobec innych działów, ADR-0016):
  - brak tokenów w przeglądarce; sesja po stronie serwera, ciasteczko `__Host-`, HttpOnly, Secure, SameSite=Strict (ADR-0011, 0013);
  - CSRF dla wywołań API z przeglądarki;
  - wylogowanie z kontrolą `sid` i OIDC back-channel logout;
  - usuwanie nagłówków `Cookie` i `X-User-*`, dołączanie `Authorization: Bearer` z tokenem użytkownika (ADR-0012);
  - walidacja JWT (issuer, audience, podpis, czas z tolerancją `ClockSkew` 30 s) dla klientów mobilnych;
  - kierowanie ruchu modułu **wyłącznie do API publicznego BFF** (`/api/{experience}/v{n}/**`), nigdy do serwisów domenowych ani do
    ścieżek `internal` (ADR-0039);
  - unieważnianie tokenów po wylogowaniu (np. denylista po `sid` albo introspekcja dla tras wrażliwych);
  - limit żądań z partycją po rzeczywistym kliencie (`X-Forwarded-For` z zaufanego Ingressu), a nie po adresie Ingressu;
  - opcjonalnie proxy analityki `/ingest` do PostHog Cloud EU (ADR-0036). Jeśli wspólna brama go nie zapewni, przenosimy
    identyfikator analityczny do naszego BFF (`GET /v1/analytics/id`), a `/ingest` do konfiguracji modułu.
- **Chart `superapp-gateway` i migracje schematu `gateway`** pozostają wzorcem i narzędziem lokalnym. Nie są częścią wydania experience,
  dopóki właściciel wspólnej bramy nie zdecyduje inaczej.
- Zmiany w lokalnej bramie są dopuszczalne tylko wtedy, gdy odwzorowują wspólną bramę albo wymaganie wobec niej. Nie dodajemy do
  niej logiki experience (ta należy do BFF, ADR-0038).

## Konsekwencje

- **Plusy:**
  - zespół nie utrzymuje komponentu brzegowego na produkcji;
  - lokalnie pełny przepływ działa bez zależności od organizacji;
  - wymagania wobec wspólnej bramy są wykonywalne, bo potwierdzają je testy E2E lokalnej bramy.
- **Ryzyka:**
  - rozjazd zachowania lokalnej i wspólnej bramy (inne nagłówki, CSRF, czasy). Środek zaradczy: lista wymagań powyżej uzgodniona z
    właścicielem bramy, a różnice opisane w przewodniku (rozdział 13);
  - ADR-0036 zakłada `/ingest` w naszej bramie: do potwierdzenia z właścicielem wspólnej bramy.
- **Do zrobienia:**
  - przekazać wymagania właścicielowi wspólnej bramy;
  - ~~w lokalnej bramie zastąpić trasy do serwisów domenowych trasą do BFF experience~~ zrobione: trasa
    `/api/{experience}/v{version:int}/{**rest}` do BFF experience (migracja `RouteExperienceThroughBff`).
