# 2. Architektura w praktyce: jak działa system od żądania do zdarzenia

Architektura procesów i przepływ żądań: od bramy brzegowej YARP (`bff-web`) i BFF experience, przez pipeline MediatR, agregat domenowy i transakcyjny outbox, po publikację zdarzeń do RabbitMQ, obsługę cache oraz composition rooty.

**Wymagania wstępne.** [Start](01-start.md) (uruchomione środowisko), [Zasady](03-zasady.md). Rozdział jest „mapą”: szczegóły
poszczególnych warstw opisują [Warstwa aplikacji](06-warstwa-aplikacji.md), [Dane i EF Core](07-dane-i-ef-core.md),
[Bezpieczeństwo](09-bezpieczenstwo.md), [Zdarzenia i integracja](10-zdarzenia-i-integracja.md) i [Cache](11-cache.md).

> **W skrócie**
>
> - Ruch idzie: moduł → brama brzegowa → **BFF experience** (`src/Bff/Example.Bff`) → serwisy domenowe (ADR-0037–0041; zob.
>   [Experience w repozytorium](#experience-w-repozytorium)).
> - Klient rozmawia tylko z bramą YARP (`bff-web` z ciasteczkiem albo `gateway-mobile` z JWT). `SuperApp.Gateway` to lokalny zamiennik
>   wspólnej bramy brzegowej. Trzyma sesję i tokeny po stronie serwera (MSSQL, schemat `gateway`), sprawdza CSRF i scope „grubo”,
>   a dalej wysyła `Authorization: Bearer`. Jedyna trasa experience to `/api/example/v{n}/**` do BFF.
> - BFF waliduje JWT i przekazuje wywołanie do serwisu klientem Refit z **tym samym** tokenem; odpowiedź serwisu, także błąd
>   z `code`, wraca do modułu bez zmian.
> - Serwis sam waliduje JWT, kontroler tylko tłumaczy HTTP na komendę/zapytanie i woła `ISender.Send`.
> - Pipeline MediatR w stałej kolejności: **logowanie → autoryzacja (scope) → walidacja → transakcja (tylko komendy)**.
> - Handler komendy nie zapisuje. Robi to `TransactionBehavior`: `IUnitOfWork.SaveChangesAsync` (zdarzenia domenowe → outbox →
>   `INSERT/UPDATE`) i `COMMIT`; po commicie interceptor uruchamia akcje `OnCommitted` (unieważnienie cache).
> - `Result` z błędem zamienia się w `application/problem+json` z `code` i `traceId` w jednym miejscu: `ResultHttpExtensions`.
> - Zdarzenia integracyjne lądują w tabeli `OutboxMessage` w tej samej transakcji; wysyła je Worker, odbiera konsument
>   z inboxem i zamienia na komendę.
> - Proces nigdy nie migruje bazy; `/health/startup` jest zielony dopiero, gdy nie ma oczekujących migracji.

W blokach kodu pominięto komentarze dokumentacji XML (są w plikach źródłowych); poza tym kod jest skopiowany z repozytorium.

---

## Experience w repozytorium

Rozwiązanie organizacji to **super app**, a nasz zespół buduje w nim **jedną experience**: moduł klienta, **BFF experience** i 1..n
serwisów domenowych (ADR-0038). Słowo „BFF” oznacza w tym podręczniku BFF experience; profil bramy `bff-web` ma nazwę historyczną.

```mermaid
flowchart LR
    MOD[moduł experience<br/>w shellu super appki] --> EDGE[wspólna brama brzegowa<br/>poza zakresem; lokalnie SuperApp.Gateway]
    EDGE -- "/api/{experience}/v{n}/**" --> PUB["BFF experience<br/>API publiczne /v{n}"]
    OTHER[BFF innej experience] -- "token użytkownika" --> INT["BFF experience<br/>API wewnętrzne /internal/v{n}"]
    PUB --> SVC[serwisy domenowe experience<br/>tylko wewnętrznie]
    INT --> SVC
    OTHER -. "zabronione (NetworkPolicy)" .-x SVC
```

| Element | Zasada (ADR-0037–0042) | W repozytorium |
|---|---|---|
| Brama brzegowa | wspólna dla super appki, utrzymuje ją inny właściciel; zamiana sesji na JWT, walidacja JWT, CSRF, unieważnianie | `SuperApp.Gateway` (profile `bff-web`, `gateway-mobile`) jako **lokalny zamiennik** w compose, E2E i IDE oraz specyfikacja wymagań wobec wspólnej bramy (ADR-0037) |
| BFF experience | bezstanowy host ASP.NET Core MVC: orkiestruje wywołania serwisów, kształtuje dane dla modułu, waliduje JWT | `src/Bff/Example.Bff` (Deployment `example-bff`, chart `superapp-bff`): fasada 30 operacji Knowledge i SleepDiary pod `/v1/knowledge/...` i `/v1/sleepdiary/...`, endpoint komponowany `GET /v1/me/summary`; szablon `dotnet new superapp-bff` |
| Trasy brzegu | wyłącznie `/api/{experience}/v{n}/**` do API publicznego BFF | jedna trasa `example-bff`: `/api/example/v{version:int}/{**rest}` → `example-bff.example.svc.cluster.local:8080`; tras do serwisów domenowych nie ma (migracja `RouteExperienceThroughBff`) |
| API wewnętrzne | `/internal/v{n}` w BFF dla BFF-ów innych experience, tylko w klastrze | `GET /internal/v1/widgets/sleep-summary`, scope `example.internal.read`; brama nigdy go nie routuje |
| Wywołania w kontekście użytkownika | token użytkownika przekazywany bez zmian (ADR-0040) | `AddDownstreamApi<T>(...).AddUserTokenForwarding()` w `SuperApp.Framework.Infrastructure/Http`; `IDownstreamTokenProvider` to furtka na token exchange |
| Izolacja ruchu | NetworkPolicy: serwisy domenowe przyjmują ruch tylko od BFF i serwisów tej samej experience (ADR-0041) | charty mają etykiety `app.kubernetes.io/part-of: {experience}` i `superapp.example/experience-role`; polityki to wymaganie wobec działu infrastruktury; lokalnie compose ich nie egzekwuje |

Ścieżki łatwo przeliczyć: moduł woła `/api/example/v1/knowledge/categories`, brama usuwa prefiks `/api/example`, BFF dostaje
`/v1/knowledge/categories`, a jego akcja woła serwis Knowledge pod `/v1/categories`. Zasady serwisu (walidacja JWT, pipeline,
`Result`, outbox) nie zależą od tego, kto woła serwis.

## 1. Komponenty

```mermaid
flowchart LR
    SPA[Angular SPA] -- "ciasteczko __Host-bff + X-CSRF: 1" --> BFF[bff-web<br/>SuperApp.Gateway]
    MOB[Android / iOS] -- "Authorization: Bearer" --> MGW[gateway-mobile<br/>SuperApp.Gateway]
    SPA -. "nawigacja /bff/login" .-> CIAM[(CIAM OIDC<br/>lokalnie Keycloak)]
    BFF -- "OIDC code + PKCE, refresh" --> CIAM
    BFF -- "/api/example/v1/** → /v1/**, Bearer JWT" --> XBFF[example-bff<br/>BFF experience]
    MGW -- "ten sam Bearer JWT" --> XBFF
    XBFF -- "ten sam Bearer JWT" --> KAPI[knowledge-api]
    XBFF -- "ten sam Bearer JWT" --> SAPI[sleepdiary-api]
    KAPI --> SQL[(MSSQL SuperApp<br/>schematy gateway, knowledge, sleepdiary)]
    SAPI --> SQL
    BFF --> SQL
    KW[knowledge-worker] --> SQL
    SW[sleepdiary-worker] --> SQL
    KW <-- "AMQP" --> MQ[(RabbitMQ)]
    SW <--> MQ
    KAPI --> RD[(Redis L2)]
    BFF --> RD
    MIG[SuperApp.Migrator<br/>Job przed rolloutem] --> SQL
    FWD[analytics-forwarder] <-- "AMQP" --> MQ
    FWD -- "zdarzenia produktowe" --> PH[(PostHog Cloud EU)]
    BFF -- "/ingest (bez sesji i tokenu)" --> PH
```

| Komponent | Projekt | Proces / Deployment | Co robi | Czego **nie** robi |
|---|---|---|---|---|
| `bff-web` | `src/Gateway/SuperApp.Gateway` (`Gateway:Profile=bff-web`); lokalny zamiennik wspólnej bramy brzegowej (ADR-0037) | `bff-web` | logowanie OIDC, sesja w MSSQL, odświeżanie tokenów, CSRF, `/bff/*`, routing YARP do BFF experience | nie zna reguł domenowych; nie przekazuje ciasteczek dalej; nie routuje do serwisów domenowych ani do `/internal` |
| `gateway-mobile` | ten sam projekt (`Gateway:Profile=gateway-mobile`) | `gateway-mobile` | walidacja JWT aplikacji mobilnej, routing YARP do BFF experience | nie ma sesji ani `/bff/*` |
| `{experience}-bff` | `src/Bff/Example.Bff` (szablon `src/Tools/SuperApp.Cli/Templates/superapp-bff`) | `example-bff` (chart `superapp-bff`) | walidacja JWT (audience `example-bff`), API publiczne `/v1/...` dla modułu (fasada serwisów i endpointy komponowane), API wewnętrzne `/internal/v1/...` dla innych experience, wywołania serwisów z tokenem użytkownika | nie ma bazy, domeny ani migracji; nie waliduje treści żądań i nie zmienia błędów serwisów; nie woła innych BFF poza ich API wewnętrznym |
| `{serwis}-api` | `{Serwis}.Api` | `knowledge-api`, `sleepdiary-api` | kontrolery MVC, walidacja JWT, komendy i zapytania; zapis do outboxa | nie wysyła outboxa do RabbitMQ (`OutboxDelivery.Disabled`) |
| `{serwis}-worker` | `{Serwis}.Worker` | `knowledge-worker`, `sleepdiary-worker` | konsumenci RabbitMQ, **dostarczanie outboxa** (`OutboxDelivery.Enabled`) | nie obsługuje ruchu HTTP poza `/health/*` |
| `analytics-forwarder` | `src/Analytics/SuperApp.AnalyticsForwarder` | `analytics-forwarder` (chart `superapp-analytics-forwarder`) | subskrybuje zdarzenia integracyjne serwisów i wysyła dozwolone zdarzenia produktowe do PostHog; bez analityki tylko loguje (EventId 9001) ([21.5](21-analityka-i-feature-flags.md#215-zdarzenia-z-backendu-superappanalyticsforwarder)) | nie ma bazy, domeny, inboxu ani komend; nie zmienia stanu |
| `SuperApp.Migrator` | `src/Migrator/SuperApp.Migrator` | Job `superapp-migrator` (tylko dev/test) | `MigrateAsync` dla bramy i każdego serwisu | nie istnieje na prod (tam skrypt DBA, ADR-0004) |
| MSSQL | dostarcza dział infrastruktury (ADR-0016) | baza `SuperApp` | jeden schemat na serwis (`knowledge`, `sleepdiary`) i `gateway` | brak odwołań między schematami (ADR-0021) |
| Redis | jw. | | L2 `HybridCache`; w bramie także ostatnia poprawna konfiguracja tras | nie jest źródłem prawdy ani magazynem sesji |
| RabbitMQ | jw. | | zdarzenia integracyjne, kolejki quorum | |
| CIAM | osobny dział; lokalnie Keycloak (ADR-0031) | | wydaje tokeny, sesja SSO, back-channel logout | |
| PostHog Cloud EU | SaaS, projekt na środowisko (ADR-0036) | | analityka produktowa, session replay, feature flags | nie dostaje `sub`, e-maili ani danych z dziennika snu; użytkownik to pseudonim `u_…` |

Lokalna brama `bff-web` ma dodatkowo proxy `/ingest/{**path}` do PostHog, zdefiniowane **w kodzie** (`Analytics/AnalyticsEndpoints.cs`),
nie w trasach z bazy: usuwa `Cookie`, `Authorization`, `X-User-*`, `X-Forwarded-*` i istnieje tylko przy włączonej analityce.
`gateway-mobile` wystawia `GET /analytics/id`, a `/bff/user` zwraca `analyticsId`. Serwisy czytają feature flags przez port
`IFeatureFlags` (`AddAppFeatureFlags` w `Add{Serwis}Core`). Szczegóły: [21 Analityka i feature flags](21-analityka-i-feature-flags.md). Zgodnie z ADR-0037 proxy `/ingest` i
`analyticsId` są wymaganiem wobec wspólnej bramy albo przyszłymi endpointami BFF experience.

### 1.1 Co jest w bazie

Jedna baza `SuperApp`, schemat na komponent (ADR-0021). Każdy serwis łączy się **własnym loginem** `{serwis}_app`, który ma
uprawnienia DML wyłącznie do swojego schematu (`deploy/sql/01-bootstrap.sql`), więc zapytanie do cudzego schematu kończy się
błędem uprawnień, a nie „cichym” sprzężeniem.

| Schemat | Tabele | Kto pisze |
|---|---|---|
| `gateway` | `Sessions` (zaszyfrowane tickety sesji BFF), `DataProtectionKeys`, `Clusters`, `Destinations`, `Routes`, `RouteMethods`, `RouteHosts`, `RouteTransforms` (+ tabele `*History`, bo tabele tras są temporalne), `__EFMigrationsHistory` | brama: sesje i klucze; trasy **wyłącznie migracje** (`DENY INSERT/UPDATE/DELETE` dla `gateway_role`, `deploy/sql/02-gateway-config-permissions.sql`) |
| `knowledge` | `Categories`, `Materials`, `ContentBlocks`, `ContentTextSpans`, `MaterialCategories`, `Collections`, `CollectionItems`, `CollectionCategories`, `Favorites`, `MaterialCompletions`, **`OutboxMessage`, `OutboxState`, `InboxState`**, `__EFMigrationsHistory` | `knowledge_app` (API i Worker) |
| `sleepdiary` | `SleepEntries`, `OutboxMessage`, `OutboxState`, `InboxState`, `__EFMigrationsHistory` | `sleepdiary_app` |

Tabele outboxa i inboxa MassTransit dodaje do modelu każdego `WriteDbContext` klasa bazowa `WriteDbContextBase`
(`AddInboxStateEntity`, `AddOutboxMessageEntity`, `AddOutboxStateEntity`), dlatego są w schemacie serwisu i w jego migracjach.

### 1.2 Co jest w RabbitMQ

MassTransit tworzy topologię sam przy starcie procesu z konsumentami. Stan lokalnego środowiska (odczytany z panelu RabbitMQ):

| Obiekt | Nazwa | Skąd |
|---|---|---|
| exchange typu wiadomości (fanout) | `Knowledge.Contracts:MaterialArchivedV1`, `Knowledge.Contracts:CollectionArchivedV1`, `Knowledge.Contracts:MaterialPublishedV1`, `SleepDiary.Contracts:SleepEntryRecordedV1` | nazwa = `{namespace}:{typ}` kontraktu |
| exchange + kolejka endpointu | `knowledge-material-archived`, `knowledge-collection-archived` (kolejki **quorum**) | `KebabCaseEndpointNameFormatter("knowledge", ...)` + nazwa konsumenta bez sufiksu `Consumer` |
| kolejki forwardera analityki | `analytics-material-published`, `analytics-material-archived`, `analytics-collection-archived`, `analytics-sleep-entry-recorded` (quorum) | `AddAppEventSubscriber(..., "analytics", ...)` w `SuperApp.AnalyticsForwarder` |
| kolejka błędów | `knowledge-material-archived_error` | tworzona przy pierwszym błędzie trwałym |

Wiązanie: `Knowledge.Contracts:MaterialArchivedV1` → exchange `knowledge-material-archived` → kolejka `knowledge-material-archived`.
Każde zdarzenie integracyjne ma co najmniej jednego subskrybenta: forwarder analityki (np. `SleepEntryRecordedV1` →
`analytics-sleep-entry-recorded`). Zdarzenie bez subskrybenta trafiłoby do exchange bez powiązań i zostałoby porzucone przez
brokera: to poprawne zachowanie publish/subscribe.

---

## 2. Composition rooty: co rejestruje każdy proces

Każdy host składa się z kilku wywołań frameworka. Nie konfigurujesz hosta ręcznie: jeśli czegoś brakuje, dodaje się to
w `SuperApp.Framework` albo w `Add{Serwis}Core`, a nie w `Program.cs`.

### 2.1 API

`src/Services/Knowledge/Knowledge.Api/Program.cs` (cały plik):

```csharp
using SuperApp.Framework.Infrastructure.Hosting;
using SuperApp.Framework.Infrastructure.Messaging;
using Knowledge.Infrastructure;
using Knowledge.Infrastructure.Persistence.Write;

var builder = WebApplication.CreateBuilder(args);

builder.AddAppServiceDefaults<KnowledgeWriteDbContext>("knowledge-api");
builder.AddAppApi();
builder.Services.AddOpenApi(options => HostingExtensions.ConfigureOpenApi(options));
builder.Services.AddKnowledgeInfrastructure(builder.Configuration, OutboxDelivery.Disabled);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapOpenApi().AllowAnonymous();
app.MapAppDefaultEndpoints();

await app.RunAsync();

public partial class Program;
```

### 2.2 Worker

`src/Services/Knowledge/Knowledge.Worker/Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddAppServiceDefaults<KnowledgeWriteDbContext>("knowledge-worker");
builder.AddAppWorker();
builder.Services.AddKnowledgeInfrastructure(
    builder.Configuration,
    OutboxDelivery.Enabled,
    bus => bus.AddConsumers(typeof(Program).Assembly));

var app = builder.Build();

app.MapAppDefaultEndpoints();

await app.RunAsync();
```

Worker jest aplikacją webową tylko po to, żeby wystawić `/health/*` dla sond. Nie ma kontrolerów ani uwierzytelnienia JWT.

### 2.3 Co rejestruje każde wywołanie

| Wywołanie | Plik | Rejestruje |
|---|---|---|
| `AddAppServiceDefaults<TWrite>(name)` | `SuperApp.Framework.Infrastructure/Hosting/HostingExtensions.cs` | OpenTelemetry (`service.name` = `name`), `TimeProvider`, `IClock`, `ProblemDetails`, health checki: `migrations` (startup), `shutdown` (ready), `mssql` i opcjonalnie `redis` (dependencies) |
| `AddAppApi()` | jw. | MVC + kontrakt JSON, `ICurrentUser` = `HttpCurrentUser`, `AddJwtBearer` z sekcji `Authentication`, polityka fallback „uwierzytelniony użytkownik” |
| `AddAppWorker()` | jw. | `ICurrentUser` = `SystemCurrentUser` (uwierzytelniony, bez `sub`, ma każdy scope) |
| `Add{Serwis}Infrastructure(cfg, delivery, consumers)` | `{Serwis}.Infrastructure/InfrastructureServiceCollectionExtensions.cs` | `Add{Serwis}Core` + `AddAppMessaging` (MassTransit, outbox, inbox) |
| `Add{Serwis}Core(cfg)` | jw. | `AddAppApplication` (MediatR, behaviors, walidatory, handlery zdarzeń domenowych), `AddAppPersistence` (`WriteDbContext` = `IUnitOfWork`, `ReadDbContext`, dispatcher, interceptor), `AddAppCaching` (`HybridCache` + `FailSafeCache`), `AddAppFeatureFlags` (`IFeatureFlags`), repozytoria |

`AddAppServiceDefaults` (`src/Framework/SuperApp.Framework.Infrastructure/Hosting/HostingExtensions.cs`):

```csharp
public static IHostApplicationBuilder AddAppServiceDefaults(this IHostApplicationBuilder builder, string serviceName)
{
    builder.AddAppTelemetry(serviceName);
    builder.Services.TryAddSingleton(TimeProvider.System);
    builder.Services.TryAddSingleton<IClock, SystemClock>();
    builder.Services.AddProblemDetails();

    var health = builder.Services.AddHealthChecks()
        .AddCheck<ShutdownHealthCheck>("shutdown", tags: [HealthTags.Ready]);

    if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Redis")))
    {
        health.AddCheck<DistributedCacheHealthCheck>("redis", tags: [HealthTags.Dependencies]);
    }

    return builder;
}

public static IHostApplicationBuilder AddAppServiceDefaults<TWriteDbContext>(this IHostApplicationBuilder builder, string serviceName)
    where TWriteDbContext : DbContext
{
    builder.AddAppServiceDefaults(serviceName);
    builder.Services.AddHealthChecks()
        .AddCheck<MigrationsAppliedHealthCheck<TWriteDbContext>>("migrations", tags: [HealthTags.Startup])
        .AddDbContextCheck<TWriteDbContext>("mssql", tags: [HealthTags.Dependencies]);

    return builder;
}
```

Wariant bez `TWriteDbContext` (bez sprawdzenia migracji i MSSQL) wywołuje BFF experience, który nie ma bazy:
`builder.AddAppServiceDefaults("example-bff")`.

`AddKnowledgeCore` (`src/Services/Knowledge/Knowledge.Infrastructure/InfrastructureServiceCollectionExtensions.cs`):

```csharp
public static IServiceCollection AddKnowledgeCore(this IServiceCollection services, IConfiguration configuration)
{
    services.AddAppApplication(KnowledgeApplication.Assembly, typeof(InfrastructureServiceCollectionExtensions).Assembly);

    services.AddAppPersistence<KnowledgeWriteDbContext, KnowledgeReadDbContext>(configuration, KnowledgeWriteDbContext.SchemaName);
    services.AddAppCaching(configuration, KnowledgeWriteDbContext.SchemaName);
    services.AddAppFeatureFlags(configuration);

    services.AddScoped<ICategoryRepository, CategoryRepository>();
    services.AddScoped<IMaterialRepository, MaterialRepository>();
    services.AddScoped<ICollectionRepository, CollectionRepository>();
    services.AddScoped<IFavoriteRepository, FavoriteRepository>();
    services.AddScoped<IMaterialCompletionRepository, MaterialCompletionRepository>();

    return services;
}
```

Dwa zestawy (assembly) są skanowane: **Application** (komendy, handlery komend, walidatory, translatory zdarzeń) i
**Infrastructure** (handlery zapytań, ADR-0026, oraz techniczne handlery zdarzeń domenowych, np. unieważnianie cache).
Nowy handler nie wymaga rejestracji; nowe repozytorium tak (jedna linia w `Add{Serwis}Core`).

Testy integracyjne wołają `AddKnowledgeCore` bez `AddAppMessaging` i podstawiają fałszywy `IIntegrationEventPublisher`
(`ServiceFixture`), dlatego granica Core/Infrastructure przebiega właśnie tu.

### 2.4 Zakresy DI: dlaczego wszystko dzieli jedną transakcję

| Usługa | Czas życia | Konsekwencja |
|---|---|---|
| `KnowledgeWriteDbContext` i `IUnitOfWork` | scoped, **ta sama instancja** (`AddScoped<IUnitOfWork>(p => p.GetRequiredService<TWrite>())`) | repozytoria, translatory i behavior transakcyjny pracują na jednym kontekście i jednej transakcji |
| `IDomainEventDispatcher` | scoped | handlery zdarzeń domenowych są pobierane z tego samego zakresu, więc publikowane przez nie wiadomości trafiają do outboxa tego samego zapisu |
| `IDomainEventHandler<T>` | scoped (rejestrowane przez `AddAppApplication`) | |
| `KnowledgeReadDbContext` | scoped, bez śledzenia zmian | |
| `FailSafeCache` | scoped; `HybridCache` singleton | |
| `AfterCommitInterceptor` | singleton (bezstanowy, akcje trzyma kontekst) | |

Zakres = jedno żądanie HTTP (API) albo jedna wiadomość (Worker, zakres tworzy MassTransit).

---

## 3. Życie żądania HTTP: komenda

Przykład: redaktor w aplikacji web tworzy kategorię. Przeglądarka wysyła `POST /api/example/v1/knowledge/categories`.

```mermaid
sequenceDiagram
    autonumber
    participant B as Przeglądarka (SPA)
    participant G as bff-web
    participant GS as MSSQL gateway.Sessions
    participant C as CIAM
    participant X as example-bff
    participant A as knowledge-api
    participant P as Pipeline MediatR
    participant H as CreateCategoryHandler
    participant U as KnowledgeWriteDbContext (UoW)
    participant DB as MSSQL knowledge

    B->>G: POST /api/example/v1/knowledge/categories<br/>Cookie: __Host-bff=…, X-CSRF: 1
    G->>G: CsrfHeaderMiddleware (brak X-CSRF → 401 auth.csrf_header_missing)
    G->>G: Routing: trasa example-bff z bazy
    G->>GS: DbTicketStore.RetrieveAsync(klucz z ciasteczka)
    GS-->>G: ticket (claims + tokeny), odszyfrowany Data Protection
    opt access token wygasa za < 60 s
        G->>GS: sp_getapplock sesji, ponowny odczyt
        G->>C: grant_type=refresh_token
        C-->>G: nowe tokeny
        G->>GS: zapis ticketu
    end
    G->>G: Polityka trasy "example" (scope knowledge.* lub sleepdiary.*), rate limit
    G->>X: POST /v1/knowledge/categories<br/>Authorization: Bearer <access token>, bez Cookie i X-User-*
    X->>X: JwtBearer: podpis, issuer, audience=example-bff
    X->>A: KnowledgeCategoriesController.Create → CategoriesCreateAsync<br/>POST /v1/categories, ten sam Bearer
    A->>A: JwtBearer: podpis, issuer, audience=knowledge-api
    A->>P: CategoriesController.Create → ISender.Send(CreateCategory)
    P->>P: Logging → Authorization (knowledge.catalog.write) → Validation
    P->>U: TransactionBehavior: BEGIN TRANSACTION
    P->>H: Handle
    H->>DB: SlugExistsAsync
    H->>H: Category.Create → Raise(CategoryChanged), categories.Add
    H-->>P: Result<Guid>.Success
    P->>U: IUnitOfWork.SaveChangesAsync
    U->>U: dispatch CategoryChanged → CategoryCacheInvalidation.OnCommitted(...)
    U->>DB: INSERT Categories
    P->>DB: COMMIT
    U->>U: AfterCommitInterceptor: RemoveByTag("knowledge:categories")
    P-->>A: Result<Guid>
    A-->>X: 201 {"id": "…"}
    X-->>G: 201 {"id": "…"} (ToActionResult, bez zmian)
    G-->>B: 201 {"id": "…"}
```

### 3.1 Brama `bff-web`: kolejność middleware

Kolejność jest w `src/Gateway/SuperApp.Gateway/Program.cs` i ma znaczenie:

```csharp
app.UseExceptionHandler();
if (profile == GatewayProfiles.BffWeb)
{
    app.UseMiddleware<CsrfHeaderMiddleware>();
}

app.UseRouting();
app.UseRequestTimeouts();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (profile == GatewayProfiles.BffWeb)
{
    app.MapBffEndpoints();
}

// PostHog proxy /ingest (bff-web) and GET /analytics/id (gateway-mobile); code-defined, never database routes (ADR-0036).
app.MapAnalyticsEndpoints(profile);

app.MapReverseProxy();
app.MapAppDefaultEndpoints();
```

| Krok | Co się dzieje | Odpowiedź przy niepowodzeniu |
|---|---|---|
| 1. CSRF | `/api/*` bez `X-CSRF: 1` jest odrzucane **przed** routingiem i uwierzytelnieniem | `401 auth.csrf_header_missing` |
| 2. Routing | wybór endpointu `/bff/*` albo trasy YARP z bazy (`DatabaseProxyConfigProvider`) | `404 http.not_found` |
| 3. Timeout | `ProxyRoute.TimeoutSeconds` z bazy (seed: 30 s) przez middleware request timeouts | przerwanie żądania |
| 4. Uwierzytelnienie | ciasteczko `__Host-bff` → klucz sesji → ticket z `gateway.Sessions`; `TokenRefresher` w `OnValidatePrincipal` | brak sesji = anonimowy |
| 5. Autoryzacja | polityka trasy (`Routes.AuthorizationPolicy`, np. `example`); fallback: uwierzytelniony użytkownik | `401 auth.invalid_token` (brak sesji) lub `403 auth.forbidden` |
| 6. Rate limiting | polityka `per-user` (domyślnie 600 żądań na minutę na `sub` lub IP) | `429 http.too_many_requests` |
| 7. YARP | transformy bezpieczeństwa, przekazanie do klastra; ponowienie GET/HEAD przy 502/503/504 | `502`/`504` `server.error` przy braku połączenia |

Odpowiedzi z tabeli to własne błędy bramy: `application/problem+json` z kodem ogólnym, `traceId` i `instance` ze ścieżką bramy
(`/api/example/...`), dopisywane przez `GatewayStatusCodePages` (ADR-0044). Odpowiedź przekazana z BFF nie jest modyfikowana.
Nawigacja przeglądarki, która nie akceptuje JSON, dostaje puste ciało.

Middleware CSRF (`src/Gateway/SuperApp.Gateway/Bff/Security/CsrfHeaderMiddleware.cs`):

```csharp
internal sealed class CsrfHeaderMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) && context.Request.Headers["X-CSRF"] != "1")
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        return next(context);
    }
}
```

Dlaczego nagłówek wystarcza: przeglądarka sama dołącza ciasteczko do żądań z obcej strony, ale nie doda własnego nagłówka
(zwykły formularz tego nie potrafi, a skrypt z innego originu wymagałby preflightu CORS, na który brama nie pozwala). Razem
z `SameSite=Strict` blokuje to CSRF.

### 3.2 Sesja: ciasteczko zawiera tylko klucz

Konfiguracja ciasteczka w `Program.cs` (fragment `AddBffWeb`):

```csharp
.AddCookie(options =>
{
    options.Cookie.Name = "__Host-bff";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.Path = "/";
    options.ExpireTimeSpan = builder.Configuration.GetValue("Gateway:SessionLifetime", TimeSpan.FromHours(8));
    options.SlidingExpiration = true;
    options.Events.OnValidatePrincipal = context =>
        context.HttpContext.RequestServices.GetRequiredService<TokenRefresher>().ValidatePrincipalAsync(context);
    options.Events.OnRedirectToLogin = context => Reject(context.Response, StatusCodes.Status401Unauthorized);
    options.Events.OnRedirectToAccessDenied = context => Reject(context.Response, StatusCodes.Status403Forbidden);
})
```

`TicketStoreCookieSetup` ustawia `CookieAuthenticationOptions.SessionStore = DbTicketStore`. Od tej chwili ciasteczko zawiera
tylko 64-znakowy losowy klucz, a ticket (claims, `access_token`, `refresh_token`, `expires_at`) leży w `gateway.Sessions`,
zaszyfrowany Data Protection (cel `SuperApp.Gateway.Bff.SessionTicket.v1`). Odczyt przy każdym żądaniu
(`src/Gateway/SuperApp.Gateway/Bff/Sessions/DbTicketStore.cs`):

```csharp
public async Task<AuthenticationTicket?> RetrieveAsync(string key)
{
    await using var scope = scopeFactory.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();

    var session = await db.Sessions.AsNoTracking().FirstOrDefaultAsync(row => row.Id == key);
    if (session is null || session.ExpiresAt <= timeProvider.GetUtcNow())
    {
        return null;
    }

    var ticket = Unprotect(session);
    if (ticket is not null)
    {
        ticket.Properties.Items[SessionKeyItem] = key;
    }

    return ticket;
}
```

`null` (brak wiersza, wygasła sesja, ticket nie do odszyfrowania po utracie kluczy) oznacza żądanie anonimowe, a więc `401`
dla SPA. Klucze Data Protection są wspólne dla replik i leżą w `gateway.DataProtectionKeys` (`GatewayDataProtection`).

### 3.3 Odświeżanie tokenu: dokładnie raz na sesję

`TokenRefresher.ValidatePrincipalAsync` działa przy każdym żądaniu z sesją
(`src/Gateway/SuperApp.Gateway/Bff/Tokens/TokenRefresher.cs`):

```csharp
public async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
{
    if (!NeedsRefresh(context.Properties)
        || !context.Properties.Items.TryGetValue(DbTicketStore.SessionKeyItem, out var sessionKey)
        || sessionKey is null)
    {
        return;
    }

    var refresh = _inFlight.GetOrAdd(sessionKey, key => new Lazy<Task<RefreshOutcome>>(() => RefreshSessionAsync(key)));
    RefreshOutcome outcome;
    try
    {
        outcome = await refresh.Value;
    }
    finally
    {
        _inFlight.TryRemove(new KeyValuePair<string, Lazy<Task<RefreshOutcome>>>(sessionKey, refresh));
    }

    if (outcome.Tokens is { } tokens)
    {
        context.Properties.StoreTokens(tokens);
    }
    else if (outcome.SignOut)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
```

| Sytuacja | Zachowanie |
|---|---|
| token ważny dłużej niż 60 s | nic się nie dzieje |
| kilka równoległych żądań tej samej sesji w jednej replice | jedno wspólne odświeżenie (`_inFlight`, single-flight) |
| żądania tej samej sesji w różnych replikach | odświeżenie pod blokadą `sp_getapplock` sesji w `DbTicketStore.UpdateExclusiveAsync`; druga replika czyta już nowe tokeny i nie woła CIAM |
| CIAM odrzuca refresh (sesja SSO zakończona) | sesja wylogowana, żądanie leci dalej jako anonimowe → SPA dostaje `401` |
| timeout (10 s) lub brak blokady (15 s) | sesja zostaje, żądanie idzie ze starym tokenem, następne spróbuje ponownie |

Dlaczego tyle zachodu: realm ma rotację refresh tokenów z jednorazowym użyciem (`revokeRefreshToken: true`,
`refreshTokenMaxReuse: 0` w `deploy/local/keycloak/realm-superapp.json`). Drugie użycie tego samego refresh tokenu CIAM traktuje
jak kradzież i kończy sesję. Szczegóły: [Bezpieczeństwo](09-bezpieczenstwo.md), ADR-0011, ADR-0013.

### 3.4 Autoryzacja w bramie i trasa z bazy

Polityki bramy (`src/Gateway/SuperApp.Gateway/Security/GatewayPolicies.cs`) odpowiadają tylko na pytanie „czy klient może w ogóle
rozmawiać z experience X”. Jest jedna polityka na experience:

```csharp
public static void Register(AuthorizationBuilder authorization)
{
    authorization.AddPolicy(Example, policy => policy.RequireAuthenticatedUser()
        .RequireAssertion(context => HasScopeOf(context.User, "knowledge.") || HasScopeOf(context.User, "sleepdiary.")));
}
```

W `bff-web` claim `scope` w sesji pochodzi z odpowiedzi tokenowej (`OnTokenValidated` w `Program.cs` kopiuje
`TokenEndpointResponse.Scope`), w `gateway-mobile` z samego JWT. Token z `knowledge.catalog.read` przejdzie przez bramę także
na `POST`; to serwis odrzuci go kodem `auth.missing_scope`, bo komenda wymaga `knowledge.catalog.write`.

Trasy nie są w `appsettings`, tylko w tabelach `gateway.*` wypełnianych migracjami z `HasData` (ADR-0022). Dane początkowe
(`src/Gateway/SuperApp.Gateway/Persistence/Seed/ProxyConfigurationSeed.cs`, fragment):

```csharp
private static readonly (string Experience, string Policy)[] Experiences =
[
    ("example", GatewayPolicies.Example),
];

// Only the public API of the BFF (/v{n}/...) is routed; /internal/... never matches (ADR-0039).
modelBuilder.Entity<ProxyRoute>().HasData(
    from profile in Profiles
    from experience in Experiences
    select new ProxyRoute
    {
        Profile = profile,
        RouteId = $"{experience.Experience}-bff",
        ClusterId = experience.Experience,
        Order = 100,
        Path = $"/api/{experience.Experience}/v{{version:int}}/{{**rest}}",
        AuthorizationPolicy = experience.Policy,
        RateLimiterPolicy = GatewayRateLimits.PerUser,
        TimeoutSeconds = 30,
    });

modelBuilder.Entity<ProxyRouteTransform>().HasData(
    from profile in Profiles
    from experience in Experiences
    select new ProxyRouteTransform
    {
        Profile = profile,
        RouteId = $"{experience.Experience}-bff",
        Order = 0,
        Kind = "PathRemovePrefix",
        Value = $"/api/{experience.Experience}",
    });
```

Skutek: `/api/example/v1/knowledge/categories` w bramie to `/v1/knowledge/categories` w BFF, a klaster `example` wskazuje na
`http://example-bff.example.svc.cluster.local:8080`. Segment `v{version:int}` sprawia, że pasuje tylko wersjonowane API publiczne:
`/api/example/internal/...` nie trafia do żadnej trasy, więc API wewnętrzne BFF jest nieosiągalne z brzegu (ADR-0039; test
w `SuperApp.Gateway.Tests` sprawdza, że żadna trasa nie zawiera `internal`). Ograniczenie `CHECK` (`CK_Destinations_ClusterAddress`)
dopuszcza **wyłącznie** adresy `http://{serwis}.{namespace}.svc.cluster.local:{port}`, dlatego lokalnie kontener `example-bff`
ma alias sieciowy o tej nazwie ([Lokalne środowisko](13-lokalne-srodowisko-i-debugowanie.md)).

Dawne trasy `/api/knowledge/**` i `/api/sleepdiary/**` usunęła migracja bramy `Persistence/Migrations/RouteExperienceThroughBff.cs`
(same dane, bez zmiany schematu). Nowy serwis domenowy nie dostaje trasy w bramie: wystawia go BFF swojej experience. Nowa
experience dostaje jedną trasę do swojego BFF (wpis w `Experiences`, polityka w `GatewayPolicies`, migracja bramy).

`DatabaseProxyConfigProvider` co `Gateway:ConfigPollInterval` (domyślnie 5 s) sprawdza ostatni wpis w
`gateway.__EFMigrationsHistory`. Nowa migracja → odczyt tabel dla profilu → mapowanie i walidacja (`IConfigValidator` YARP)
→ atomowa podmiana konfiguracji i zapis kopii do cache L2. Błędna konfiguracja jest odrzucana (log 3002, licznik
`superapp.gateway.proxy_config.reload_failures`), a działa poprzednia.

### 3.5 Transformy: co dostaje BFF

`src/Gateway/SuperApp.Gateway/Proxy/Transforms/SecurityTransforms.cs`:

```csharp
public static void Apply(TransformBuilderContext context, string profile)
{
    context.AddRequestHeaderRemove("Cookie");
    context.AddRequestTransform(transform =>
    {
        var identityHeaders = transform.ProxyRequest.Headers
            .Select(header => header.Key)
            .Where(name => name.StartsWith("X-User-", StringComparison.OrdinalIgnoreCase))
            .ToList();
        identityHeaders.ForEach(name => transform.ProxyRequest.Headers.Remove(name));
        return ValueTask.CompletedTask;
    });

    if (profile == GatewayProfiles.BffWeb)
    {
        // The browser holds no tokens; the BFF attaches the access token from the server-side session.
        context.AddRequestTransform(async transform =>
        {
            transform.ProxyRequest.Headers.Authorization = null;
            var accessToken = await transform.HttpContext.GetTokenAsync("access_token");
            if (accessToken is not null)
            {
                transform.ProxyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }
        });
    }
}
```

Żądanie przeglądarki i żądanie, które dostaje BFF:

```http
POST /api/example/v1/knowledge/categories HTTP/1.1
Host: localhost:5001
Cookie: __Host-bff=CfDJ8...
X-CSRF: 1
Content-Type: application/json

{"name":"Higiena snu","slug":"higiena-snu"}
```

```http
POST /v1/knowledge/categories HTTP/1.1
Host: example-bff.example.svc.cluster.local:8080
Authorization: Bearer eyJhbGciOiJSUzI1NiIs...
traceparent: 00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01
X-CSRF: 1
Content-Type: application/json

{"name":"Higiena snu","slug":"higiena-snu"}
```

Nagłówka `Cookie` ani żadnego `X-User-*` BFF ani serwis nigdy nie dostają; `traceparent` dodaje instrumentacja HttpClient (YARP,
a w BFF klienci Refit), więc ślad ciągnie się od bramy przez BFF do serwisu ([Logowanie i obserwowalność](14-logowanie-i-obserwowalnosc.md)).

### 3.6 BFF experience: przekazanie wywołania do serwisu

BFF (`src/Bff/Example.Bff/Program.cs`) to zwykły host API frameworku (`AddAppServiceDefaults`, `AddAppApi`: walidacja JWT
z audience `example-bff`, `ProblemDetails`, sondy) bez bazy. Klienci serwisów są wygenerowani przez Refitter z commitowanych
kontraktów serwisów (`Clients/{Serwis}/Generated`) i zarejestrowani jedną linią:

```csharp
builder.Services.AddDownstreamApi<IKnowledgeApi>(builder.Configuration, "Knowledge").AddUserTokenForwarding();
builder.Services.AddDownstreamApi<ISleepDiaryApi>(builder.Configuration, "SleepDiary").AddUserTokenForwarding();
```

| Element | Co robi |
|---|---|
| `AddDownstreamApi<T>(configuration, name)` | Refit przez `IHttpClientFactory`, adres z `Downstream:{name}:BaseAddress` (w klastrze adres Service, z IDE `http://localhost:5101`/`5102`), standardowa odporność z ponowieniami **tylko** metod bezpiecznych (GET, HEAD, OPTIONS), daty ISO 8601 w ścieżce i query, rejestracja `DownstreamUnavailableExceptionHandler` |
| `AddUserTokenForwarding()` | `UserTokenForwardingHandler` przepisuje do wywołania token z nagłówka `Authorization` bieżącego żądania, bez zmian (ADR-0040); źródłem tokenu jest `IDownstreamTokenProvider`, furtka na przyszły token exchange |

Akcja fasady to jedna linia (`Controllers/Knowledge/KnowledgeCategoriesController.cs`):

```csharp
[HttpPost("v1/knowledge/categories")]
public async Task<IActionResult> Create([FromBody] CreateCategory body, CancellationToken cancellationToken) =>
    this.ToActionResult(await knowledge.CategoriesCreateAsync(body, cancellationToken));
```

`ToActionResult` (`SuperApp.Framework.Infrastructure/Api/DownstreamResponseExtensions.cs`) przekazuje odpowiedź serwisu **bez zmian**:
sukces z jego statusem i ciałem, błąd z jego statusem i ciałem `application/problem+json` (z `code` i `traceId`). Moduł dostaje więc
np. `409 knowledge.category.slug_taken` dokładnie tak, jak zwrócił go serwis. BFF nie waliduje treści żądań
(`ModelValidatorProviders.Clear()`): walidacja i kody błędów należą do serwisu; wejście niemożliwe do powiązania (np. `?type=Bogus`) BFF odrzuca sam (`400`). Gdy serwis jest nieosiągalny albo nie odpowiada
w czasie, `DownstreamUnavailableExceptionHandler` odpowiada `503 downstream.unavailable` albo `504 downstream.timeout` (log 220).

Poza fasadą BFF ma endpointy, których nie ma żaden serwis:

- `GET /v1/me/summary` (`Controllers/Experience/ExperienceSummaryController.cs`) woła równolegle Knowledge (ulubione) i SleepDiary
  (ostatnie 7 dni). `SuperApp.Framework.Infrastructure/Http/Downstream/PartialResponseFetcher.cs` ogranicza każdą część odpowiedzi do 2 s i zamienia każdą awarię na status części odpowiedzi (`Ok`,
  `Forbidden`, `Unavailable`, `Timeout`), więc odpowiedź to zawsze `200`, a moduł pokazuje te części, które dostał (częściowe
  renderowanie);
- `GET /internal/v1/widgets/sleep-summary` (`Controllers/Internal/InternalWidgetsController.cs`): API wewnętrzne dla BFF-ów innych
  experience, chronione polityką `[Authorize(Policy = ExampleBffScopes.InternalRead)]` (scope `example.internal.read`,
  `ScopePolicyExtensions.RequireScope`).

Dwa kontrakty BFF (`openapi/Example.Bff_public.json` i `openapi/Example.Bff_internal.json`) powstają przy buildzie z podziałem po
ścieżce (`Hosting/BffOpenApiDocuments.cs`); szczegóły w [API i kontrakty](08-api-i-kontrakty.md).

### 3.7 `gateway-mobile`: ta sama ścieżka bez sesji

Profil mobilny nie ma kroków 1 i 4 (CSRF, sesja). Zamiast ciasteczka jest `AddJwtBearer` z sekcji `Authentication`
(`Audience=gateway-mobile`), a transform nie podmienia nagłówka `Authorization`: serwis dostaje **ten sam** token, który
wysłała aplikacja (ADR-0012). Token musi więc mieć w `aud` `gateway-mobile`, `example-bff` (client scope `example-bff-audience`)
i `knowledge-api`; w realmie dodają je mapery scope'ów.

### 3.8 Serwis: walidacja JWT

`AddAppApi` (`HostingExtensions.cs`):

```csharp
public static IHostApplicationBuilder AddAppApi(this IHostApplicationBuilder builder)
{
    builder.Services
        .AddControllers()
        .AddJsonOptions(options => ConfigureJson(options.JsonSerializerOptions));

    // The OpenAPI generator reads the Minimal API JSON options; they must match the MVC options exactly.
    builder.Services.ConfigureHttpJsonOptions(options => ConfigureJson(options.SerializerOptions));

    // AddOpenApi is called by the Api project itself (see ConfigureOpenApi): the XML comment generator
    // only handles calls made in the project whose documentation it describes (ADR-0033).

    builder.Services.AddHttpContextAccessor();
    builder.Services.TryAddScoped<ICurrentUser, HttpCurrentUser>();

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            builder.Configuration.GetSection("Authentication").Bind(options);
            options.MapInboundClaims = false;
            options.TokenValidationParameters.ValidateAudience = true;
            options.TokenValidationParameters.ValidateIssuer = true;
            options.TokenValidationParameters.ClockSkew = TokenClockSkew;
        });

    builder.Services.AddAuthorizationBuilder()
        .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

    return builder;
}
```

- `Authentication:Authority` i `Authentication:Audience` (`knowledge-api` w `appsettings.json` serwisu) decydują o walidacji.
  Klucze podpisu serwis pobiera sam z JWKS CIAM: nie ufa bramie (zero trust, ADR-0007).
- `MapInboundClaims = false`: claimy mają nazwy z tokenu (`sub`, `scope`), a nie długie nazwy URI z WS-Federation;
  `HttpCurrentUser` czyta właśnie `sub` oraz `scope`/`scp`.
- Polityka fallback sprawia, że **każdy** endpoint wymaga uwierzytelnienia, chyba że ma `[AllowAnonymous]`. Wyjątki są jawne:
  `MapOpenApi().AllowAnonymous()` i endpointy health.

### 3.9 Kontroler

`src/Services/Knowledge/Knowledge.Api/Controllers/CategoriesController.cs` (fragment):

```csharp
[ApiController]
[Route("v1/categories")]
[ProducesErrorResponseType(typeof(void))]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, MediaTypeNames.Application.ProblemJson)]
public sealed class CategoriesController(ISender sender) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created, MediaTypeNames.Application.Json)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    public async Task<IActionResult> Create(CreateCategory command, CancellationToken cancellationToken) =>
        this.ToActionResult(await sender.Send(command, cancellationToken), id => StatusCode(StatusCodes.Status201Created, new CreatedResponse(id)));
}
```

Kontroler nie ma logiki: deserializacja ciała do komendy, `ISender.Send`, `ToActionResult`. Model binding `[ApiController]`
działa **przed** pipeline'em: niepoprawny JSON (np. liczba zamiast tekstu) kończy się automatycznym `400` ASP.NET Core bez
pola `code` (przykład w [Rozwiązywaniu problemów](17-rozwiazywanie-problemow.md)).

### 3.10 Pipeline MediatR

Rejestracja (`src/Framework/SuperApp.Framework.Application/ApplicationServiceCollectionExtensions.cs`):

```csharp
services.AddMediatR(configuration =>
{
    configuration.RegisterServicesFromAssemblies(assemblies);
    configuration.AddOpenBehavior(typeof(LoggingBehavior<,>));
    configuration.AddOpenBehavior(typeof(AuthorizationBehavior<,>));
    configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
    configuration.AddOpenBehavior(typeof(TransactionBehavior<,>));
});
```

MediatR wywołuje behaviors w kolejności rejestracji, pierwszy jest najbardziej zewnętrzny. Kolejność pilnuje test architektury
`Pipeline_behaviors_are_registered_in_order` (ADR-0017).

```mermaid
flowchart LR
    S[ISender.Send] --> L[LoggingBehavior<br/>span + log 100/101]
    L --> AZ[AuthorizationBehavior<br/>RequiresScope]
    AZ -- "brak scope" --> F1[Result: auth.missing_scope]
    AZ --> V[ValidationBehavior<br/>FluentValidation]
    V -- "błędy pól" --> F2[Result: validation.failed]
    V --> T{"TransactionBehavior<br/>tylko ICommand"}
    T --> H[Handler]
    H --> T
    T -- "sukces" --> SV[SaveChangesAsync + COMMIT]
```

**1. `LoggingBehavior`** (`Behaviors/LoggingBehavior{TRequest,TResponse}.cs`): span o nazwie typu żądania i log z czasem.
Nie loguje treści żądania (dane osobowe). Wyjątków nie łapie.

```csharp
public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
{
    var requestName = typeof(TRequest).Name;
    using var activity = ApplicationTelemetry.ActivitySource.StartActivity(requestName);
    var started = Stopwatch.GetTimestamp();

    var response = await next();

    var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    if (response is Result { IsFailure: true } failure)
    {
        activity?.SetStatus(ActivityStatusCode.Error, failure.Error.Code);
        LogRequestFailed(logger, requestName, failure.Error.Code, elapsed);
    }
    else
    {
        LogRequestHandled(logger, requestName, elapsed);
    }

    return response;
}
```

**2. `AuthorizationBehavior`**: atrybuty `[AllowAnonymousRequest]` / `[RequiresScope]` czytane raz na typ i cache'owane.

```csharp
public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
{
    var (anonymous, scope) = Requirements.GetOrAdd(typeof(TRequest), static type => (
        type.GetCustomAttribute<AllowAnonymousRequestAttribute>() is not null,
        type.GetCustomAttribute<RequiresScopeAttribute>()?.Scope));

    if (anonymous)
    {
        return next();
    }

    if (!currentUser.IsAuthenticated)
    {
        return Task.FromResult(TResponse.FromError(AuthorizationErrors.Unauthenticated));
    }

    if (scope is not null && !currentUser.HasScope(scope))
    {
        return Task.FromResult(TResponse.FromError(AuthorizationErrors.MissingScope(scope)));
    }

    return next();
}
```

Autoryzacja jest **przed** walidacją, żeby klient bez uprawnień nie mógł „sondować” reguł walidacji (ADR-0017).
`TResponse.FromError` działa dzięki ograniczeniu `TResponse : IResultFactory<TResponse>` (statyczna metoda abstrakcyjna
interfejsu): behavior potrafi zbudować błędny `Result` lub `Result<T>` bez refleksji.

**3. `ValidationBehavior`**: wszystkie walidatory żądania naraz, błędy pogrupowane po nazwie właściwości:

```csharp
public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
{
    var failures = new List<FluentValidation.Results.ValidationFailure>();
    foreach (var validator in validators)
    {
        var result = await validator.ValidateAsync(request, cancellationToken);
        failures.AddRange(result.Errors);
    }

    if (failures.Count == 0)
    {
        return await next();
    }

    var details = failures
        .GroupBy(failure => failure.PropertyName)
        .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).ToArray());

    return TResponse.FromError(Error.Validation("validation.failed", "Żądanie zawiera niepoprawne dane.", details));
}
```

**4. `TransactionBehavior`** (`Behaviors/TransactionBehavior{TRequest,TResponse}.cs`):

```csharp
internal sealed class TransactionBehavior<TRequest, TResponse>(IUnitOfWork unitOfWork)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommand<TResponse>
    where TResponse : IResultFactory<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (unitOfWork.HasActiveTransaction)
        {
            var nested = await next();
            if (!IsSuccess(nested))
            {
                unitOfWork.DiscardChanges();
                return nested;
            }

            var savedNested = await unitOfWork.SaveChangesAsync(cancellationToken);
            return savedNested.IsSuccess ? nested : TResponse.FromError(savedNested.Error);
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var response = await next();
        if (!IsSuccess(response))
        {
            return response;
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return TResponse.FromError(saved.Error);
        }

        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    private static bool IsSuccess(TResponse response) => response is Result { IsSuccess: true };
}
```

- Ograniczenie `where TRequest : ICommand<TResponse>` sprawia, że kontener DI **pomija** ten behavior dla zapytań
  (`IQuery<T>`): zapytania nie otwierają transakcji.
- Gałąź `HasActiveTransaction` służy konsumentom w Workerze: tam transakcję otworzył już outbox/inbox MassTransit, więc
  behavior tylko zapisuje, a commit robi MassTransit (rozdział 5).
- Błędny wynik handlera albo wyjątek: `await using` zamyka transakcję bez commitu, czyli rollback. Nic nie zostaje zapisane.

### 3.11 Handler i agregat

`src/Services/Knowledge/Knowledge.Application/Features/Categories/CreateCategory/CreateCategoryHandler.cs`:

```csharp
internal sealed class CreateCategoryHandler(ICategoryRepository categories) : ICommandHandler<CreateCategory, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CreateCategory command, CancellationToken cancellationToken)
    {
        if (await categories.SlugExistsAsync(command.Slug, cancellationToken))
        {
            return CategoryErrors.SlugTaken;
        }

        if (!Category.Create(command.Name, command.Slug).TryGetValue(out var category, out var error))
        {
            return error;
        }

        categories.Add(category);
        return category.Id.Value;
    }
}
```

Agregat (`src/Services/Knowledge/Knowledge.Domain/Categories/Category.cs`, fragment):

```csharp
public static Result<Category> Create(string name, string slug)
{
    var validName = Text.Required(name, MaxNameLength, "knowledge.category.invalid_name", "name");
    if (validName.IsFailure)
    {
        return validName.Error;
    }

    if (!IsValidSlug(slug))
    {
        return CategoryErrors.InvalidSlug;
    }

    var category = new Category(CategoryId.New()) { Name = validName.Value, Slug = slug };
    category.Raise(new CategoryChanged(category.Id));
    return category;
}
```

`categories.Add(category)` tylko dodaje encję do `ChangeTracker`. `Raise` dopisuje zdarzenie do listy w agregacie; nic się
jeszcze nie wykonało.

### 3.12 Zapis: `WriteDbContextBase.SaveChangesAsync`

`src/Framework/SuperApp.Framework.Infrastructure/Persistence/WriteDbContextBase.cs`, dwie metody, które wykonują całą pracę:

```csharp
async Task<Result> IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken)
{
    try
    {
        await SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
    catch (DbUpdateConcurrencyException) when (_ownTransaction is not null)
    {
        DiscardChanges();
        return ConcurrencyConflict;     // persistence.concurrency_conflict, 409
    }
    catch (DbUpdateException exception) when (UniqueConstraintViolation.TryGetName(exception, out var name))
    {
        DiscardChanges();
        return UniqueConstraintErrors.TryGetValue(name, out var error)
            ? error
            : Error.Conflict("persistence.duplicate", "Zasób o podanych danych już istnieje.");
    }
}

public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
{
    var aggregates = ChangeTracker.Entries<IAggregateRoot>()
        .Select(entry => entry.Entity)
        .Where(aggregate => aggregate.DomainEvents.Count > 0)
        .ToList();

    var domainEvents = aggregates.SelectMany(aggregate => aggregate.DomainEvents).ToList();
    aggregates.ForEach(aggregate => aggregate.ClearDomainEvents());

    foreach (var domainEvent in domainEvents)
    {
        await dispatcher.DispatchAsync(domainEvent, cancellationToken);
    }

    return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
}
```

Kolejność w czasie dla `CreateCategory`:

| # | Kod | Baza | Transakcja |
|---|---|---|---|
| 1 | `TransactionBehavior`: `BeginTransactionAsync` | `BEGIN TRANSACTION` | otwarta |
| 2 | handler: `SlugExistsAsync` | `SELECT` | w transakcji |
| 3 | zebranie i wyczyszczenie zdarzeń agregatów | | |
| 4 | `DomainEventDispatcher` → `CategoryCacheInvalidation.HandleAsync` → `unitOfWork.OnCommitted(...)` | nic | tylko rejestracja akcji |
| 5 | translatory (przy innych komendach, np. `MaterialArchivedTranslator`) → `IIntegrationEventPublisher.PublishAsync` | nowe encje `OutboxMessage`/`OutboxState` w `ChangeTracker` | |
| 6 | `base.SaveChangesAsync` | `INSERT Categories` (+ wiersze outboxa) | w transakcji |
| 7 | `transaction.CommitAsync` | `COMMIT` | zatwierdzona |
| 8 | `AfterCommitInterceptor.TransactionCommittedAsync` | `RemoveByTagAsync("knowledge:categories")` w Redis i pamięci | po commicie, przed odpowiedzią HTTP |

Dispatch jest jednoprzebiegowy: zdarzenia podniesione przez handlery zdarzeń nie są obsługiwane w tym samym zapisie. Handler
zdarzenia domenowego nie modyfikuje innych agregatów i nie wywołuje zapisu (ADR-0027); może tylko opublikować zdarzenie
integracyjne albo zarejestrować akcję po commicie.

**Wyścig o unikalny indeks.** Dwa równoległe `POST` z tym samym slugiem przejdą `SlugExistsAsync`; drugi `INSERT` łamie
`IX_Categories_Slug`. `UniqueConstraintViolation` rozpoznaje błąd SQL 2601/2627, wyciąga nazwę indeksu, a mapa
`UniqueConstraintErrors` kontekstu zamienia ją na ten sam błąd, który handler zwraca bez wyścigu
(`src/Services/Knowledge/Knowledge.Infrastructure/Persistence/Write/KnowledgeWriteDbContext.cs`):

```csharp
protected override IReadOnlyDictionary<string, Error> UniqueConstraintErrors { get; } = new Dictionary<string, Error>
{
    ["IX_Categories_Slug"] = CategoryErrors.SlugTaken,
    ["IX_Favorites_UserId_ItemType_ItemId"] = LibraryErrors.FavoriteAddedConcurrently,
    ["IX_MaterialCompletions_UserId_MaterialId"] = LibraryErrors.CompletionRecordedConcurrently,
};
```

`DiscardChanges` odłącza niezapisane encje i czyści akcje po commicie, a `TransactionBehavior` zwraca błąd bez commitu.
Klient dostaje `409 knowledge.category.slug_taken`, a nie `500`. Tak samo konflikt `rowversion` (ktoś zmienił agregat między
odczytem a zapisem) daje w API `409 persistence.concurrency_conflict`; w konsumencie ten sam konflikt zostaje wyjątkiem, żeby
MassTransit ponowił wiadomość na świeżych danych.

**Akcje po commicie** (`src/Framework/SuperApp.Framework.Infrastructure/Persistence/AfterCommitInterceptor.cs`, fragment):

```csharp
public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
{
    if (eventData.Context is WriteDbContextBase context)
    {
        // The commit has happened: run the actions even if the request is being cancelled right now.
        await RunAsync(context, CancellationToken.None);
    }
}

public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
{
    Discard(eventData.Context);
    return Task.CompletedTask;
}
```

Interceptor obserwuje **każdą** transakcję kontekstu, niezależnie od tego, kto ją otworzył (behavior w API czy MassTransit
w Workerze). Wyjątek akcji jest logowany (EventId 210) i połykany: komenda jest już zatwierdzona.

### 3.13 Odpowiedź: `Result` → HTTP

`src/Framework/SuperApp.Framework.Infrastructure/Api/ResultHttpExtensions.cs`:

```csharp
public static IActionResult ToActionResult(this ControllerBase controller, Result result) =>
    result.IsSuccess ? controller.NoContent() : Problem(controller, result.Error);

public static IActionResult ToActionResult<T>(this ControllerBase controller, Result<T> result, Func<T, IActionResult>? onSuccess = null) =>
    result.TryGetValue(out var value, out var error) ? onSuccess?.Invoke(value) ?? controller.Ok(value) : Problem(controller, error);

public static int ToStatusCode(this ErrorType type) => type switch
{
    ErrorType.Validation => StatusCodes.Status400BadRequest,
    ErrorType.Forbidden => StatusCodes.Status403Forbidden,
    ErrorType.NotFound => StatusCodes.Status404NotFound,
    ErrorType.Conflict => StatusCodes.Status409Conflict,
    ErrorType.BusinessRule => StatusCodes.Status422UnprocessableEntity,
    _ => StatusCodes.Status500InternalServerError,
};

private static ObjectResult Problem(ControllerBase controller, Error error)
{
    var status = error.Type.ToStatusCode();
    ProblemDetails problem = error.Details is { Count: > 0 } details
        ? new ValidationProblemDetails(details.ToDictionary(pair => pair.Key, pair => pair.Value))
        : new ProblemDetails();

    problem.Status = status;
    problem.Title = error.Message;
    problem.Instance = controller.HttpContext.Request.Path;
    problem.Extensions["code"] = error.Code;
    problem.Extensions["traceId"] = ProblemDetailsConventions.TraceId(controller.HttpContext);

    return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
}
```

Odpowiedzi `knowledge-api` (`400`, `409`, `403` zarejestrowane w lokalnym środowisku bezpośrednio na porcie 5101 z tokenem `dev-cli`; `201` według kontraktu `openapi/Knowledge.Api.json`):

```http
HTTP/1.1 201 Created
Content-Type: application/json; charset=utf-8

{"id":"01a0f63c-b6c2-7f96-a905-05a4d1fde790"}
```

```http
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json; charset=utf-8

{"title":"Żądanie zawiera niepoprawne dane.","status":400,"instance":"/v1/categories",
 "errors":{"Name":["'Name' must not be empty."],"Slug":["'Slug' must not be empty."]},
 "code":"validation.failed","traceId":"19b39bc67df137ec111c2b3062874a88"}
```

```http
HTTP/1.1 409 Conflict
Content-Type: application/problem+json; charset=utf-8

{"title":"Kategoria o tym slugu już istnieje.","status":409,"instance":"/v1/categories",
 "code":"knowledge.category.slug_taken","traceId":"f2a632e22bd772cbcc21464bbbd35670"}
```

```http
HTTP/1.1 403 Forbidden
Content-Type: application/problem+json; charset=utf-8

{"title":"Brak wymaganego uprawnienia 'knowledge.catalog.write'.","status":403,"instance":"/v1/categories",
 "code":"auth.missing_scope","traceId":"4e15096ae8e8f528be59fe4ec772a394"}
```

Klucze w `errors` to nazwy właściwości komendy (`Name`, `Slug`), tak jak podaje FluentValidation. Klient rozgałęzia się po
`code`, nigdy po `title` (komunikat może się zmienić).

### 3.14 Gdy leci wyjątek

Wyjątek techniczny (baza niedostępna, błąd w kodzie; w konsumencie także `DbUpdateConcurrencyException`) przechodzi przez behaviors bez obsługi,
transakcja jest wycofywana przez `await using`, a `UseExceptionHandler` z `AddProblemDetails` zwraca `500`
`application/problem+json` z kodem `server.error` i `traceId` w tym samym formacie co w błędach z `Result` (32 znaki hex;
konwencja `ProblemDetailsConventions`, ADR-0044). Wpis w logu kategorii `Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware`
zawiera stos wywołań. Błędy biznesowe nigdy nie powinny tędy przechodzić (ADR-0015).

---

## 4. Życie zapytania

```mermaid
sequenceDiagram
    autonumber
    participant A as Kontroler
    participant P as Pipeline
    participant Q as ListCategoriesHandler (Infrastructure)
    participant FC as FailSafeCache
    participant HC as HybridCache L1 + Redis L2
    participant R as KnowledgeReadDbContext

    A->>P: Send(ListCategories)
    P->>P: Logging → Authorization (knowledge.catalog.read) → Validation
    Note over P: TransactionBehavior pominięty (IQuery)
    P->>Q: Handle
    Q->>FC: GetOrCreateAsync("knowledge:categories:v1", fabryka, opcje, tag)
    FC->>HC: GetOrCreateAsync(koperta z FreshUntil)
    alt brak wpisu (miss)
        HC->>R: fabryka: SELECT ... ORDER BY Name (projekcja na DTO)
        R-->>HC: lista
    end
    HC-->>FC: CacheEnvelope
    alt FreshUntil w przyszłości
        FC-->>Q: wartość (fresh / miss)
    else przeterminowany (stale)
        FC->>R: odświeżenie (jeden wywołujący na klucz w procesie)
        alt błąd przejściowy
            FC-->>Q: stara wartość (fail-safe, log 300)
        else sukces
            FC->>HC: SetAsync(nowa koperta)
            FC-->>Q: nowa wartość
        end
    end
    Q-->>A: Result<IReadOnlyList<CategoryDto>>
```

Handler zapytania leży w **Infrastructure** (ADR-0026), bo korzysta bezpośrednio z `ReadDbContext`
(`src/Services/Knowledge/Knowledge.Infrastructure/Features/Categories/ListCategoriesHandler.cs`):

```csharp
internal sealed class ListCategoriesHandler(KnowledgeReadDbContext db, FailSafeCache cache)
    : IQueryHandler<ListCategories, Result<IReadOnlyList<CategoryDto>>>
{
    public async Task<Result<IReadOnlyList<CategoryDto>>> Handle(ListCategories query, CancellationToken cancellationToken)
    {
        var categories = await cache.GetOrCreateAsync(
            KnowledgeCache.CategoriesKey,
            async token => (IReadOnlyList<CategoryDto>)await db.Categories
                .OrderBy(category => category.Name)
                .Select(category => new CategoryDto(category.Id, category.Name, category.Slug))
                .ToListAsync(token),
            KnowledgeCache.Categories,
            [KnowledgeCache.CategoriesTag],
            cancellationToken);

        return Result<IReadOnlyList<CategoryDto>>.Success(categories);
    }
}
```

Różnice względem komendy:

| | Komenda | Zapytanie |
|---|---|---|
| Kontekst EF | `KnowledgeWriteDbContext` przez repozytorium | `KnowledgeReadDbContext` bezpośrednio, `NoTracking`, `SaveChanges` rzuca `NotSupportedException` |
| Connection string | `ConnectionStrings:Write` | `ConnectionStrings:Read` (ten sam login; na prod może wskazywać replikę `ApplicationIntent=ReadOnly`) |
| Model | agregaty | płaskie `*Row` (`Persistence/Read/Models`) i DTO z prymitywami |
| Transakcja | `TransactionBehavior` | brak |
| Cache | zakazany | `FailSafeCache` tam, gdzie się opłaca |

Cache nie zawsze jest używany: `GetMaterialHandler` dla redaktora (scope `knowledge.catalog.write`) czyta z bazy z pominięciem
cache (redaktor musi widzieć szkice i swoje zmiany od razu), a dla czytelnika korzysta z cache opublikowanej wersji. Szczegóły
kluczy, tagów i czasów: [Cache](11-cache.md).

---

## 5. Życie zdarzenia integracyjnego

Przykład: archiwizacja materiału. Komenda `ArchiveMaterial` zmienia tylko agregat `Material`; usunięcie materiału z ulubionych
wszystkich użytkowników dzieje się asynchronicznie, przez zdarzenie integracyjne konsumowane przez Workera tego samego serwisu.

```mermaid
sequenceDiagram
    autonumber
    participant API as knowledge-api
    participant DB as MSSQL knowledge
    participant W as knowledge-worker
    participant MQ as RabbitMQ
    participant C as MaterialArchivedConsumer

    API->>API: ArchiveMaterialHandler → material.Archive(now) → Raise(MaterialArchived)
    API->>API: SaveChangesAsync: MaterialArchivedTranslator → PublishAsync(MaterialArchivedV1)
    API->>DB: UPDATE Materials + INSERT OutboxMessage/OutboxState (jedna transakcja), COMMIT
    API-->>API: 204 No Content (wiadomość jeszcze nie wysłana)
    loop delivery service (tylko Worker)
        W->>DB: odczyt OutboxState/OutboxMessage
        W->>MQ: publish do exchange Knowledge.Contracts:MaterialArchivedV1
        W->>DB: usunięcie dostarczonych wierszy
    end
    MQ->>W: kolejka knowledge-material-archived
    W->>DB: BEGIN, InboxState (MessageId, ConsumerId): duplikat?
    alt nowa wiadomość
        W->>C: Consume
        C->>C: ISender.Send(RemoveFavoritesOfItem) jako SystemCurrentUser
        Note over C: TransactionBehavior: HasActiveTransaction → tylko SaveChanges
        C->>DB: DELETE Favorites WHERE ItemType/ItemId
        W->>DB: zapis InboxState, COMMIT
    else duplikat
        W->>MQ: ACK bez wywołania konsumenta
    end
```

### 5.1 Publikacja: translator zdarzenia domenowego

`src/Services/Knowledge/Knowledge.Application/IntegrationEvents/MaterialArchivedTranslator.cs`:

```csharp
internal sealed class MaterialArchivedTranslator(IIntegrationEventPublisher publisher) : IDomainEventHandler<MaterialArchived>
{
    public Task HandleAsync(MaterialArchived domainEvent, CancellationToken cancellationToken) =>
        publisher.PublishAsync(new MaterialArchivedV1(domainEvent.MaterialId.Value, domainEvent.ArchivedAt), cancellationToken);
}
```

Application zna tylko port `IIntegrationEventPublisher`. Implementacja (w `MessagingServiceCollectionExtensions`) to
`IPublishEndpoint.Publish` MassTransit; dzięki `UseBusOutbox` wywołanie nie idzie do brokera, tylko dodaje wiersz
`OutboxMessage` do tego samego `KnowledgeWriteDbContext`. Wiadomość istnieje wtedy i tylko wtedy, gdy zmiana stanu została
zatwierdzona. Czas w zdarzeniu (`ArchivedAt`) pochodzi z agregatu, nie z zegara translatora.

### 5.2 Konfiguracja MassTransit

`src/Framework/SuperApp.Framework.Infrastructure/Messaging/MessagingServiceCollectionExtensions.cs` (fragment `AddAppMessaging`):

```csharp
services.AddMassTransit(bus =>
{
    bus.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter(servicePrefix, includeNamespace: false));
    configureConsumers?.Invoke(bus);

    bus.AddEntityFrameworkOutbox<TWriteDbContext>(outbox =>
    {
        outbox.UseSqlServer();
        outbox.UseBusOutbox(busOutbox =>
        {
            if (outboxDelivery == OutboxDelivery.Disabled)
            {
                busOutbox.DisableDeliveryService();
            }
        });
    });

    bus.AddConfigureEndpointsCallback((context, _, endpoint) =>
    {
        endpoint.UseMessageRetry(retry => retry.Intervals(100, 500, 1000, 5000));
        endpoint.UseEntityFrameworkOutbox<TWriteDbContext>(context);

        if (endpoint is IRabbitMqReceiveEndpointConfigurator rabbitMq)
        {
            rabbitMq.SetQuorumQueue();
        }
    });

    bus.UsingRabbitMq((context, rabbitMq) =>
    {
        var connectionString = configuration.GetConnectionString("RabbitMq");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Brak connection stringu RabbitMq.");
        }

        rabbitMq.Host(new Uri(connectionString));
        rabbitMq.ConfigureEndpoints(context);
    });
});
```

| Ustawienie | Skutek |
|---|---|
| `DisableDeliveryService()` w API | API tylko zapisuje outbox. Wysyła Worker, więc skalowanie API nie mnoży pracy, a czas odpowiedzi nie zależy od brokera. **Gdy Worker nie działa, wiadomości czekają w `OutboxMessage`.** |
| `UseEntityFrameworkOutbox` na endpointach | inbox (deduplikacja po `MessageId` + konsument) i outbox konsumenta w jednej transakcji z jego komendą |
| `UseMessageRetry(100, 500, 1000, 5000)` | 4 ponowienia po wyjątku; potem wiadomość do kolejki `{endpoint}_error` |
| `SetQuorumQueue()` | kolejki quorum (wymóg działu infrastruktury) |
| `BuildTimeDocumentGeneration.IsActive` (wcześniej w metodzie) | przy generowaniu OpenAPI w buildzie MassTransit nie jest rejestrowany, więc build nie potrzebuje brokera |

### 5.3 Konsument

`src/Services/Knowledge/Knowledge.Worker/Consumers/MaterialArchivedConsumer.cs`:

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

| Wynik konsumenta | Co się dzieje z wiadomością |
|---|---|
| sukces | commit (zmiany + `InboxState` + ewentualny outbox konsumenta), ACK |
| `Result` z błędem | log ostrzeżenia, ACK; ponawianie nic by nie zmieniło |
| wyjątek | retry 100 ms / 500 ms / 1 s / 5 s, potem `knowledge-material-archived_error` |
| ta sama wiadomość drugi raz (redelivery) | inbox wykrywa `MessageId`, konsument nie jest wywoływany |

Komenda wysłana przez konsumenta przechodzi **pełny pipeline**. `SystemCurrentUser` ma każdy scope i `IsAuthenticated = true`,
ale `Subject = null`, więc komendy wysyłane z Workera nie mogą zależeć od zalogowanego użytkownika. Szczegóły kontraktów,
wersjonowania i idempotencji: [Zdarzenia i integracja](10-zdarzenia-i-integracja.md).

---

## 6. Start procesu i sondy

```mermaid
sequenceDiagram
    autonumber
    participant K as Kubernetes
    participant P as Pod (API / Worker)
    participant DB as MSSQL

    K->>P: start kontenera
    loop startupProbe co 5 s (do 60 prób)
        K->>P: GET /health/startup
        P->>DB: GetPendingMigrationsAsync (__EFMigrationsHistory w schemacie serwisu)
        alt brak oczekujących migracji
            P-->>K: 200 Healthy
        else są oczekujące albo baza niedostępna
            P-->>K: 503 Unhealthy
        end
    end
    loop readinessProbe co 5 s
        K->>P: GET /health/ready (tylko: czy nie trwa zamykanie)
    end
    loop livenessProbe co 10 s
        K->>P: GET /health/live (bez sprawdzeń)
    end
```

Mapowanie endpointów (`HostingExtensions.MapAppDefaultEndpoints`):

```csharp
public static WebApplication MapAppDefaultEndpoints(this WebApplication app)
{
    app.MapHealthChecks("/health/startup", Probe(HealthTags.Startup)).AllowAnonymous();
    app.MapHealthChecks("/health/ready", Probe(HealthTags.Ready)).AllowAnonymous();
    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
    app.MapHealthChecks("/health/dependencies", new HealthCheckOptions
    {
        Predicate = registration => registration.Tags.Contains(HealthTags.Dependencies) || registration.Tags.Contains(HealthTags.MassTransit),
    }).AllowAnonymous();

    return app;
}
```

| Endpoint | Checki | Sonda (chart `deploy/helm/superapp-service`, `superapp.probes`) | Dlaczego tak |
|---|---|---|---|
| `/health/startup` | `migrations` (`MigrationsAppliedHealthCheck<TWrite>`); w bramie: `migrations` dla `GatewayDbContext` oraz `proxy-config` (konfiguracja tras załadowana z bazy lub z L2) | `startupProbe`, co 5 s, `failureThreshold: 60` | nowy kod nigdy nie obsługuje ruchu na starym schemacie; rollout czeka na Migratora / DBA |
| `/health/ready` | `shutdown` (po SIGTERM `Unhealthy`) | `readinessProbe` | awaria wspólnej bazy nie może wyjąć z ruchu wszystkich podów naraz (ADR-0018) |
| `/health/live` | żadne | `livenessProbe` | restart tylko, gdy proces nie odpowiada |
| `/health/dependencies` | `mssql`, `redis` (jeśli skonfigurowany), checki MassTransit | żadna; monitoring i alerty | widać awarię zależności bez zabijania podów |

Sprawdzenie migracji (`HealthChecks/MigrationsAppliedHealthCheck{TContext}.cs`):

```csharp
public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext healthCheckContext, CancellationToken cancellationToken = default)
{
    var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
    return pending.Count == 0
        ? HealthCheckResult.Healthy()
        : HealthCheckResult.Unhealthy($"Oczekujące migracje: {string.Join(", ", pending)}");
}
```

Endpointy zwracają tylko status tekstowy (`Healthy` / `Unhealthy`); opis z listą migracji nie trafia do odpowiedzi.
Graceful shutdown: chart ustawia `preStop: sleep 5 s` i `terminationGracePeriodSeconds: 30`, a `ShutdownHealthCheck`
od SIGTERM odpowiada „not ready”, żeby Service przestał kierować ruch, zanim proces się zamknie.

**Migracje wykonuje tylko `SuperApp.Migrator`** (`src/Migrator/SuperApp.Migrator/Program.cs`): po kolei `GatewayDbContext`,
`KnowledgeWriteDbContext`, `SleepDiaryWriteDbContext`, każdy `MigrateAsync`; pierwszy błąd kończy proces kodem 1 i zatrzymuje
rollout. Na dev/test to Job przed rolloutem, na prod skrypt `--idempotent` uruchamiany przez DBA (ADR-0004).

---

## 7. Gdzie szukać kodu

| Chcę zrozumieć | Plik |
|---|---|
| kolejność middleware i konfigurację OIDC bramy | `src/Gateway/SuperApp.Gateway/Program.cs` |
| sesję BFF i blokady | `src/Gateway/SuperApp.Gateway/Bff/Sessions/DbTicketStore.cs` |
| odświeżanie tokenu | `src/Gateway/SuperApp.Gateway/Bff/Tokens/TokenRefresher.cs` |
| `/bff/login`, `/bff/logout`, `/bff/user`, back-channel logout | `src/Gateway/SuperApp.Gateway/Bff/BffEndpoints.cs` |
| trasy z bazy | `src/Gateway/SuperApp.Gateway/Proxy/Configuration/DatabaseProxyConfigProvider.cs`, `Persistence/Seed/ProxyConfigurationSeed.cs` |
| BFF experience: rejestracja klientów, dwa kontrakty | `src/Bff/Example.Bff/Program.cs`, `Hosting/BffOpenApiDocuments.cs` |
| fasada i endpointy komponowane BFF | `src/Bff/Example.Bff/Controllers/*`, `SuperApp.Framework.Infrastructure/Http/Downstream/PartialResponseFetcher.cs` |
| klient serwisu, przekazanie tokenu, 503/504 | `src/Framework/SuperApp.Framework.Infrastructure/Http/Downstream/*`, `Http/UserContext/*`, `Api/DownstreamResponseExtensions.cs`, `Api/DownstreamUnavailableExceptionHandler.cs` |
| wspólny host serwisu, JWT, health | `src/Framework/SuperApp.Framework.Infrastructure/Hosting/HostingExtensions.cs` |
| pipeline | `src/Framework/SuperApp.Framework.Application/Behaviors/*.cs` |
| Unit of Work, zdarzenia domenowe, konflikty | `src/Framework/SuperApp.Framework.Infrastructure/Persistence/WriteDbContextBase.cs` |
| akcje po commicie | `src/Framework/SuperApp.Framework.Infrastructure/Persistence/AfterCommitInterceptor.cs` |
| mapowanie błędów na HTTP | `src/Framework/SuperApp.Framework.Infrastructure/Api/ResultHttpExtensions.cs` |
| outbox, inbox, retry | `src/Framework/SuperApp.Framework.Infrastructure/Messaging/MessagingServiceCollectionExtensions.cs` |
| cache | `src/Framework/SuperApp.Framework.Infrastructure/Caching/FailSafeCache.cs` |
| composition root serwisu | `src/Services/{Serwis}/{Serwis}.Infrastructure/InfrastructureServiceCollectionExtensions.cs` |
| analityka, pseudonim, feature flags | `src/Framework/SuperApp.Framework.Infrastructure/Analytics/*.cs`, `src/Gateway/SuperApp.Gateway/Analytics/AnalyticsEndpoints.cs` |
| forwarder zdarzeń do PostHog | `src/Analytics/SuperApp.AnalyticsForwarder/Program.cs`, `Consumers/`, `Events/` |

---

## Typowe błędy

| Symptom | Przyczyna | Naprawa |
|---|---|---|
| Handler wywołuje `SaveChangesAsync`, zdarzenia „działają podwójnie” albo zapis jest częściowy | zapis poza `TransactionBehavior` | usuń zapis z handlera; kończ na `Add`/metodzie agregatu |
| Cache pokazuje stare dane po zmianie | unieważnienie wykonane w handlerze zdarzenia **przed** commitem albo brak handlera unieważniającego | rejestruj `unitOfWork.OnCommitted(...)` w handlerze zdarzenia domenowego (wzór `CategoryCacheInvalidation`) |
| Zdarzenie integracyjne nie dociera do konsumenta, a API zwraca sukces | Worker nie działa (API ma `OutboxDelivery.Disabled`) | uruchom Worker; wiadomości czekają w `OutboxMessage` |
| Komenda z konsumenta kończy się `auth.unauthenticated` lub błędem „brak użytkownika” | komenda zależy od `ICurrentUser.Subject`, a w Workerze jest `null` | przekaż identyfikator w danych zdarzenia/komendy |
| Brama zwraca `401 auth.csrf_header_missing` na `/api/*` mimo zalogowania | brak nagłówka `X-CSRF: 1` | interceptor Angulara musi dodawać nagłówek do każdego `/api/*` |
| `404` z bramy na `/api/knowledge/...` | stara ścieżka: serwisy nie mają tras w bramie | ścieżka modułu to `/api/example/v1/knowledge/...` (przez BFF) |
| BFF odpowiada `503 downstream.unavailable` / `504 downstream.timeout` | serwis nieosiągalny albo za wolny (log 220 w BFF) | sprawdź `Downstream:{Serwis}:BaseAddress` i czy serwis działa |
| Pod nie dostaje ruchu po wdrożeniu | `/health/startup` czeka na migracje | uruchom Migratora (dev/test) lub skrypt DBA (prod) |
| Logika w kontrolerze lub konsumencie | pominięcie pipeline'u: brak autoryzacji, walidacji, transakcji | kontroler i konsument tylko tłumaczą wejście na komendę |

## Do zapamiętania

- Moduł → brama (lokalnie `SuperApp.Gateway`) → BFF experience → serwisy. Brama zna jedną trasę na experience (`/api/{experience}/v{n}/**`),
  serwisy domenowe nie mają tras w bramie, a `/internal` nigdy nie jest routowany.
- BFF przekazuje token użytkownika bez zmian i odpowiedź serwisu bez zmian; własne kody ma tylko dla niedostępności (`503`/`504 downstream.*`).
- Brama: sesja, CSRF, scope „czy w ogóle”, Bearer. Serwis: JWT, scope konkretnej operacji, reguły zasobu, zawsze, niezależnie od wołającego.
- Jeden zakres DI = jeden `WriteDbContext` = jedna transakcja; repozytoria, translatory i behavior pracują na tej samej instancji.
- Pipeline: logowanie → autoryzacja → walidacja → transakcja (tylko komendy).
- Zapis: zdarzenia domenowe → outbox → `INSERT/UPDATE` → `COMMIT` → akcje `OnCommitted`. Konflikt unikalnego indeksu to `409`, nie `500`.
- Błąd biznesowy to `Result`; `ResultHttpExtensions` zamienia go na `problem+json` z `code` i `traceId`.
- API zapisuje outbox, Worker go wysyła i konsumuje wiadomości z inboxem.
- Proces sprawdza migracje, nigdy ich nie wykonuje.

## Powiązane

- Rozdziały: [Warstwa aplikacji](06-warstwa-aplikacji.md), [Dane i EF Core](07-dane-i-ef-core.md), [API i kontrakty](08-api-i-kontrakty.md),
  [Bezpieczeństwo](09-bezpieczenstwo.md), [Zdarzenia i integracja](10-zdarzenia-i-integracja.md), [Cache](11-cache.md),
  [Lokalne środowisko i debugowanie](13-lokalne-srodowisko-i-debugowanie.md), [Logowanie i obserwowalność](14-logowanie-i-obserwowalnosc.md),
  [Rozwiązywanie problemów](17-rozwiazywanie-problemow.md), [Analityka i feature flags](21-analityka-i-feature-flags.md).
- Dokument architektury: [rozdział 6, widok dynamiczny](../architektura.md#6-widok-dynamiczny).
- ADR: [0003](../adr/0003-rozdzielenie-write-i-read-dbcontext.md), [0004](../adr/0004-migracje-przez-migrator-i-job-k8s.md),
  [0005](../adr/0005-komunikacja-asynchroniczna-outbox-inbox.md), [0006](../adr/0006-bramy-yarp-bff-web-i-gateway-mobile.md),
  [0007](../adr/0007-zero-trust-walidacja-jwt-w-serwisach.md), [0011](../adr/0011-wlasny-bff-na-yarp.md), [0012](../adr/0012-audience-tokenow.md),
  [0013](../adr/0013-session-store-i-data-protection.md), [0015](../adr/0015-result.md), [0017](../adr/0017-kolejnosc-pipeline-behaviors.md),
  [0018](../adr/0018-health-checki-readiness.md), [0020](../adr/0020-cache-l1-l2-redis.md), [0021](../adr/0021-schemat-bazy-per-mikroserwis.md),
  [0022](../adr/0022-konfiguracja-yarp-w-bazie.md), [0026](../adr/0026-handlery-zapytan-w-infrastructure.md),
  [0027](../adr/0027-zdarzenia-domenowe-dispatch-w-uow.md), [0035](../adr/0035-mediatr-i-masstransit-w-wersjach-open-source.md),
  [0037](../adr/0037-superapp-gateway-jako-lokalny-zamiennik-wspolnej-bramy.md), [0038](../adr/0038-experience-modul-bff-i-serwisy-domenowe.md),
  [0039](../adr/0039-api-publiczne-i-wewnetrzne-bff.md), [0040](../adr/0040-dostep-do-api-wewnetrznego-i-serwisow-domenowych.md),
  [0041](../adr/0041-networkpolicy-izolacja-experience.md), [0014](../adr/0014-klienci-http-refit-refitter.md).
