# 14. Logowanie i obserwowalność

Standardy obserwowalności i logowania: strukturalne generowanie logów przez `[LoggerMessage]` z unikalnymi numerami `EventId`, konfiguracja OpenTelemetry (distributed tracing, metryki Prometheus/OTLP), propagacja kontekstu (`traceparent`) przez HTTP i RabbitMQ oraz korelacja za pomocą `traceId`.

**Wymagania wstępne.** [Architektura w praktyce](02-architektura-w-praktyce.md) (procesy i przepływ żądania),
[Lokalne środowisko i debugowanie](13-lokalne-srodowisko-i-debugowanie.md) (logi kontenerów). Decyzje: ADR-0008, ADR-0018.

> **W skrócie**
>
> - Log = metoda `partial` z atrybutem `[LoggerMessage(EventId, LogLevel, "szablon {Pole}")]`. Żadnych `logger.LogInformation($"...")`.
> - `EventId` bierzesz z zakresu swojego komponentu w [rejestrze](../logowanie-eventid.md) i dopisujesz go tam w tym samym PR.
> - Nie logujesz tokenów, sekretów, kluczy sesji ani danych osobowych (treść notatek, imiona, e-maile, `sub`). Identyfikatory zasobów wolno.
> - Każde żądanie MediatR jest już logowane i mierzone przez `LoggingBehavior` (EventId 100/101); w handlerach zwykle nic nie logujesz.
> - OpenTelemetry: ślady (ASP.NET Core, HttpClient/YARP, SqlClient, MassTransit, `SuperApp.Application`, `SuperApp.Infrastructure`),
>   metryki, logi. Eksport OTLP włącza się samą zmienną `OTEL_EXPORTER_OTLP_ENDPOINT`.
> - `traceId` w `ProblemDetails` to identyfikator śladu; wyszukujesz go w Tempo, a logi tego żądania mają ten sam `TraceId`.

W blokach kodu pominięto komentarze dokumentacji XML (są w plikach źródłowych).

---

## 1. Logi przez `[LoggerMessage]`

### 1.1 Dlaczego source generator

| Problem z `logger.LogInformation(...)` | Co daje `[LoggerMessage]` |
|---|---|
| szablon parsowany przy każdym wywołaniu, argumenty pakowane do `object[]` (alokacje, boxing) | kod generowany w czasie kompilacji, bez alokacji, gdy poziom jest wyłączony |
| `EventId` zwykle pomijany, więc logów nie da się filtrować ani alertować po numerze | stały `EventId` w atrybucie: alert „EventId 3002 w bramie” jest stabilny między wersjami |
| łatwo o interpolację `$"..."`, która gubi strukturę (pola) i może wciągnąć dane osobowe | szablon z nazwanymi polami: `{MaterialId}` trafia do logu jako osobne pole |
| komunikat rozproszony po kodzie | jedna deklaracja metody, wołana z wielu miejsc |

Reguła jest w ADR-0008. Build jej nie wymusza (brak analizatora blokującego `LogInformation`); pilnuje jej
review.

### 1.2 Wzorce z repozytorium

**Metoda w klasie, która loguje** (najczęstszy przypadek). `src/Framework/SuperApp.Framework.Application/Behaviors/LoggingBehavior{TRequest,TResponse}.cs`:

```csharp
internal sealed partial class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : IResultFactory<TResponse>
{
    // ... Handle (rozdział 2 przewodnika) ...

    [LoggerMessage(100, LogLevel.Information, "Request {RequestName} handled in {ElapsedMs} ms")]
    private static partial void LogRequestHandled(ILogger logger, string requestName, double elapsedMs);

    [LoggerMessage(101, LogLevel.Warning, "Request {RequestName} failed with {ErrorCode} in {ElapsedMs} ms")]
    private static partial void LogRequestFailed(ILogger logger, string requestName, string errorCode, double elapsedMs);
}
```

Klasa musi być `partial`, metoda `static partial`, pierwszy parametr `ILogger`. Nazwy parametrów odpowiadają polom szablonu
(wielkość liter nie ma znaczenia).

**Konsument z błędem biznesowym.** `src/Services/Knowledge/Knowledge.Worker/Consumers/MaterialArchivedConsumer.cs`:

