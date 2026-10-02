# ADR-0016: Infrastruktura i CIAM dostarczane przez inne działy

- **Status:** Zaakceptowany (doprecyzowany przez ADR-0041: wymagania NetworkPolicy)
- **Data:** 2026-09-29
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §3, §9, §11, §12, §13; ADR-0003, ADR-0004, ADR-0005, ADR-0008, ADR-0013, ADR-0020, ADR-0021

## Kontekst

Aplikacja działa na Kubernetes i korzysta z usług infrastrukturalnych oraz CIAM (OIDC / OAuth 2.0).
Wszystko poza kodem i artefaktami aplikacji dostarczają i konfigurują inne działy.

## Decyzja

- **Dział infrastruktury dostarcza (poza zakresem projektu i jego ADR-ów):**
  - klaster Kubernetes wraz z Ingress/Gateway API, cert-manager, ArgoCD, KEDA, NetworkPolicy,
  - MSSQL, Redis, RabbitMQ,
  - Vault / External Secrets,
  - OTel Collector i stos obserwowalności (Tempo, Loki, Prometheus, Grafana).
- **CIAM dostarcza i konfiguruje osobny dział** (nie dział infrastruktury).
- Budowa, wdrożenie, HA, backup, aktualizacje, monitoring, licencje **oraz konfiguracja wewnątrz
  usług** (konfiguracja i klienci CIAM, NetworkPolicy, vhosty i użytkownicy RabbitMQ, użytkownicy
  i uprawnienia MSSQL, wpisy w Vault) należą do działu dostarczającego daną usługę.
- **Zakres zespołu:** kod aplikacji, obrazy kontenerów, chart Helm aplikacji (wdrażane przez
  dostarczone ArgoCD) oraz **wymagania wobec usług** opisane niżej, przekazywane właściwemu działowi.
- Adresy i poświadczenia usług aplikacja dostaje z Vault / External Secrets.

### Wymagania wobec usług

| Usługa | Wymagania |
|---|---|
| MSSQL | Jedna baza na środowisko, schemat per serwis (ADR-0021); DBA uruchamia skrypt bootstrap, a na prod skrypty migracji (ADR-0004); HA na prod; opcjonalnie replika do odczytu (ADR-0003) |
| Redis | Instancja na środowisko, wyłącznie cache L2 (ADR-0020); `allkeys-lru` z limitem pamięci; trwałość niewymagana; TLS i ACL |
| RabbitMQ | Quorum queues; vhost i użytkownicy per środowisko; TLS (ADR-0005) |
| Vault / External Secrets | Sekrety aplikacji (connection stringi, certyfikaty, klucze analityki `Analytics__IdKey` i `Analytics__FeatureFlagsKey` wspólne dla procesów środowiska, ADR-0036) jako Secrets w namespace'ach aplikacji |
| OTel Collector | Endpoint OTLP dostępny z namespace'ów aplikacji (ADR-0008) |
| Kubernetes | Namespace'y aplikacji; Ingress z TLS dla `bff-web`, `gateway-mobile`; KEDA dla Workerów; ArgoCD z obsługą sync-wave/`PreSync` na dev/test (ADR-0004); NetworkPolicy zgodne z zasadami architektury §5; ruch wychodzący HTTPS do PostHog Cloud EU (`eu.i.posthog.com`, `eu-assets.i.posthog.com`) z namespace'ów bram, serwisów i `analytics-forwarder` (ADR-0036) |

**Wobec działu CIAM:** klienci OIDC zgodnie z zasadami architektury §5 (`bff-web`, `mobile-android`, `mobile-ios`, `{serwis}-api`, `{serwis}-client`); audience tokenów obejmujące serwisy aplikacji (ADR-0012); scope redaktora (`knowledge.catalog.write`) nadawany wyłącznie użytkownikom z odpowiednią rolą; rotacja refresh tokenów; OIDC Back-Channel Logout dla `bff-web`. Wzorcowa konfiguracja: lokalny realm z ADR-0031.

## Konsekwencje

- Zmiany wymagań zgłaszane działowi dostarczającemu daną usługę.
- Testy integracyjne używają lokalnych kontenerów (Testcontainers), niezależnie od usług środowiskowych.
