# ADR-0007: Zero trust: każdy serwis waliduje JWT

- **Status:** Zaakceptowany (doprecyzowany przez ADR-0040: przekazywanie tokenu użytkownika przez BFF)
- **Data:** 2026-09-29
- **Decydenci:** zespół architektury (zapis przyjętych zasad architektury)
- **Powiązane:** zasady architektury §5; ADR-0006, ADR-0012

## Kontekst

Brama nie może być jedyną linią obrony; ruch wewnątrz klastra nie jest z definicji zaufany.

## Decyzja

- Każdy mikroserwis sam waliduje JWT (`AddJwtBearer`: podpis przez JWKS, issuer, audience).
- Serwisy nie ufają nagłówkom tożsamości.
- NetworkPolicy: HTTP do serwisów domenowych tylko z namespace'u bram (i uzasadnionych serwisów).
- Serwis ↔ serwis: client credentials (`{serwis}-client`), tokeny cache'owane własną implementacją
  w `SuperApp.Framework` (ADR-0011).
- Klienci CIAM: `bff-web` (confidential), `mobile-android`/`mobile-ios` (public, PKCE),
  `{serwis}-api` (resource server), `{serwis}-client` (client credentials).
- Sekrety przez External Secrets / Vault; nigdy w repo ani `appsettings*.json`.

## Konsekwencje

- Każdy serwis potrzebuje dostępu sieciowego do JWKS CIAM (NetworkPolicy egress).
- Rotacja kluczy CIAM obsługiwana przez odświeżanie JWKS (domyślne w `JwtBearer`).
- Jeden token z listą audience; serwis akceptuje wyłącznie własne audience (ADR-0012).
