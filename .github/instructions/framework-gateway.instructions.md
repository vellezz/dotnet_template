---
applyTo: "src/Framework/**/*.cs,src/Gateway/**/*.cs,src/Migrator/**/*.cs,src/Tools/**/*.cs"
---

# Framework, gateway, migrator and analyzers

- `SuperApp.Framework.*` is technical code shared by all services and must contain no business concepts. Changing a public framework
  API affects every service and the `src/Tools/SuperApp.Cli/Templates/superapp-service` template: update callers, the template and the docs together.
- Framework public types require `<remarks>` in addition to the usual complete XML docs (APP006 with
  `app_documentation_require_remarks`); keep the existing level of detail (purpose, how it works, rules, pitfalls, example).
- `SuperApp.Gateway` is the **local stand-in for the shared edge gateway** of the super app (ADR-0037): it runs in docker compose, E2E
  tests and the IDE, and its behavior is the specification of our requirements towards the shared gateway. It is not a release
  artifact of the experience (chart `superapp-gateway` and the `gateway` schema are local tooling). Change it only to mirror the shared
  gateway or a requirement towards it; never add experience logic (orchestration, data shaping, domain rules), which belongs in the
  BFF experience (ADR-0038). Profile names `bff-web`/`gateway-mobile` are historical: `bff-web` is a gateway profile, not the BFF.
- Routes: one route per experience, `/api/{experience}/v{version:int}/{**rest}` to its BFF's public API
  (`http://{experience}-bff.{experience}.svc.cluster.local:8080`, transform `PathRemovePrefix /api/{experience}`), declared in the
  `Experiences` list of `ProxyConfigurationSeed` with a policy from `GatewayPolicies` (e.g. `GatewayPolicies.Example`). Never add routes
  to domain services or to `internal` paths (ADR-0039); `GatewayDatabaseTests` checks that no route contains `internal`. A new
  experience = constant and registration in `GatewayPolicies`, entry in `Experiences`, new gateway migration
  (`dotnet ef migrations add <Name> -p src/Gateway/SuperApp.Gateway`, like `RouteExperienceThroughBff`).
- The gateway (YARP) does coarse-grained authorization only (scope prefix per service) and never contains domain rules. Route
  configuration lives in MSSQL and changes only through migrations of `GatewayDbContext` (`Persistence/Seed/ProxyConfigurationSeed.cs`);
  destinations must be `http://{service}.{namespace}.svc.cluster.local:{port}`.
- `bff-web` profile: no tokens in the browser, server-side sessions in MSSQL, CSRF header `X-CSRF: 1` on `/api/*`, logout as
  `GET /bff/logout?sid=...`, no `offline_access`. JWT validation (mobile profile and `AddAppApi`) uses `HostingExtensions.TokenClockSkew`
  (30 s); keep it.
- Analytics in the gateway (ADR-0036, `Analytics/AnalyticsEndpoints.cs`):
  - bff-web proxies `/ingest/{**path}` to `Analytics:Host` and `/ingest/static/{**path}` to `Analytics:AssetsHost` with YARP
    `MapForwarder`. The proxy is defined **in code, never as a route in the database**: database routes may only point to cluster
    services and always carry the user's token (ADR-0022, `CK_Destinations_ClusterAddress` stays unchanged).
  - The proxy removes `Cookie`, `Authorization`, `X-User-*`, `X-CSRF`, `X-Forwarded-*` and `Forwarded` from the request and
    `Set-Cookie` from the response, is anonymous with the `per-user` rate limit, and is mapped only when analytics is enabled
    (otherwise the path is unknown and the default policy answers 401). Keep these removals when touching the transforms.
  - `/bff/user` returns `analyticsId`; gateway-mobile exposes `GET /analytics/id` (`{ "analyticsId": "u_..." | null }`, token
    required). Both come from `AnalyticsIdentity.ForSubject(sub)`; never return or forward `sub` to PostHog.
  - Per ADR-0037 the `/ingest` proxy and the analytics ID are requirements towards the shared gateway or, if it does not provide
    them, future BFF endpoints
    (e.g. `GET /v1/analytics/id`); do not build new features on top of them in the gateway.
- HTTP clients in the framework (`SuperApp.Framework.Infrastructure/Http`):
  - `Downstream/AddDownstreamApi<T>(configuration, name)`: Refit client with the address `Downstream:{name}:BaseAddress` (missing or
    relative = startup failure), standard resilience with retries only for safe methods (GET, HEAD, OPTIONS), JSON with string enums and
    `PolymorphicDiscriminatorProperty`, ISO dates in URLs (`DownstreamUrlParameterFormatter`); registers
    `DownstreamUnavailableExceptionHandler` (`503 downstream.unavailable` / `504 downstream.timeout`, EventId 220).
  - `UserContext/AddUserTokenForwarding()`: forwards the incoming user's token unchanged (ADR-0040). `IDownstreamTokenProvider` is the
    seam for token exchange (RFC 8693): switching must change only that implementation and configuration, never BFFs, services or
    contracts.
  - `ClientCredentials/AddClientCredentialsToken(name)`: system calls only. Never log tokens.
- API helpers for BFFs: `Api/DownstreamResponseExtensions.ToActionResult` relays a service answer unchanged (status, problem body with
  `code`); `Security/ScopePolicyExtensions.RequireScope` builds scope policies for hosts without the MediatR pipeline.
- OpenAPI transformers apply to every API: `OpenApiOperationIdTransformer` (`operationId` = `{Controller}_{Action}`; renaming a controller
  or action renames generated client methods), `OpenApiBearerSecurityTransformer` (Bearer scheme, requirement on non-anonymous
  operations), `OpenApiAbstractTypeSchemaTransformer` (`x-abstract: true` for abstract polymorphic DTOs).
- Analytics in the framework (`SuperApp.Framework.Infrastructure/Analytics`): `AddAppAnalytics` (options with startup validation,
  `AnalyticsIdentity`, PostHog client when enabled; gateway and forwarder) and `AddAppFeatureFlags` (adds `IFeatureFlags`; services).
  Only `SuperApp.Framework.Infrastructure` and `SuperApp.AnalyticsForwarder` may reference the `PostHog` package (architecture rule 11).
  `PostHogFeatureFlags` must never fail a request: timeout, no answer or unknown flag returns the code default, logs EventId 400–401
  and increments `superapp.feature_flags.fallbacks`. Hosts without a database use `AddAppServiceDefaults(builder, serviceName)` (never both
  overloads).
- New analyzer rules need a descriptor in `Descriptors.cs` (English title/message with the ADR), an entry in
  `AnalyzerReleases.Unshipped.md` and tests in `SuperApp.Analyzers.Tests`.
- Log EventIds per component are registered in `docs/logowanie-eventid.md`.
- Problem responses: `Api/ProblemDetailsConventions` (`CustomizeProblemDetails` in `AddAppServiceDefaults`) adds a status-based `code`
  and the 32-hex `traceId` to problems created by ASP.NET Core; framework code that writes a problem itself sets `code` and
  `traceId = ProblemDetailsConventions.TraceId(httpContext)` (ADR-0044).
- The gateway gives only its own empty errors a problem body (`Hosting/GatewayStatusCodePages`, CSRF `auth.csrf_header_missing`,
  429 `http.too_many_requests`); answers relayed from a BFF must stay byte-for-byte unchanged.