```csharp
public sealed partial class MaterialArchivedConsumer(ISender sender, ILogger<MaterialArchivedConsumer> logger) : IConsumer<MaterialArchivedV1>
{
    public async Task Consume(ConsumeContext<MaterialArchivedV1> context)
    {
        var result = await sender.Send(new RemoveFavoritesOfItem(FavoriteItemType.Material, context.Message.MaterialId), context.CancellationToken);
        if (result.IsFailure)
        {
            LogRejected(logger, context.Message.MaterialId, result.Error.Code);
        }
    }

    // Source-generated log method (the only allowed way of logging, see architecture rules §11).
    [LoggerMessage(2001, LogLevel.Warning, "Removing favorites of archived material {MaterialId} rejected: {ErrorCode}")]
    private static partial void LogRejected(ILogger logger, Guid materialId, string errorCode);
}
```

**Log z wyjątkiem.** Wyjątek przekazujesz jako parametr typu `Exception`; generator dołączy go do wpisu (typ, komunikat, stos)
i nie wstawia go do szablonu. `src/Framework/SuperApp.Framework.Infrastructure/Persistence/AfterCommitInterceptor.cs`:

```csharp
[LoggerMessage(210, LogLevel.Warning, "After-commit action of {Context} failed")]
private static partial void LogAfterCommitActionFailed(ILogger logger, string context, Exception exception);
```

**Wspólna klasa `Log` projektu.** Gdy komunikaty są używane w kilku miejscach albo projekt ma top-level `Program.cs`
(`src/Migrator/SuperApp.Migrator/Log.cs`):

```csharp
internal static partial class Log
{
    [LoggerMessage(4001, LogLevel.Information, "Migrating {Context}: {PendingCount} pending migration(s)")]
    public static partial void Migrating(ILogger logger, string context, int pendingCount);

    [LoggerMessage(4002, LogLevel.Information, "Migrated {Context}")]
    public static partial void Migrated(ILogger logger, string context);

    [LoggerMessage(4003, LogLevel.Error, "Migration of {Context} failed; rollout must not continue")]
    public static partial void Failed(ILogger logger, string context, Exception exception);
}
```

Wariant z metodą rozszerzającą (`this ILogger logger`, wywołanie `logger.OrderPlaced(orderId)`) z ADR-0008 jest
równoważny. Klasa `Log` w osobnym pliku `Log.cs` (APP004/APP005), przestrzeń nazw zgodna z katalogiem (IDE0130),
domyślnie `internal`.

### 1.3 Jak dodać nowy log (przykład dla Knowledge)

Przykład według wzorców repozytorium (w kodzie Knowledge.Api/Application/Infrastructure nie ma jeszcze własnych logów, więc
zakres 1000–1999 jest wolny). Załóżmy log z warstwy ACL po nieudanym wywołaniu innego serwisu:

1. Otwórz [rejestr EventId](../logowanie-eventid.md): Knowledge (Api, Application, Infrastructure) ma `1000–1999`, użyte „—”.
2. Weź pierwszy wolny numer (`1001`) i zadeklaruj metodę:

```csharp
// Knowledge.Infrastructure/Integration/SleepDiaryGateway.cs (przykład; ACL nie istnieje jeszcze w repozytorium)
internal sealed partial class SleepDiaryGateway(/* ... */ ILogger<SleepDiaryGateway> logger)
{
    [LoggerMessage(1001, LogLevel.Warning, "SleepDiary unavailable while loading summary for material {MaterialId}; using fallback")]
    private static partial void LogSleepDiaryUnavailable(ILogger logger, Guid materialId, Exception exception);
}
```

3. W tym samym PR zaktualizuj kolumnę „Użyte” w rejestrze (`1001`).

**Źle / Dobrze:**

```csharp
// Źle: interpolacja (brak pól, alokacje), brak EventId, dane osobowe i token w logu
logger.LogInformation($"User {user.Email} saved entry with notes '{command.Notes}', token {accessToken}");

// Źle: szablon poprawny, ale nadal LogInformation bez EventId i z danymi osobowymi
logger.LogInformation("User {Email} recorded sleep", user.Email);

// Dobrze: stały EventId z zakresu serwisu, tylko identyfikator zasobu i kod błędu
[LoggerMessage(5001, LogLevel.Warning, "Sleep entry {EntryId} rejected: {ErrorCode}")]
private static partial void LogEntryRejected(ILogger logger, Guid entryId, string errorCode);
```

### 1.4 Rejestr `EventId`

Plik: [`docs/logowanie-eventid.md`](../logowanie-eventid.md). Stan obecny:

