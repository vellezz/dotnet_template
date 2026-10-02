# ADR-0031: Lokalny CIAM do developmentu (Keycloak)

- **Status:** Zaakceptowany (doprecyzowany przez ADR-0045 (proponowany): tożsamość od shella)
- **Data:** 2026-09-30
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §5; ADR-0006, ADR-0007, ADR-0011, ADR-0012, ADR-0016

## Kontekst

CIAM na środowiskach dev, test i prod dostarcza i konfiguruje osobny dział (ADR-0016). Zespół potrzebuje
lokalnego dostawcy OIDC, żeby uruchamiać bramy i serwisy na własnych maszynach i testować przepływy
uwierzytelnienia bez zależności od środowisk współdzielonych.

## Decyzja

- **Keycloak jako lokalny CIAM**, wyłącznie na maszynach deweloperskich i w testach:
  `deploy/local/docker-compose.yml`, realm `app` importowany z `deploy/local/keycloak/realm-superapp.json`.
- Aplikacje nie zależą od Keycloaka: korzystają wyłącznie ze standardów OIDC / OAuth 2.0; lokalnie
  wskazują go w `appsettings.Development.json` (`Authentication:Authority`).
- Konfiguracja realmu odwzorowuje wymagania wobec działu CIAM (ADR-0016) i służy jako ich wzorzec:

| Element | Konfiguracja |
|---|---|
| Scope serwisów | `knowledge.catalog.read`, `knowledge.catalog.write`, `knowledge.library.read`, `knowledge.library.write`, `sleepdiary.entry.read`, `sleepdiary.entry.write`; każdy dodaje audience swojego serwisu (ADR-0012) |
| Scope redaktora | `knowledge.catalog.write` nadawany wyłącznie użytkownikom z rolą `knowledge-editor` |
| `bff-web` | confidential, Authorization Code + PKCE (S256), back-channel logout z wymaganym `sid` |
| `mobile-android`, `mobile-ios` | public, Authorization Code + PKCE, audience `gateway-mobile` |
| `knowledge-api`, `sleepdiary-api`, `gateway-mobile` | resource servers (audience), bez przepływów |
| `knowledge-client`, `sleepdiary-client` | client credentials (serwis ↔ serwis) |
| `dev-cli` | public, password grant do pobierania tokenów w testach; **wyłącznie lokalnie** |
| Realm | rotacja refresh tokenów (jednorazowe użycie), access token 5 min, sesja do 8 h, ochrona przed brute force |
| Użytkownicy | `reader` / `reader`, `editor` / `editor` (rola `knowledge-editor`) |

## Konsekwencje

- Plik realmu zawiera sekrety deweloperskie lokalnej instancji (`dev-*`); nie są używane nigdzie poza nią.
  Aplikacje dostają lokalny sekret `bff-web` przez zmienną środowiskową (`launchSettings.json` profilu `bff-web`,
  `deploy/local/docker-compose.yml`), nigdy przez `appsettings*.json`; na pozostałych środowiskach z Vault (External Secrets).
- Zmiana wymagań wobec CIAM = zmiana realmu lokalnego i zgłoszenie do działu CIAM.
- Klient `dev-cli` (password grant) i sekrety `dev-*` nie mogą trafić do konfiguracji żadnego innego środowiska.
