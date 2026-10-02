---
mode: agent
description: Add a new experience BFF (project, clients, gateway route, realm, compose, Helm) following the repository rules
---

Add the BFF of a new experience end to end. Ask for missing details: experience name (PascalCase), the domain services it calls (new
ones and their scopes), the first public endpoints, and whether an internal API is needed now.

Use the repository tool `dotnet superapp` (ADR-0046, `docs/przewodnik/22-narzedzie-superapp.md`; install with `tools/bootstrap.sh` or
`tools/bootstrap.ps1` if `dotnet superapp --help` fails). It creates and registers everything mechanical; never make those edits by
hand. Follow `docs/przewodnik/przepisy/11-nowa-experience-i-bff.md`, `.github/instructions/bff.instructions.md` and the example
`src/Bff/Example.Bff` for the rest. Steps:

1. BFF: `dotnet superapp add bff {Experience} --dry-run`, check the plan, then run it without `--dry-run`. It generates the project and
   its tests with a free port, adds them to the solutions, compose, Helm, the gateway (policy, route and migration), the local realm
   (`{experience}-bff-audience`, `{experience}.internal.read`), the architecture tests and the EventId register.
2. Domain services of the experience (if new): `dotnet superapp add service {Service} --experience {experience} --scope {resource}.{action} ...`,
   then model the first aggregate and the migration `Initial` (recipe 03, recipe 07 step 2) and describe each scope in `{Service}Scopes`
   (the tool leaves a TODO in each summary).
3. Clients: build (`dotnet build SuperApp.slnx`, generates each service contract), then for every service
   `dotnet superapp add client --bff {experience} --service {Service}`. For services whose contract uses dates without a time zone set
   `dateTimeType` to `System.DateTime` in the `.refitter` file and run `dotnet refitter --settings-file ...` again.
4. Endpoints: pass-through actions in `Controllers/{Service}/` (`this.ToActionResult(...)`, docs and `[ProducesResponseType]` copied
   from the service contract), composed endpoints in `Controllers/Experience/` with `PartialResponseFetcher`, internal API (if any) in
   `Controllers/Internal/` under `internal/v1/...` with the `{Experience}BffScopes.InternalRead` policy. Build, then review and commit
   `openapi/{Experience}.Bff_public.json` and `openapi/{Experience}.Bff_internal.json`.
5. Tests: keep the template's `ContractSplitTests`; add tests for composed endpoints and mappings as in `src/Bff/Example.Bff.Tests`.
   Run `dotnet build SuperApp.slnx`, `dotnet test --solution SuperApp.slnx` (including architecture tests) and `dotnet superapp doctor`
   (0 errors; fix every finding, each has a `fix`).
6. End to end: `docker compose -f deploy/local/docker-compose.yml --profile app up -d --build`, then through the gateway
   `https://localhost:5001/api/{experience}/v1/...` (session) or `https://localhost:5002/api/{experience}/v1/...` (`dev-cli` token);
   check that `/api/{experience}/internal/...` is not routed and that the internal API called directly on the BFF port answers 200 with
   the internal scope and 403 without it.

Never add gateway routes to the domain services or to `internal` paths (ADR-0039). Summarize the created files, the new contracts, the
gateway migration, the next steps printed by the tool and the open items (requirements for the shared gateway, the CIAM and the
infrastructure department, NetworkPolicy).
