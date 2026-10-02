# ADR-0010: Komercyjne licencje MediatR i MassTransit

- **Status:** Zastąpiony przez ADR-0035 (2026-09-30: przejście na ostatnie wersje open source)
- **Data:** 2026-09-29
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §2, §9, §14; ADR-0002, ADR-0005

## Kontekst

Zasady architektury zakładają MediatR (komendy/zapytania, pipeline behaviors) i MassTransit
(RabbitMQ, EF outbox/inbox, sagi). Obie biblioteki przeszły w 2025 na modele komercyjne:
MediatR od v13, MassTransit od v9.

## Decyzja

- Korzystamy z **komercyjnych licencji MediatR i MassTransit** (licencje zakupione).
- Używamy aktualnych, wspieranych wersji objętych licencją (MediatR 13+, MassTransit v9+),
  a nie ostatnich wersji OSS (MediatR 12.x, MassTransit v8).

## Konsekwencje

- Klucze licencyjne są sekretami: dostarczane przez External Secrets / Vault, nigdy w repo
  ani w `appsettings*.json`; konfiguracja zgodnie z dokumentacją obu bibliotek.
- Ważność i zakres licencji (liczba deweloperów, środowiska) monitorowane przez właściciela
  projektu; odnowienie jest warunkiem dalszych aktualizacji.
- Wersje pakietów zarządzane przez Central Package Management (`Directory.Packages.props`).
- Użycie `ISender` w kontrolerach (zasady architektury §10) jest zgodne z tą decyzją.
  Porty (`IIntegrationEventPublisher`, repozytoria, ACL) pozostają zgodnie z §6.
