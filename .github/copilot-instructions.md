# Copilot instructions for this repository

You work in a .NET 10 microservice solution built with Clean Domain-Driven Design and CQRS. The binding rules are the ADRs in
`docs/adr/` (Polish) and the rules chapter `docs/przewodnik/03-zasady.md`; the developer guide with worked examples is `docs/przewodnik/`. When a request
conflicts with these rules, say so and propose the rule-conforming alternative instead of silently breaking a rule.
Layer-specific rules are in `.github/instructions/*.instructions.md` (BFF: `bff.instructions.md`); step-by-step tasks are prompts in
`.github/prompts/` (`new-command-endpoint`, `new-query-endpoint`, `new-aggregate`, `new-product-event`, `new-experience`,
`review-against-rules`).

## Architecture in one screen

- The system is one **experience** of the organization's **super app** (ADR-0038). Vocabulary used everywhere: *super app* = the
  whole solution; *shell* = another team's native host app (identity, flags, push, navigation); *experience* = one feature owned by
  one team: client module + **BFF experience** + 1..n domain services; *edge gateway* = the super app's shared gateway, out of our
  scope (ADR-0037); *BFF* = the BFF experience, never the gateway profile `bff-web`. Nothing may assume there is only one experience.
- **BFF experience in code.** `src/Bff/Example.Bff` is the BFF of the example experience: a facade of Knowledge and SleepDiary
  (`/v1/knowledge/...`, `/v1/sleepdiary/...` matching the services' `/v1/...`), composed endpoints (`GET /v1/me/summary`) and an
  internal API (`GET /internal/v1/widgets/sleep-summary`, scope `example.internal.read`). The local gateway routes only
  `/api/example/v{n}/**` to it (prefix `/api/example` removed); domain services have no gateway routes and `/internal` is never routed.
  A new experience starts from `dotnet superapp add bff {Experience}` (template `superapp-bff` plus every registration) and is deployed with the Helm chart `deploy/helm/superapp-bff`.
  BFF rules: `.github/instructions/bff.instructions.md`.
- One microservice = one bounded context (`src/Services/{Service}`: Domain, Application, Infrastructure, Api, Worker,
  Contracts, tests). Current contexts: Knowledge (materials, block content, collections, categories, favorites, completions)
  and SleepDiary (one sleep entry per user per day). Each service has its own schema in a shared MSSQL database.
- Never reference another service's projects (the only exception is its `{Service}.Contracts`, the published language) or query
  another schema. Cross-context communication: integration events
  (RabbitMQ, MassTransit 8, transactional outbox) by default; synchronous calls are the exception, through a Refit client:
  - in the user's context (BFF → our services, another experience's BFF → our BFF's internal API, service → service within the
    experience) the **user's token is forwarded unchanged**; the receiver validates the JWT, the scope and the resource rules and
    never trusts a user identifier sent in the payload (ADR-0040);
  - behind the edge gateway only JWT access tokens issued by the CIAM travel (one issuer, passed unchanged; the gateway never mints
    its own tokens; no cookies, ID tokens or identity headers behind it);
  - client credentials only for system calls (no user): an explicit exception with scope `{experience}.internal.system.*` (calls to a BFF's internal API) or `{service}.system.{action}` (service → service within the experience, ADR-0042) and
    caller audit (`azp`); never for data of a specific person identified only by a payload ID;
  - another experience is reached **only through its BFF's internal API**, never its domain services (NetworkPolicy, ADR-0041);
  - external systems always behind an anti-corruption layer with our own credentials; the user's token never leaves the trust boundary.
  - Token exchange (RFC 8693) is an open door for later, not implemented: keep it a change of the forwarding handler only.
- Dependency rule (enforced by architecture tests): `Domain` → only `SuperApp.Framework.Domain`; `Application` → Domain,
  Contracts, `SuperApp.Framework.Application` (no EF Core, no MassTransit, no Infrastructure); Api/Worker reference Infrastructure
  only in the composition root.
- Shared technical code lives in `src/Framework/SuperApp.Framework.*` (no business concepts). Its XML docs are the reference for
  how every type is meant to be used: read them before using `ICommand`, `Result`, `IUnitOfWork`, `FailSafeCache`.
- Clients never call domain services directly. The module calls only its BFF's **public API** (`/v{n}`) through the edge gateway;
  the BFF's **internal API** (`/internal/v{n}`) serves other experiences' BFFs, changes only in a backward-compatible way and requires
  scope `{experience}.internal.*`; domain service APIs are internal to the experience (ADR-0039).
- `src/Gateway/SuperApp.Gateway` (YARP, profiles `bff-web` and `gateway-mobile`) is a **local stand-in for the shared edge gateway** and
  the specification of our requirements towards it (ADR-0037). Do not add experience logic to it; that belongs in the BFF experience.
- A BFF has no database, domain or migrations and references no service project (not even `Contracts`) and no other BFF
  (architecture rules 12–14). It calls the services through Refit clients generated by Refitter
  (`Clients/{Service}/{service}.refitter`, public types), registered with
  `AddDownstreamApi<I{Service}Api>(configuration, "{Service}").AddUserTokenForwarding()` (`Downstream:{Service}:BaseAddress`), and relays
  their answers unchanged with `this.ToActionResult(...)`; it does not validate requests (validation belongs to the services). An
  unreachable or slow service gives `503 downstream.unavailable` / `504 downstream.timeout`.
- Tokens are validated with `ClockSkew` 30 s (`HostingExtensions.TokenClockSkew`, used by `AddAppApi` and the local mobile gateway).
- Product analytics, session replay and feature flags use PostHog Cloud EU (ADR-0036). Domain services never send analytics
  events and never reference the PostHog SDK: they read flags through the `IFeatureFlags` port, and backend product events are
  produced by `src/Analytics/SuperApp.AnalyticsForwarder` from the services' existing integration events. Only
  `SuperApp.Framework.Infrastructure` and the forwarder may use the `PostHog` package (architecture rule 11).

## Core rules

- Business logic belongs in aggregates. Handlers orchestrate: convert inputs to strongly typed IDs/value objects, load the
  aggregate through its repository, call one aggregate method, return its `Result`. One command changes one aggregate.
- Expected failures are `Result`/`Result<T>` with an `Error` (stable code `{service}.{concept}.{problem}`, type Validation 400,
  Forbidden 403, NotFound 404, Conflict 409, BusinessRule 422). Exceptions only for technical failures. Never ignore a `Result`.
- Every HTTP error is `application/problem+json` with `code` and a 32-hex `traceId`, also errors produced by ASP.NET Core
  (ADR-0044, `ProblemDetailsConventions`).
- The module runs inside the super app shell: identity, module visibility flags and analytics consent are expected from the shell
  (ADR-0045, proposed, not yet binding); do not build client features that duplicate them without asking.
- Read values with `TryGetValue`: `if (!Category.Create(name, slug).TryGetValue(out var category, out var error)) return error;`.
  `Result<T>` has no `Value`, and `Result.Error` is `Error?`, non-null only after checking `IsFailure`/`IsSuccess` (ADR-0047).
  Tests unwrap results with `ResultAssert.Success(...)`/`ResultAssert.Failure(...)` from `SuperApp.Framework.Testing`, which
  production code must never reference.
- Command handlers never call `SaveChangesAsync`: `TransactionBehavior` saves, dispatches domain events, writes the outbox and
  commits after a successful result. Repository `Add`/`Remove` are synchronous (change tracker only); reads are async.
- Unique index races and `rowversion` conflicts come back from the save as `Conflict` results (mapped code, `persistence.duplicate`,
  `persistence.concurrency_conflict`); never catch `DbUpdateException`/`DbUpdateConcurrencyException` in service code. In a
  consumer a failed command's tracked changes are discarded by `TransactionBehavior`; a concurrency conflict there stays an
  exception so MassTransit retries the message.
- Queries bypass the domain: query handlers live in Infrastructure, read `{Service}ReadDbContext` and project to DTOs that
  contain only primitives and enums.
- Strongly typed IDs and single-value objects are hand-written `readonly record struct` types created only via `Create(...)`
  (validation) or `New()`; never `default`/`new()` (APP002). `FromTrusted` is for infrastructure and tests only.
- Domain events implement `IDomainEvent` and are handled by `IDomainEventHandler<T>` inside the unit of work. Never use MediatR
  `INotification`, `IPublisher` or `IMediator` (APP003). Work that needs a committed change (cache invalidation) is registered
  with `IUnitOfWork.OnCommitted`.
- Integration events are versioned records in `{Service}.Contracts` (`MaterialArchivedV1`), primitives only, published only by
  translating a domain event via `IIntegrationEventPublisher`; event timestamps come from the aggregate.
- Authorization: `[RequiresScope({Service}Scopes.X)]` on every command/query (scope names `{service}.{resource}.{action}` as
  constants); resource rules (owner from `sub`, someone else's resource = 404, draft visibility) in the handler or aggregate via
  `ICurrentUser`. The service checks them **always**, whoever calls: the gateway and the BFF only check earlier (coarse-grained, UX).
  Service entitlements (e.g. an active service in a domain) are domain facts enforced by the service, never token claims.
- Unique indexes users can hit concurrently are mapped to context errors in `{Service}WriteDbContext.UniqueConstraintErrors`.
- Feature flags: declare `public static readonly FeatureFlag X = new("{service}_{feature}", defaultValue)` in `{Service}FeatureFlags`
  next to `{Service}Scopes`; read them in handlers with `await featureFlags.IsEnabledAsync({Service}FeatureFlags.X, cancellationToken)`.
  The default value must be safe to run with permanently (it is used when PostHog is slow, down or does not know the flag). A flag
  is never a permission: access is decided by scopes and resource rules.
- Privacy: PostHog only ever receives the pseudonym `u_…` (HMAC of `sub`, `AnalyticsIdentity`), never `sub`, e-mail or name, and only
  allow-listed event properties (identifiers of catalog objects, categories). Never send user-entered text or sleep diary data
  (health data). A new product event is a consumer in the forwarder plus a name in `ProductEventNames`, never code in a service.

## Code conventions (build fails otherwise)

- One top-level type per file; file name equals type name (`Result{T}.cs` for generics, `Order.Log.cs` for partial parts) (APP004, APP005).
- Folders by concern; namespace equals folder path (IDE0130).
- C#: nullable enabled, warnings as errors, `sealed` and `internal` by default, file-scoped namespaces, primary constructors
  where readable, `CancellationToken` on every async method.
- Names, XML docs and comments in English. Every public/protected type and member has complete XML docs written for a
  developer new to the code base: `<summary>` (purpose and role), `<remarks>` (rules, invariants, lifecycle, pitfalls, related
  types), `<param>` for every parameter including positional record parameters, `<typeparam>`, `<returns>` (for `Result`: the
  success value and every error code), `<exception>` when thrown on purpose, `<example>` when usage is not obvious;
  `<inheritdoc />` for implementations that add nothing. Never restate the signature (CS1591, APP006).
- Logging only through `[LoggerMessage]` source-generated methods with an `EventId` from the service range in
  `docs/logowanie-eventid.md` (`dotnet superapp list eventids` shows the next free ID); no string interpolation, no personal data,
  no tokens or secrets.
- Use the repository tool `dotnet superapp` (ADR-0046; install with `tools/bootstrap.sh` or `.ps1`): `list ports|eventids|scopes`
  before choosing a port, an event ID or a scope, `info <service|BFF>` before changing one, and `doctor` after every change.
  Create and remove services, BFFs and service clients of a BFF only with `dotnet superapp add|remove service|bff|client`
  (`--dry-run` first); never register them by hand in the solutions, Migrator, SQL bootstrap, realm, compose, Helm, gateway or
  architecture tests. Do the remaining work the command lists under next steps. New use cases start from
  `dotnet superapp add usecase`, migrations from `dotnet superapp migration add`, and contracts with their clients are regenerated with
  `dotnet superapp contracts` (check with `--check`; `contracts snapshot` before an API change and `contracts diff` after it report
  breaking changes, exit code 2). Aggregates, events, consumers, scopes, flags and product events start from
  `dotnet superapp add aggregate|event|consumer|scope|flag|product-event`; the local environment is `dotnet superapp env up|status|down`
  and `dotnet superapp e2e` checks it end to end.
- Do not add NuGet packages or change versions without an explicit request; versions live in `Directory.Packages.props`.
  MediatR stays on 12.x and MassTransit on 8.x (last open-source versions, ADR-0035).
- No secrets in `appsettings*.json` or code. Do not mention AI assistants in code, docs or commit messages.

## Where things go (Knowledge as example)

| Artifact | Location |
|---|---|
| Aggregate, ID, errors, repository interface | `Knowledge.Domain/{Aggregate}/` |
| Domain event | `Knowledge.Domain/{Aggregate}/Events/` |
| Command/query, validator, command handler, DTO | `Knowledge.Application/Features/{Aggregate}/{UseCase}/` (one file each) |
| Domain → integration event translator | `Knowledge.Application/IntegrationEvents/` |
| Query handler | `Knowledge.Infrastructure/Features/{Aggregate}/{UseCase}Handler.cs` |
| EF configuration (write / read) | `Knowledge.Infrastructure/Persistence/Write/Configurations/`, `.../Read/Configurations/` |
| Repository, read model | `Knowledge.Infrastructure/Persistence/Write/Repositories/`, `.../Read/Models/` |
| Cache keys, tags, invalidation | `Knowledge.Infrastructure/Caching/` |
| Migration | `Knowledge.Infrastructure/Migrations/{Name}.cs` (rename the generated file, keep the `.Designer.cs`) |
| Controller action, request body record | `Knowledge.Api/Controllers/` |
| Message consumer | `Knowledge.Worker/Consumers/` |
| Integration event | `Knowledge.Contracts/` |
| Feature flag declarations | `Knowledge.Application/KnowledgeFeatureFlags.cs` (next to `KnowledgeScopes.cs`) |
| BFF facade action (public API) | `src/Bff/{Experience}.Bff/Controllers/{Service}/{Service}{Resource}Controller.cs` |
| BFF composed endpoint, internal API | `src/Bff/{Experience}.Bff/Controllers/Experience/`, `.../Controllers/Internal/` |
| BFF client of a service (Refitter) | `src/Bff/{Experience}.Bff/Clients/{Service}/{service}.refitter` + `Generated/` (never edited) |
| Backend product event (PostHog) | `SuperApp.AnalyticsForwarder/Consumers/{Event}Consumer.cs` + name in `SuperApp.AnalyticsForwarder/Events/ProductEventNames.cs` |

## Definition of done

every new project added to `SuperApp.slnx` and `SuperApp.sln` regenerated with `dotnet superapp doctor --fix`,
`dotnet superapp doctor` with 0 errors (run it last and fix every finding; `--json` gives machine-readable findings with a `fix` each),
`dotnet build SuperApp.slnx` with 0 errors and 0 warnings, `dotnet test --solution SuperApp.slnx` green (including architecture tests),
reviewed diff of the OpenAPI contracts for API changes (services' `openapi/*.json` and the BFF's public/internal documents, with the
regenerated Refitter clients), new tests at the right level (domain without mocks, application with fakes,
integration with Testcontainers), and a new migration (never edited applied ones) for every write-model change, in
expand/contract style. See `docs/przewodnik/18-checklista.md`.