| Zakres | Komponent | Użyte |
|---|---|---|
| 100–199 | `SuperApp.Framework.Application` (behaviors) | 100–101 |
| 200–299 | `SuperApp.Framework.Infrastructure` (dispatcher zdarzeń domenowych, akcje po commicie, wywołania innych API, audyt API wewnętrznego BFF) | 200, 210, 220, 230 |
| 300–399 | `SuperApp.Framework.Infrastructure` (cache) | 300 |
| 400–499 | `SuperApp.Framework.Infrastructure` (feature flags, ADR-0036) | 400–401 |
| 1000–1999 | Knowledge (Api, Application, Infrastructure) | — |
| 2000–2999 | Knowledge (Worker) | 2001–2002 |
| 3000–3999 | `SuperApp.Gateway`: 30xx konfiguracja tras, 31xx tokeny BFF, 32xx back-channel logout, 33xx sesje, 34xx ponawianie | 3001–3004, 3101–3104, 3201–3202, 3301–3302, 3401–3402 |
| 4000–4999 | `SuperApp.Migrator` | 4001–4003 |
| 5000–5999 | SleepDiary | — |
| 6000–6999 | `Example.Bff` (BFF experience Example) | 6001 |
| 7000–8999 | kolejne serwisy i BFF-y (po 1000 na komponent) | — |
| 9000–9999 | `SuperApp.AnalyticsForwarder` (ADR-0036) | 9001–9002 |

Wszystkie istniejące komunikaty:

| EventId | Poziom | Komponent | Komunikat |
|---|---|---|---|
| 100 | Information | `LoggingBehavior` | `Request {RequestName} handled in {ElapsedMs} ms` |
| 101 | Warning | `LoggingBehavior` | `Request {RequestName} failed with {ErrorCode} in {ElapsedMs} ms` |
| 200 | Debug | `DomainEventDispatcher` | `Dispatching domain event {EventName}` |
| 210 | Warning | `AfterCommitInterceptor` | `After-commit action of {Context} failed` |
| 220 | Warning | `DownstreamUnavailableExceptionHandler` | `Downstream call failed ({Code})` z wyjątkiem; `Code` = `downstream.unavailable` (brak połączenia, otwarty circuit breaker) albo `downstream.timeout` (limit czasu); odpowiedź `503` / `504` (BFF i każdy host z `AddDownstreamApi`) |
| 230 | Information | `InternalApiCallAudit` | `Internal API {Endpoint} called by client {CallerClientId}`; szablon trasy i klient z claimu `azp`, tylko żądania pod `/internal` po autoryzacji (globalny filtr MVC w BFF) |
| 300 | Warning | `FailSafeCache` | `Cache fail-safe: returning stale value for {CacheKey}` |
| 400 | Warning | `HybridCacheFeatureFlags` | `Feature flag {FlagKey}: {Reason}, using the default value {DefaultValue}` (flaga nieznana w PostHog) |
| 401 | Warning | `HybridCacheFeatureFlags` | `Feature flags could not be evaluated; every flag of this request uses its default value` (timeout, błąd sieci) |
| 2001, 2002 | Warning | konsumenci Knowledge | `Removing favorites of archived material/collection {Id} rejected: {ErrorCode}` |
| 3001 | Information | `DatabaseProxyConfigProvider` | `Gateway {Profile}: applied proxy config {MigrationId} ({RouteCount} routes, {ClusterCount} clusters)` |
| 3002 | Error | jw. | `... proxy config {MigrationId} rejected by validation; keeping previous configuration` |
| 3003 | Warning | jw. | `... proxy config refresh failed; keeping current configuration` |
| 3004 | Warning | jw. | `... database unavailable at startup; loaded last valid config {MigrationId} from cache` |
| 3101–3104 | Warning | `TokenRefresher` | refresh odrzucony przez CIAM (status), nieudany, pominięty (brak blokady sesji), timeout |
| 3201 / 3202 | Warning / Information | `BffEndpoints` | back-channel logout: niepoprawny `logout_token` / unieważniono N sesji |
| 3301 / 3302 | Information / Warning | `SessionCleanupService` | usunięto N wygasłych sesji / sprzątanie nieudane |
| 3401 / 3402 | Warning | `IdempotentRetryHandler` | ponowienie GET/HEAD po statusie 502/503/504 / po błędzie połączenia |
| 4001–4003 | Information / Error | `SuperApp.Migrator` | migracja kontekstu: start, koniec, błąd |
| 6001 | Warning | `ExperienceSummaryController` (`Example.Bff`) | `Summary part {Part} returned {Status}`; część odpowiedzi komponowanej o statusie innym niż `Ok` (`Forbidden`, `Unavailable`, `Timeout`) |
| 9001 | Information | `LoggingProductEventSink` | `Analytics disabled: product event {EventName} ({EventOwner}, properties: {PropertyNames}) not sent` |
| 9002 | Warning | `PostHogProductEventSink` | `Product event {EventName} was not queued for PostHog (queue full or client disposed)` |

