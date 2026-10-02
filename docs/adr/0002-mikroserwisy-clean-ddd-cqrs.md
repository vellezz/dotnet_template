# ADR-0002: Mikroserwisy .NET w Clean DDD i CQRS

- **Status:** Zaakceptowany
- **Data:** 2026-09-29
- **Decydenci:** zespół architektury (zapis przyjętych zasad architektury)
- **Powiązane:** zasady architektury §1, §2, §6, §13; ADR-0003, ADR-0010, ADR-0015

## Kontekst

System obejmuje wiele obszarów biznesowych, klientów web i mobile oraz wdrożenie na Kubernetes
on-prem. Potrzebne są niezależne wdrożenia, wyraźne granice modeli i ochrona logiki domenowej
przed zależnościami technicznymi.

## Fakty (zweryfikowane 2026-09-29)

- .NET 10 jest wydaniem LTS (listopad 2025, wsparcie do listopada 2028).

## Decyzja

- Backend: .NET 10 LTS, ASP.NET Core MVC (kontrolery).
- **Mikroserwis = jeden bounded context**, własny schemat we wspólnej bazie (ADR-0021), własny projekt `Domain`.
- Warstwy i reguła zależności:
  ```
  Api, Worker, Infrastructure ─> Application ─> Domain
                                           Application ─> Contracts
  Api ─> Infrastructure wyłącznie jako composition root
  SuperApp.Migrator ─> {Serwis}.Infrastructure wszystkich serwisów (tylko uruchamianie migracji)
  Domain ─> SuperApp.Framework.Domain (i nic więcej)
  ```
- Współdzielony jest tylko wewnętrzny framework techniczny `SuperApp.Framework.*`, bez pojęć biznesowych.
- Logika biznesowa w agregatach; handlery tylko orkiestrują.
- Jedna transakcja modyfikuje jeden agregat; spójność między agregatami przez zdarzenia.
- CQRS: komendy przez repozytoria na `WriteDbContext`, zapytania omijają domenę (ADR-0003).
- Reguła zależności egzekwowana referencjami między projektami i testami architektury (ADR-0025).

## Konsekwencje

- Silne ID pisane ręcznie, bez zależności Domain od pakietów (ADR-0023).
- Application nie referuje EF Core; handlery zapytań leżą w Infrastructure (ADR-0026).
- Zdarzenia domenowe dispatchowane w Unit of Work przez `IDomainEventDispatcher` (ADR-0027).
- Wyższy koszt operacyjny niż monolit; wymagana dojrzałość w obserwowalności (ADR-0008).
