# Architecture Decision Records

Zasady prowadzenia: [ADR-0001](0001-rejestrowanie-decyzji-architektonicznych.md). Szablon: [0000-szablon.md](0000-szablon.md).

| Nr | Tytuł | Status |
|---|---|---|
| [0001](0001-rejestrowanie-decyzji-architektonicznych.md) | Rejestrowanie decyzji architektonicznych | Zaakceptowany (zmieniony przez ADR-0043: dopiski o doprecyzowaniu w nagłówkach) |
| [0002](0002-mikroserwisy-clean-ddd-cqrs.md) | Mikroserwisy .NET w Clean DDD i CQRS | Zaakceptowany |
| [0003](0003-rozdzielenie-write-i-read-dbcontext.md) | Rozdzielone `WriteDbContext` i `ReadDbContext` | Zaakceptowany |
| [0004](0004-migracje-przez-migrator-i-job-k8s.md) | Migracje przez jeden Migrator aplikacji i Job Kubernetes | Zaakceptowany |
| [0005](0005-komunikacja-asynchroniczna-outbox-inbox.md) | Komunikacja asynchroniczna z outbox/inbox | Zaakceptowany |
| [0006](0006-bramy-yarp-bff-web-i-gateway-mobile.md) | Bramy YARP: BFF web i gateway mobile | Zaakceptowany (implementacja BFF: ADR-0011; session store: ADR-0013; doprecyzowany przez ADR-0037: bramy to lokalny zamiennik wspólnej bramy brzegowej) |
| [0007](0007-zero-trust-walidacja-jwt-w-serwisach.md) | Zero trust: każdy serwis waliduje JWT | Zaakceptowany (doprecyzowany przez ADR-0040: przekazywanie tokenu użytkownika przez BFF) |
| [0008](0008-logowanie-loggermessage-i-opentelemetry.md) | `[LoggerMessage]` i OpenTelemetry | Zaakceptowany |
| [0009](0009-kontrakty-openapi-i-generowani-klienci.md) | Kontrakty OpenAPI i generowani klienci | Zaakceptowany (generator .NET: ADR-0014; wersja OpenAPI: ADR-0019; doprecyzowany przez ADR-0039: dwa kontrakty BFF, publiczny i wewnętrzny) |
| [0010](0010-licencje-mediatr-i-masstransit.md) | Komercyjne licencje MediatR i MassTransit | Zastąpiony przez 0035 |
| [0011](0011-wlasny-bff-na-yarp.md) | Własny BFF na YARP | Zaakceptowany (doprecyzowany przez ADR-0037: własność bramy) |
| [0012](0012-audience-tokenow.md) | Jeden token z listą audience, uprawnienia przez scope | Zaakceptowany (doprecyzowany przez ADR-0040: token użytkownika w wywołaniach BFF; ADR-0042: scope wywołań systemowych) |
| [0013](0013-session-store-i-data-protection.md) | Session store BFF i Data Protection w MSSQL | Zaakceptowany (doprecyzowany przez ADR-0037: własność bramy) |
| [0014](0014-klienci-http-refit-refitter.md) | Klienci HTTP .NET: Refit generowany przez Refitter | Zaakceptowany (doprecyzowany przez ADR-0040: przekazywanie tokenu użytkownika; ADR-0042: client credentials tylko systemowo) |
| [0015](0015-result.md) | Naruszenia reguł przez `Result` | Zaakceptowany (doprecyzowany przez ADR-0044: `code` i `traceId` w każdej odpowiedzi błędu; ADR-0047: odczyt wyniku sprawdzany przez kompilator) |
| [0016](0016-infrastruktura-i-ciam-dostarczane-zewnetrznie.md) | Infrastruktura i CIAM dostarczane przez inne działy | Zaakceptowany (doprecyzowany przez ADR-0041: wymagania NetworkPolicy) |
| [0017](0017-kolejnosc-pipeline-behaviors.md) | Kolejność pipeline behaviors | Zaakceptowany |
| [0018](0018-health-checki-readiness.md) | Sondy Kubernetes i health checki | Zaakceptowany |
| [0019](0019-openapi-3-0.md) | Kontrakty w OpenAPI 3.0 | Zaakceptowany (doprecyzowany przez ADR-0039: dwa kontrakty BFF) |
| [0020](0020-cache-l1-l2-redis.md) | Cache dwupoziomowy (L1 w pamięci, L2 w Redis) | Zaakceptowany |
| [0021](0021-schemat-bazy-per-mikroserwis.md) | Wspólna baza MSSQL, osobny schemat per mikroserwis | Zaakceptowany |
| [0022](0022-konfiguracja-yarp-w-bazie.md) | Konfiguracja YARP w MSSQL (tabele relacyjne) z cache | Zaakceptowany (doprecyzowany przez ADR-0037: trasy do BFF experience w lokalnej bramie) |
| [0023](0023-silne-id-pisane-recznie.md) | Silnie typowane ID pisane ręcznie | Zaakceptowany |
| [0024](0024-value-objects.md) | Value objects | Zaakceptowany |
| [0025](0025-testy-architektury.md) | Testy architektury w jednym projekcie | Zaakceptowany (do ponownej oceny) |
| [0026](0026-handlery-zapytan-w-infrastructure.md) | Handlery zapytań w Infrastructure | Zaakceptowany |
| [0027](0027-zdarzenia-domenowe-dispatch-w-uow.md) | Zdarzenia domenowe: dispatch w Unit of Work | Zaakceptowany |
| [0028](0028-knowledge-model-domeny-i-tresc-blokowa.md) | Serwis Knowledge: model domeny i treść blokowa | Zaakceptowany |
| [0029](0029-sleepdiary-model-domeny.md) | Serwis SleepDiary: model domeny | Zaakceptowany |
| [0030](0030-szablon-serwisu.md) | Szablon nowego serwisu | Zaakceptowany |
| [0031](0031-lokalny-ciam-keycloak.md) | Lokalny CIAM do developmentu (Keycloak) | Zaakceptowany (doprecyzowany przez ADR-0045 (proponowany): tożsamość od shella) |
| [0032](0032-jeden-typ-na-plik.md) | Jeden typ na plik, katalogi według odpowiedzialności | Zaakceptowany |
| [0033](0033-dokumentacja-xml-publicznego-api.md) | Dokumentacja w kodzie (XML) po angielsku, dla osoby nowej w zespole | Zaakceptowany |
| [0034](0034-lokalne-srodowisko-docker-compose.md) | Lokalne środowisko deweloperskie w docker compose | Zaakceptowany |
| [0035](0035-mediatr-i-masstransit-w-wersjach-open-source.md) | MediatR i MassTransit w ostatnich wersjach open source | Zaakceptowany |
| [0036](0036-analityka-produktowa-i-feature-flags-posthog.md) | Analityka produktowa, session replay i feature flags w PostHog Cloud EU | Zaakceptowany (doprecyzowany przez ADR-0037: proxy `/ingest` jako wymaganie wobec wspólnej bramy; ADR-0045 (proponowany): flagi i zgoda od shella) |
| [0037](0037-superapp-gateway-jako-lokalny-zamiennik-wspolnej-bramy.md) | `SuperApp.Gateway` jako lokalny zamiennik wspólnej bramy brzegowej | Zaakceptowany |
| [0038](0038-experience-modul-bff-i-serwisy-domenowe.md) | Experience: moduł, BFF i serwisy domenowe | Zaakceptowany |
| [0039](0039-api-publiczne-i-wewnetrzne-bff.md) | API publiczne i wewnętrzne na poziomie BFF | Zaakceptowany |
| [0040](0040-dostep-do-api-wewnetrznego-i-serwisow-domenowych.md) | Dostęp do API wewnętrznego i serwisów domenowych; przekazywanie tokenu użytkownika | Zaakceptowany (doprecyzowuje ADR-0012 i ADR-0014 w zakresie wywołań w kontekście użytkownika; uzupełniony przez ADR-0042: scope wywołań systemowych) |
| [0041](0041-networkpolicy-izolacja-experience.md) | NetworkPolicy jako gwarancja izolacji experience | Zaakceptowany |
| [0042](0042-scope-wywolan-systemowych-miedzy-serwisami.md) | Scope systemowych wywołań między serwisami jednej experience | Zaakceptowany |
| [0043](0043-dopiski-o-doprecyzowaniu-w-naglowkach-adr.md) | Dopiski o doprecyzowaniu w nagłówkach zaakceptowanych ADR | Zaakceptowany (zmienia ADR-0001) |
| [0044](0044-jednolite-odpowiedzi-bledow-code-i-traceid.md) | Jednolite odpowiedzi błędów: zawsze `code` i `traceId` | Zaakceptowany (doprecyzowuje ADR-0015) |
| [0045](0045-modul-w-super-appce-kontrakt-z-shellem.md) | Moduł w super appce: kontrakt z shellem | Proponowany |
| [0046](0046-narzedzie-deweloperskie-superapp.md) | Narzędzie deweloperskie `dotnet superapp` | Zaakceptowany |
| [0047](0047-odczyt-result-bez-wyjatkow.md) | Odczyt `Result` sprawdzany przez kompilator, bez wyjątków | Zaakceptowany (doprecyzowuje ADR-0015) |