Nowy serwis lub BFF dostaje kolejny wolny zakres tysiąca (od 7000 do 8999; 9000–9999 należy do forwardera analityki); dopisz go do rejestru razem z utworzeniem serwisu (szablon
`superapp-service` niczego tu nie przydziela).
Zduplikowany `EventId` nie psuje builda, ale psuje alerty i filtrowanie; dlatego rejestr.

### 1.5 Czego nie logować

| Nie loguj | Dlaczego | Zamiast tego |
|---|---|---|
| tokenów (`access_token`, `refresh_token`, `logout_token`), nagłówka `Authorization`, ciasteczek | przejęcie konta | nic; ewentualnie fakt nieudanej walidacji (wzór 3201) |
| sekretów klienta, haseł, connection stringów | jw. | |
| klucza sesji BFF | to jest ciasteczko; nawet blokady SQL używają tylko skrótu klucza (`gateway-session:` + SHA-256) | |
| danych osobowych: e-mail, imię, treść notatek ze snu, `sub` użytkownika | RODO; logi są szeroko dostępne i długo przechowywane | identyfikator zasobu (`MaterialId`, `EntryId`), kod błędu |
| całych komend/zapytań i DTO | mogą zawierać wszystko powyżej; dlatego `LoggingBehavior` loguje tylko nazwę typu | pojedyncze pola techniczne |
| treści `Error.Message` z danymi użytkownika | komunikaty błędów trafiają do logów i odpowiedzi (konwencja: bez danych osobowych) | `Error.Code` |

### 1.6 Poziomy logowania

Polityka wynikająca z istniejących komunikatów:

| Poziom | Kiedy | Przykłady |
|---|---|---|
| `Debug` | szczegóły o dużej liczności, przydatne tylko przy diagnozie | 200 (każde zdarzenie domenowe) |
| `Information` | normalne, oczekiwane zdarzenie, które warto policzyć lub odtworzyć | 100 (obsłużone żądanie), 3001 (nowa konfiguracja tras), 3301, 4001, 4002 |
| `Warning` | coś się nie udało, ale system sobie poradził (błąd biznesowy, degradacja, ponowienie) | 101 (`Result` z błędem), 300 (fail-safe), 210, 2001, 3003, 3101–3104, 3401 |
| `Error` | wymaga działania człowieka; stan nie naprawi się sam | 3002 (odrzucona konfiguracja tras), 4003 (migracja nieudana) |
| `Critical` | nieużywany | |

Wyjątki nieobsłużone loguje ASP.NET Core (`ExceptionHandlerMiddleware`, poziom `Error`) i MassTransit (błąd konsumenta po
wyczerpaniu ponowień); nie łap ich tylko po to, żeby zalogować.

Konfiguracja filtrów (`appsettings.json` serwisu):

```json
"Logging": {
  "LogLevel": {
    "Default": "Information",
    "Microsoft.AspNetCore": "Warning",
    "Microsoft.EntityFrameworkCore": "Warning"
  }
}
```

Brama dodatkowo `"Yarp": "Warning"`. Żeby lokalnie zobaczyć SQL generowany przez EF, ustaw
`Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command=Information` w profilu uruchomieniowym; na klastrze SQL jest
widoczny w śladach (instrumentacja SqlClient), nie w logach.

---

## 2. OpenTelemetry

### 2.1 Konfiguracja

Całość jest w `AddAppServiceDefaults` (`src/Framework/SuperApp.Framework.Infrastructure/Hosting/HostingExtensions.cs`):

```csharp
private static void AddAppTelemetry(this IHostApplicationBuilder builder, string serviceName)
{
    var telemetry = builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService(serviceName))
        .WithTracing(tracing => tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSqlClientInstrumentation()
            .AddSource("MassTransit", ApplicationTelemetry.SourceName, InfrastructureTelemetry.SourceName))
        .WithMetrics(metrics => metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddMeter("MassTransit", InfrastructureTelemetry.SourceName))
        .WithLogging();

    if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
    {
        telemetry.UseOtlpExporter();
    }
}
```

