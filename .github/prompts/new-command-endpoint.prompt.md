---
mode: agent
description: Add a state-changing use case (command) with its endpoint, following the repository rules
---

Add a new command use case end to end. Ask for anything below that is not given: service, aggregate, use case name, HTTP route
(service and BFF), required scope, inputs, rules and error codes.

Follow `docs/przewodnik/przepisy/01-endpoint-komendy.md` and the layer instructions in `.github/instructions/`. Start with the repository
tool: `dotnet superapp add usecase {Service} {Feature} {UseCase} --scope {resource}.{action}` creates the command, validator, handler and
controller action as a compiling skeleton with TODOs (a new scope first: `dotnet superapp add service {Service} --experience ... --scope ...`).
Then fill it in. Steps:

1. Domain: add or extend the aggregate method named in the ubiquitous language; check invariants, change state, raise a domain event
   if others must react, return `Result`. Add reusable errors to `{Aggregate}Errors` (`{service}.{concept}.{problem}`).
2. Application, `Features/{Aggregate}/{UseCase}/`: the command record (primitives only, `[RequiresScope]` with a constant from
   `{Service}Scopes`), its validator (domain constants, same trimming as the aggregate) and an `internal sealed` handler that converts
   inputs with `Create(...).TryGetValue(...)`, loads through the repository, calls one aggregate method and returns its `Result`
   without saving.
3. Api: a thin controller action with `[ProducesResponseType]` for every status, consumer-facing XML docs with `<response>` codes,
   `this.ToActionResult(...)`; request body record next to the controller if needed.
4. Tests: domain test for the rule (no mocks), integration test through `fixture.SendAsync` (collection `PipelineCollection.Name`).
5. Complete English XML docs on every new public type and member; one type per file; namespace = folder.
6. Expose it in the BFF of the experience (`.github/instructions/bff.instructions.md`; the module never calls the service directly):
   - build the service so that `openapi/{Service}.Api.json` is regenerated (the new operation has `operationId` `{Controller}_{Action}`);
   - regenerate the clients: `dotnet superapp contracts` (never edit `Generated/`);
   - add the action to `src/Bff/{Experience}.Bff/Controllers/{Service}/{Service}{Resource}Controller.cs` following the existing actions:
     route `v{n}/{service}/...` matching the service route, body type from the generated client, the same `[ProducesResponseType]` and
     XML docs (scope, rules, every `<response>` with its error codes) as the service action, body
     `this.ToActionResult(await client.{Operation}Async(..., cancellationToken))`; no validation in the BFF;
   - build the BFF so that `openapi/{Experience}.Bff_public.json` is regenerated and commit it with the client.
7. Run `dotnet build SuperApp.slnx` and `dotnet test --solution SuperApp.slnx`; show the diff of the service contract, the generated client and
   the BFF public contract and summarize the contract change. Check the call end to end through the local gateway:
   `https://localhost:5001/api/{experience}/v1/{service}/...` (e.g. `/api/example/v1/knowledge/...`).
