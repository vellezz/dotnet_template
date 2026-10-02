---
applyTo: "src/Services/**/*.Api/**/*.cs,src/Tools/SuperApp.Cli/Templates/superapp-service/ServiceName.Api/**/*.cs"
---

# Api layer (controllers)

- Controllers are thin: map route/query/body to a command or query, `await sender.Send(...)` (MediatR `ISender`), return
  `this.ToActionResult(result)` or `this.ToActionResult(result, id => StatusCode(StatusCodes.Status201Created, new CreatedResponse(id)))`.
  No business logic, no status code decisions, no try/catch for expected failures.
- Who consumes which API (ADR-0039): a domain service's API (`/v{n}/...`) is internal to the experience: its consumers are the BFF
  and services of the same experience, never the edge gateway (the gateway has no routes to domain services). Its contract is the
  source of the BFF's Refitter client: a new or changed operation reaches the module only through a BFF action
  (`.github/instructions/bff.instructions.md`). In the BFF experience, the **public API** (`/v{n}/...`, consumer: our module only, through the
  edge gateway; breaking change = new `/v{n+1}`) and the **internal API** (`/internal/v{n}/...`, consumers: other experiences' BFFs,
  in-cluster only, backward-compatible changes only, operations designed for the consumer, scope `{experience}.internal.*`;
  system calls `{experience}.internal.system.*` with the justification documented in the contract) are separate OpenAPI documents.
- Every API validates the JWT itself (`AddAppApi`, `ClockSkew` 30 s) and applies scope and resource rules regardless of the caller;
  never accept a user identifier from the payload as the basis for access.
- Routes are versioned (`[Route("v1/...")]`), IDs constrained (`{materialId:guid}`); request body records live next to the
  controller (`{Action}Request.cs`) when the body differs from the command.
- Every action declares each response for the OpenAPI contract: success (`[ProducesResponseType<TDto>(200, MediaTypeNames.Application.Json)]`,
  201 with `CreatedResponse`, or 204) and action-specific errors as `ProblemDetails`/`ValidationProblemDetails` with
  `MediaTypeNames.Application.ProblemJson`. 401 and 403 are declared once at controller level. Never add `[Produces("application/json")]`
  (it overrides `application/problem+json` on errors).
- Every error response carries `code` and a 32-hex `traceId` (ADR-0044): codes from `Result` via `ResultHttpExtensions`; framework
  errors (binding 400, 401, route 404, 415, 500) get a status code from `ProblemDetailsConventions`, registered by `AddAppServiceDefaults`.
  Keep `app.UseExceptionHandler()` and `app.UseStatusCodePages()` in `Program.cs`; never write `traceId` by hand in another format.
- XML docs are written for API consumers and end up in the contract: `<summary>`, `<remarks>` with the required scope and
  limits, `<param>` for every parameter (put `cancellationToken` before the body parameter), `<returns>`, and `<response code="...">`
  for every status with the error codes it can carry.
- After a change, the build regenerates `openapi/{Project}.json` (label `3.0.3`); the contract diff is part of the review and must
  stay backward compatible (new operations and new trailing DTO fields are fine; renames/removals need a new API version).
- `Program.cs` follows the existing hosts: `AddAppServiceDefaults`, `AddAppApi`, `AddOpenApi(options => HostingExtensions.ConfigureOpenApi(options))`,
  `Add{Service}Infrastructure(..., OutboxDelivery.Disabled)`, `MapAppDefaultEndpoints`.