| Sygnał | Źródła | Co widać |
|---|---|---|
| Ślady | ASP.NET Core | span serwera dla każdego żądania HTTP (`POST v1/categories`) |
| | HttpClient | wywołania wychodzące: YARP w bramie, klienci Refit w BFF (wywołania serwisów domenowych), odświeżanie tokenu |
| | SqlClient | każde polecenie SQL (także z EF Core) |
| | `MassTransit` | publikacja, wysłanie z outboxa, odbiór i konsumpcja wiadomości |
| | `SuperApp.Application` (`ApplicationTelemetry`) | span na każdą komendę/zapytanie, nazwany typem żądania (`CreateCategory`), status `Error` z kodem błędu przy `Result.Failure` |
| | `SuperApp.Infrastructure` (`InfrastructureTelemetry`) | span `domain-event {Nazwa}` na każdy dispatch zdarzenia domenowego |
| Metryki | ASP.NET Core, HttpClient, runtime | czas i liczba żądań (`http.server.request.duration` z atrybutem `http.route`, więc per trasa także w bramach), GC, wątki |
| | `MassTransit` | liczniki i czasy konsumpcji, publikacji, błędów |
| | `SuperApp.Infrastructure` | metryki cache (2.3) |
| | `SuperApp.Gateway` (tylko brama) | metryki konfiguracji tras (2.3) |
| Logi | `ILogger` (`WithLogging`) | wszystkie logi z `TraceId`/`SpanId` aktywnego śladu |

Atrybut zasobu `service.name` = drugi argument `AddAppServiceDefaults`: `knowledge-api`, `knowledge-worker`,
`sleepdiary-api`, `sleepdiary-worker`, w BFF `example-bff`, w forwarderze analityki `analytics-forwarder`; w bramie nazwa profilu (`bff-web`, `gateway-mobile`). Po nim
filtrujesz w Grafanie.

**Eksport.** `UseOtlpExporter()` włącza OTLP dla wszystkich trzech sygnałów naraz, ale tylko gdy ustawiona jest zmienna
`OTEL_EXPORTER_OTLP_ENDPOINT`. Charty Helm ustawiają ją na `http://otel-collector.observability.svc.cluster.local:4317`
(`otel.endpoint` w `values.yaml` chartów `superapp-service`, `superapp-bff`, `superapp-gateway` i `superapp-analytics-forwarder`); kolektor przekazuje dalej do Tempo (ślady),
Loki (logi) i Prometheusa (metryki). Lokalnie i w testach zmienna nie jest ustawiona: nic nie jest eksportowane, logi idą tylko
na konsolę, a proces nie potrzebuje kolektora.

### 2.2 Własne źródła w serwisie

`AddAppTelemetry` rejestruje tylko źródła frameworka. Jeśli serwis potrzebuje własnego `ActivitySource` albo `Meter`, musi go
**dodatkowo zarejestrować**, inaczej dane po cichu przepadną. Wzór z bramy (`src/Gateway/SuperApp.Gateway/Program.cs`):

```csharp
builder.Services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddMeter(GatewayTelemetry.MeterName));
```

i definicja (`src/Gateway/SuperApp.Gateway/Telemetry/GatewayTelemetry.cs`):

```csharp
internal static class GatewayTelemetry
{
    public const string MeterName = "SuperApp.Gateway";

    public static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> ProxyConfigReloadFailures =
        Meter.CreateCounter<long>("superapp.gateway.proxy_config.reload_failures", description: "Nieudane przeładowania konfiguracji tras (walidacja lub baza)");
}
```

Dla śladów odpowiednikiem jest `ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddSource("Knowledge"))`.
Zasady: nazwa metryki `app.{obszar}.{co}` (kropki, małe litery), atrybuty o **małej liczności** (wynik, profil, typ),
nigdy identyfikator użytkownika czy zasobu (każda wartość atrybutu to osobna seria w Prometheusie).

### 2.3 Metryki własne

