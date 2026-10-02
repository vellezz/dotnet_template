---
mode: agent
description: Add a read use case (query) with its GET endpoint, following the repository rules
---

Add a new query end to end. Ask for missing details: service, HTTP route (service and BFF), what is returned, filters, paging,
required scope, visibility rules
(e.g. readers see only published items), whether it should be cached.

Follow `docs/przewodnik/przepisy/02-endpoint-zapytania.md`. Start with the repository tool:
`dotnet superapp add usecase {Service} {Feature} {UseCase} --query --scope {resource}.{action} [--dto {Name}Dto]` creates the query, DTO,
validator, Infrastructure handler and GET action as a compiling skeleton with TODOs; then fill it in:

1. Application, `Features/{Aggregate}/{UseCase}/`: the query record `IQuery<Result<TDto>>` with `[RequiresScope]` and the DTO
   records (primitives and enums only). Paged lists return `PagedResult<TDto>`.
2. Infrastructure, `Features/{Aggregate}/{UseCase}Handler.cs`: `internal sealed` handler using `{Service}ReadDbContext` directly,
   projecting with `Select`, deterministic ordering, `Math.Clamp(pageSize, 1, Paging.MaxPageSize)` and `Paging.Skip`, visibility via
   `ICurrentUser`, NotFound errors for missing or invisible items. Add read models/configurations under `Persistence/Read/` if needed.
3. If cached: key/tag/options in `Caching/{Service}Cache.cs` (versioned key) and invalidation registered with `IUnitOfWork.OnCommitted`
   in a domain event handler.
4. Api: GET action with `[ProducesResponseType<TDto>(200, ...)]`, consumer-facing XML docs, `this.ToActionResult(...)`.
5. Integration tests (Testcontainers) for results, paging and visibility.
6. Expose it in the BFF of the experience (`.github/instructions/bff.instructions.md`; the module never calls the service directly):
   - build the service so that `openapi/{Service}.Api.json` is regenerated (the new operation has `operationId` `{Controller}_{Action}`);
   - regenerate the client: `dotnet tool restore`, then
     `dotnet refitter --settings-file src/Bff/{Experience}.Bff/Clients/{Service}/{service}.refitter` (never edit `Generated/`);
   - add the action to `src/Bff/{Experience}.Bff/Controllers/{Service}/{Service}{Resource}Controller.cs` following the existing actions:
     route `v{n}/{service}/...` matching the service route, body type from the generated client, the same `[ProducesResponseType]` and
     XML docs (scope, rules, every `<response>` with its error codes) as the service action, body
     `this.ToActionResult(await client.{Operation}Async(..., cancellationToken))`; no validation in the BFF;
   - build the BFF so that `openapi/{Experience}.Bff_public.json` is regenerated and commit it with the client.
7. Run `dotnet build SuperApp.slnx` and `dotnet test --solution SuperApp.slnx`; show the diff of the service contract, the generated client and
   the BFF public contract and summarize the contract change. Check the call end to end through the local gateway:
   `https://localhost:5001/api/{experience}/v1/{service}/...` (e.g. `/api/example/v1/knowledge/...`).
