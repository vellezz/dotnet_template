# Rejestr zakresów EventId

Logi wyłącznie przez `[LoggerMessage]` ze stałym `EventId` (ADR-0008). Każdy komponent ma własny zakres;
nowy komunikat dostaje kolejny wolny numer z zakresu komponentu, nowy serwis lub BFF kolejny wolny zakres.

| Zakres | Komponent | Użyte |
|---|---|---|
| 100–199 | `SuperApp.Framework.Application` (behaviors) | 100–101 |
| 200–299 | `SuperApp.Framework.Infrastructure` (dispatcher zdarzeń domenowych, akcje po commicie, niedostępna usługa zależna `DownstreamUnavailableExceptionHandler`, audyt API wewnętrznego BFF `InternalApiCallAudit`) | 200, 210, 220, 230 |
| 300–399 | `SuperApp.Framework.Infrastructure` (cache) | 300 |
| 400–499 | `SuperApp.Framework.Infrastructure` (feature flags, ADR-0036) | 400–401 |
| 1000–1999 | Knowledge (Api, Application, Infrastructure) | — |
| 2000–2999 | Knowledge (Worker) | 2001–2002 |
| 3000–3999 | `SuperApp.Gateway`: 30xx konfiguracja tras, 31xx tokeny BFF, 32xx back-channel logout, 33xx sesje, 34xx ponawianie | 3001–3004, 3101–3104, 3201–3202, 3301–3302, 3401–3402 |
| 4000–4999 | `SuperApp.Migrator` | 4001–4003 |
| 5000–5999 | SleepDiary | — |
| 6000–6999 | `Example.Bff` (BFF experience Example): 6001 część odpowiedzi komponowanej inna niż `Ok` (`ExperienceSummaryController`) | 6001 |
| 7000–8999 | kolejne serwisy i BFF-y (po 1000 na komponent) | — |
| 9000–9999 | `SuperApp.AnalyticsForwarder` (ADR-0036) | 9001–9004 |