| Metryka | Typ | Atrybuty | Źródło | Do czego |
|---|---|---|---|---|
| `superapp.cache.requests` | licznik | `result` = `fresh` / `stale` / `miss` | `FailSafeCache` | współczynnik trafień: `fresh / (fresh + stale + miss)` |
| `superapp.cache.fail_safe.activations` | licznik | | `FailSafeCache` | ile razy zwrócono starą wartość, bo odświeżenie skończyło się błędem przejściowym; rosnąca wartość = źródło danych ma problem, użytkownicy jeszcze tego nie widzą. **Alert.** |
| `superapp.feature_flags.fallbacks` | licznik | `reason` = `timeout` / `unavailable` / `unknown flag` / `cache_error` | `HybridCacheFeatureFlags` | ile ewaluacji flag skończyło się wartością domyślną z kodu; rosnące `timeout`/`unavailable` = PostHog niedostępny. **Alert** ([21.11](21-analityka-i-feature-flags.md#2111-obserwowalność)) |
| `superapp.gateway.proxy_config.loaded` | gauge (0/1) | `profile`, `migration_id` | `DatabaseProxyConfigProvider` | czy replika ma konfigurację tras i w której wersji; różne `migration_id` na replikach = przeładowanie w toku lub błąd |
| `superapp.gateway.proxy_config.reload_failures` | licznik | | jw. | odrzucona lub nieudana konfiguracja tras (raz na wersję migracji). **Alert.** |

Rejestracja licznika cache (`src/Framework/SuperApp.Framework.Infrastructure/Telemetry/InfrastructureTelemetry.cs`):

```csharp
public static readonly Counter<long> CacheRequests =
    Meter.CreateCounter<long>("superapp.cache.requests", description: "Cache reads by result (ADR-0020)");

public static readonly Counter<long> CacheFailSafeActivations =
    Meter.CreateCounter<long>("superapp.cache.fail_safe.activations", description: "Stale cache values returned after a failed refresh");
```

W Prometheusie nazwy są zwykle tłumaczone przez kolektor (kropki na podkreślenia, sufiks `_total` dla liczników, np.
`superapp_cache_requests_total{result="stale"}`); dokładną postać określa konfiguracja kolektora działu infrastruktury.

Minimalne alerty z dokumentu architektury (§8.9): niepuste kolejki `_error`, `superapp.gateway.proxy_config.reload_failures`,
stan `/health/dependencies`, `superapp.cache.fail_safe.activations` powyżej progu (progi do ustalenia); dodatkowo
`superapp.feature_flags.fallbacks{reason="timeout"|"unavailable"}` i niepuste kolejki `analytics-*_error` (ADR-0036).

---

## 3. Propagacja śladu: brama → BFF → serwis → RabbitMQ → konsument

```mermaid
sequenceDiagram
    autonumber
    participant B as Przeglądarka
    participant G as bff-web
    participant F as example-bff
    participant A as knowledge-api
    participant DB as MSSQL
    participant W as knowledge-worker
    participant MQ as RabbitMQ

    B->>G: POST /api/example/v1/knowledge/materials/{id}/archive (opcjonalnie traceparent z OTel w SPA)
    Note over G: span serwera ASP.NET Core (nowy ślad T, jeśli brak traceparent)
    G->>F: HttpClient (YARP): POST /v1/knowledge/materials/{id}/archive, traceparent: 00-T-...-01
    Note over F: span serwera (rodzic: span YARP)
    F->>A: HttpClient (Refit): POST /v1/materials/{id}/archive, traceparent: 00-T-...-01
    Note over A: span serwera (rodzic: span klienta w BFF) → span "ArchiveMaterial" (SuperApp.Application)
    A->>DB: spany SQL; span "domain-event MaterialArchived"
    A->>DB: INSERT OutboxMessage (nagłówki MassTransit z kontekstem śladu T)
    Note over A,W: opóźnienie: delivery service Workera
    W->>MQ: publish (span MassTransit, kontekst z nagłówków wiadomości)
    MQ->>W: odbiór
    Note over W: span konsumpcji MaterialArchivedConsumer → span "RemoveFavoritesOfItem" → spany SQL
```

1. **Wejście do bramy.** Instrumentacja ASP.NET Core czyta nagłówek `traceparent`, jeśli klient go wysłał (np. Angular
   z SDK OpenTelemetry, opcjonalnie wg ADR-0008), albo zaczyna nowy ślad.
2. **Brama → BFF.** YARP wysyła żądanie przez `HttpClient`; instrumentacja HttpClient dopisuje `traceparent` z bieżącym
   spanem. Brama usuwa `Cookie` i `X-User-*`, ale nagłówków śladu nie rusza.
3. **BFF → serwis.** Klient Refit w BFF też jest `HttpClient`, więc wywołanie serwisu dostaje `traceparent` tak samo. Endpoint
   komponowany (`GET /v1/me/summary`) ma pod spanem serwera BFF dwa równoległe spany klienta, po jednym na serwis; po nich
   widać, który serwis odpowiadał najdłużej.
4. **W serwisie.** Span serwera jest dzieckiem spanu klienta w BFF; `LoggingBehavior` otwiera span o nazwie komendy,
   `DomainEventDispatcher` span `domain-event {Nazwa}`, SqlClient spany zapytań. Logi z tego czasu mają ten sam `TraceId`.
5. **Outbox.** MassTransit zapisuje wiadomość w `OutboxMessage` razem z nagłówkami (kolumna `Headers`), w tym z kontekstem
   śladu (nagłówki `MT-Activity-Id`, `MT-Activity-Propagation`). Wysłanie z Workera następuje później, ale niesie ten sam
   kontekst.
6. **Konsument.** MassTransit odtwarza kontekst z nagłówków, więc span konsumpcji i wszystko pod nim (komenda, SQL) należy
   do tego samego śladu. W Tempo widać przerwę między zapisem w API a konsumpcją: to czas oczekiwania w outboxie i kolejce.

Praktyczny wniosek: jeden `traceId` z odpowiedzi API pokazuje także asynchroniczne skutki komendy w innych procesach.
Błąd serwisu przechodzi przez BFF bez zmian (`ToActionResult`), więc `traceId` w ciele odpowiedzi, którą widzi moduł, to
identyfikator tego samego śladu, który zaczął się w bramie; `instance` pokazuje ścieżkę serwisu (`/v1/materials/...`), a nie
ścieżkę BFF.

---

## 4. `traceId` w odpowiedzi i jak znaleźć żądanie

Każda odpowiedź błędu z `ResultHttpExtensions` zawiera `traceId`:

```json
{"title":"Materiał nie istnieje.","status":404,"instance":"/v1/materials/0199b000-0000-7000-8000-000000000001/publish",
 "code":"knowledge.material.not_found","traceId":"d9bdeb57efe9f5bdf05bd6b95eeddfb1"}
```

```csharp
problem.Extensions["traceId"] = ProblemDetailsConventions.TraceId(controller.HttpContext);
```

Odpowiedzi `503 downstream.unavailable` i `504 downstream.timeout` z BFF (serwis nieosiągalny albo zbyt wolny) mają ten
sam kształt (`code`, `traceId` jako 32 znaki), a BFF zapisuje przy nich log 220 z wyjątkiem.

Ten sam format mają odpowiedzi tworzone przez samo ASP.NET Core (401 z `UseStatusCodePages`, 404 z routingu, 500 z
`UseExceptionHandler`, automatyczne 400 model bindingu): konwencja `ProblemDetailsConventions` (ADR-0044) zastępuje domyślny
`traceparent` samym identyfikatorem śladu i dokłada ogólny `code` według statusu:

```json
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.2","title":"Unauthorized","status":401,
 "traceId":"4f3944f3c86ef3d6e3f662101d17a5de","code":"auth.invalid_token","instance":"/v1/entries"}
```

**Na klastrze:**

1. Weź `traceId` (32 znaki szesnastkowe) z odpowiedzi, z narzędzi deweloperskich przeglądarki albo od użytkownika.
2. Grafana → Tempo → wyszukiwanie po Trace ID. Zobaczysz spany bramy, serwisu, SQL, zdarzeń domenowych i konsumentów.
3. Logi tego żądania: w Loki filtrem po identyfikatorze śladu (logi OTLP niosą `TraceId`/`SpanId`) i `service.name`; jeśli
   dział infrastruktury skonfigurował powiązanie Tempo ↔ Loki, przejdziesz do logów bezpośrednio ze spanu.
4. Span `CreateCategory` ze statusem `Error` ma w opisie kod błędu (`knowledge.category.slug_taken`); log 101 z tym samym
   `TraceId` mówi, ile trwało żądanie.

**Lokalnie** nie ma stosu obserwowalności (ADR-0034). Włącz zakresy w konsoli procesu (JSON + `IncludeScopes`, przepis 9.7
w [Lokalnym środowisku](13-lokalne-srodowisko-i-debugowanie.md)) i wyszukaj `traceId` w `docker compose logs`.

---

## 5. Health endpointy

Szczegóły w [Architekturze w praktyce, rozdział 6](02-architektura-w-praktyce.md#6-start-procesu-i-sondy) i ADR-0018. Z punktu
widzenia obserwowalności:

| Endpoint | Kto pyta | Co oznacza `Unhealthy` |
|---|---|---|
| `/health/startup` | `startupProbe` | brak bazy albo oczekujące migracje (brama: także brak konfiguracji tras); pod nie dostaje ruchu |
| `/health/ready` | `readinessProbe` | proces się zamyka (SIGTERM) |
| `/health/live` | `livenessProbe` | nigdy (brak checków); brak odpowiedzi = restart |
| `/health/dependencies` | **monitoring** | MSSQL, Redis (jeśli skonfigurowany) albo bus MassTransit niedostępny; alert, nie restart |

Odpowiedź to sam status tekstowy (`Healthy`, `Degraded`, `Unhealthy`), bez szczegółów:

```bash
curl -s http://localhost:5101/health/dependencies     # Healthy
```

Szczegóły nieudanego checku (np. lista oczekujących migracji) nie trafiają do odpowiedzi; sprawdź je bezpośrednio
(`dotnet ef migrations list --connection ...`, [Lokalne środowisko](13-lokalne-srodowisko-i-debugowanie.md), rozdział 6).

---

## Typowe błędy

| Symptom | Przyczyna | Naprawa |
|---|---|---|
| W logach brak pól (`{MaterialId}` jako część tekstu), nie da się filtrować | interpolacja `$"..."` albo `LogInformation` | metoda `[LoggerMessage]` z nazwanymi polami |
| Dwa różne komunikaty z tym samym `EventId` | numer nie z rejestru | weź wolny numer z zakresu komponentu, popraw rejestr |
| Token albo e-mail w logach | logowanie całych obiektów/nagłówków | loguj identyfikator zasobu i kod błędu; usuń wpis, zgłoś incydent zgodnie z procedurą |
| Własne spany/metryki nie pojawiają się w Grafanie | `ActivitySource`/`Meter` nie jest dodany do providera | `ConfigureOpenTelemetryTracerProvider(... AddSource)` / `ConfigureOpenTelemetryMeterProvider(... AddMeter)` |
| Eksplozja liczby serii w Prometheusie | atrybut metryki z identyfikatorem (użytkownik, materiał) | tylko atrybuty o małej liczności |
| Ślad urywa się na bramie | wywołanie nie przez instrumentowany `HttpClient` (np. własny `SocketsHttpHandler` bez fabryki) | używaj `IHttpClientFactory` |
| Stos wyjątku nie trafia do logu | wyjątek przekazany jako argument szablonu (`{Error}` z `exception.Message`) | parametr typu `Exception` w metodzie `[LoggerMessage]` |
| Brak telemetrii w środowisku dev/test | nie ustawiono `OTEL_EXPORTER_OTLP_ENDPOINT` | wartość w `values.yaml` chartu |

## Do zapamiętania

- Logi tylko przez `[LoggerMessage]`, `EventId` z rejestru, bez danych osobowych i tokenów; wyjątek jako parametr `Exception`.
- `LoggingBehavior` loguje i mierzy każde żądanie; nie dubluj tego w handlerach.
- Poziomy: `Information` = normalne zdarzenie, `Warning` = poradziliśmy sobie, `Error` = potrzebny człowiek.
- OTLP włącza się zmienną `OTEL_EXPORTER_OTLP_ENDPOINT`; lokalnie nic nie jest eksportowane.
- Własne źródła i mierniki trzeba zarejestrować w providerach.
- `traceId` z `ProblemDetails` (32 znaki hex, ADR-0044) prowadzi do pełnego śladu: brama, serwis, SQL, outbox, konsument.

## Powiązane

- [Rejestr EventId](../logowanie-eventid.md), dokument architektury [§8.9 Obserwowalność](../architektura.md#89-obserwowalność).
- Rozdziały: [Architektura w praktyce](02-architektura-w-praktyce.md), [Cache](11-cache.md),
  [Zdarzenia i integracja](10-zdarzenia-i-integracja.md), [Lokalne środowisko i debugowanie](13-lokalne-srodowisko-i-debugowanie.md),
  [Rozwiązywanie problemów](17-rozwiazywanie-problemow.md), [Analityka i feature flags](21-analityka-i-feature-flags.md).
- ADR: [0008](../adr/0008-logowanie-loggermessage-i-opentelemetry.md), [0018](../adr/0018-health-checki-readiness.md),
  [0020](../adr/0020-cache-l1-l2-redis.md), [0022](../adr/0022-konfiguracja-yarp-w-bazie.md).
