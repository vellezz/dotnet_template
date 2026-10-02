# ADR-0011: Własna implementacja BFF na YARP

- **Status:** Zaakceptowany (doprecyzowany przez ADR-0037: własność bramy)
- **Data:** 2026-09-29
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §4, §5, §14; ADR-0006, ADR-0007, ADR-0013

## Kontekst

BFF web wymaga logowania OIDC, sesji po stronie serwera, ochrony CSRF, odświeżania tokenów
i przekazywania access tokenu do serwisów. Serwisy wołające inne serwisy potrzebują
tokenów client credentials z cache.

## Decyzja

- BFF implementujemy samodzielnie na YARP + ASP.NET Core (`AddCookie`, `AddOpenIdConnect`,
  `ITicketStore`, transformy YARP).
- Zarządzanie tokenami implementujemy w `SuperApp.Framework.Infrastructure`:
  - **token użytkownika (BFF):** odświeżanie refresh tokenem przed wygaśnięciem (`expires_at`),
    zapis nowych tokenów w sesji (`ITicketStore`);
  - **client credentials (serwis ↔ serwis):** cache tokenu per klient CIAM, odnawianie
    przed wygaśnięciem, dołączanie przez `DelegatingHandler`
    w kliencie z `IHttpClientFactory` (ADR-0014).

## Konsekwencje / wymagania techniczne

- **Równoległe odświeżanie:** wiele równoczesnych żądań nie może odświeżać tego samego tokenu
  (single-flight per sesja / per klient). Przy rotacji refresh tokenów w CIAM podwójne
  użycie refresh tokenu unieważnia sesję. Żądania jednej sesji mogą trafiać do różnych replik, więc
  single-flight w pamięci repliki nie wystarcza: odświeżenie tokenu użytkownika odbywa się pod
  wyłączną blokadą aplikacyjną sesji w MSSQL (`sp_getapplock`, zasób per sesja, krótka transakcja,
  ADR-0013). Pod blokadą replika ponownie czyta zapisany ticket i woła CIAM tylko wtedy, gdy token
  nadal wymaga odświeżenia; jeśli inna replika już to zrobiła, przejmuje zapisane tokeny. Nowe tokeny
  są zapisywane przed zwolnieniem blokady.
- Wspólne odświeżenie nie jest związane z żądaniem, które je rozpoczęło: ma własny limit czasu
  (10 s), więc przerwanie tego żądania nie powoduje błędu pozostałych. Przekroczenie limitu lub brak
  blokady nie kończy sesji (żądanie idzie z bieżącymi tokenami, kolejne ponawia odświeżenie).
- Obsługa błędu odświeżenia: wylogowanie i zwrot 401 do Angulara (bez pętli przekierowań).
- Samodzielnie implementujemy: CSRF (`X-CSRF: 1` na `/api/*`), `/bff/login`, `/bff/logout`, `/bff/user`,
  unieważnianie sesji, OIDC Back-Channel Logout z CIAM (ADR-0013).
- **Wylogowanie** to nawigacja przeglądarki (top-level) `GET /bff/logout?sid={sid}`, a nie wywołanie
  XHR/fetch: tylko nawigacja może podążyć za przekierowaniem 302 do endpointu end-session CIAM
  (inny origin). Nawigacja nie wyśle nagłówka `X-CSRF`, więc ochroną CSRF jest parametr `sid`: musi być
  równy claimowi `sid` sesji (inna strona go nie zna). Brak lub niezgodność `sid` = 400 bez
  wylogowania; bez sesji przekierowanie na `/`. `/bff/user` zwraca gotowy `logoutUrl`
  (`/bff/logout?sid=...`), a SPA wykonuje `window.location.href = logoutUrl`. Wariant `POST` nie istnieje.
  Sesja bez claimu `sid` (CIAM go nie wydał) nie ma `logoutUrl`; CIAM musi wydawać `sid` w ID tokenie (ten sam wymóg co przy back-channel logout, ADR-0016, ADR-0031).
- BFF **nie żąda** scope `offline_access`. Token offline tworzy w CIAM sesję offline niezależną od
  sesji SSO: wylogowanie w CIAM jej nie kończy i back-channel logout nie dociera do BFF. Refresh token
  związany z sesją SSO wystarcza, bo sesja BFF i tak nie może żyć dłużej niż sesja SSO.
- Implementacja podlega przeglądowi bezpieczeństwa i testom integracyjnym (wygaśnięcie sesji,
  odświeżanie, logout, równoległe żądania).
