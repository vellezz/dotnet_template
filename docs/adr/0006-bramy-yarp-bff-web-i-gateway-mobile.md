# ADR-0006: Bramy YARP: BFF web i gateway mobile

- **Status:** Zaakceptowany (implementacja BFF: ADR-0011; session store: ADR-0013; doprecyzowany przez ADR-0037: bramy to lokalny zamiennik wspólnej bramy brzegowej)
- **Data:** 2026-09-29
- **Decydenci:** zespół architektury (zapis przyjętych zasad architektury)
- **Powiązane:** zasady architektury §3, §4; ADR-0007, ADR-0011, ADR-0012, ADR-0013

## Kontekst

Klienci web nie powinni przechowywać tokenów w przeglądarce; klienci mobilni są public
clients OIDC. Klienci nie mogą wołać mikroserwisów bezpośrednio.

## Decyzja

- Jeden projekt `SuperApp.Gateway`, dwa profile i dwa Deploymenty: `bff-web`, `gateway-mobile`.
- **BFF web:** cookie + OIDC (Authorization Code + PKCE, confidential client `bff-web`),
  ciasteczko `HttpOnly`, `Secure`, `SameSite=Strict`; server-side session store; wspólne klucze
  Data Protection; odświeżanie tokenów własną implementacją (ADR-0011);
  transform dołącza `Authorization: Bearer`, usuwa `Cookie`; CSRF przez `X-CSRF: 1`.
- **Gateway mobile:** `AddJwtBearer`, przekazuje bearer token dalej.
- Autoryzacja gruboziarnista w bramie (`AuthorizationPolicy` per trasa); drobnoziarnista w serwisie.
- Brama usuwa przychodzące nagłówki `X-User-*`.
- Trasy `/api/{kontekst}/{**rest}` → Service k8s; jawne timeouty per klaster.

## Fakty (zweryfikowane 2026-09-29)

- YARP wspiera `AuthorizationPolicy` per trasa, ale **nie ma wbudowanego retry**.
- `Microsoft.AspNetCore.RateLimiting` działa w pamięci procesu (per replika).

## Konsekwencje / wymagania techniczne

- **SameSite=Strict** działa, tylko jeśli host CIAM (`sso.*`) i aplikacja należą do tej samej
  domeny rejestrowalnej (ten sam „site”). W innym wypadku pierwsze żądanie po logowaniu
  przyjdzie bez ciasteczka; wtedy `Lax`. Zalecany prefiks nazwy `__Host-`.
- **Retry dla GET** wymaga własnego `IForwarderHttpClientFactory` z pipeline'em resilience,
  tylko dla żądań bez body.
- **Load balancing:** kube-proxy rozkłada per połączenie TCP; YARP utrzymuje pulę połączeń,
  więc po skalowaniu ruch jest nierówny. Ustawić `PooledConnectionLifetime` (np. 1–2 min).
- **Rate limiting:** limit efektywny = limit × liczba replik; uwzględnić przy konfiguracji
  lub przenieść limitowanie na Ingress.
- Audience tokenów mobilnych musi obejmować serwisy docelowe (konfiguracja audience w CIAM,
  ADR-0012).
