---
applyTo: "src/**/tests/**/*.cs,src/**/*.Tests/**/*.cs,tests/**/*.cs,src/Tools/SuperApp.Cli/Templates/superapp-service/tests/**/*.cs,src/Tools/SuperApp.Cli/Templates/superapp-bff/*.Tests/**/*.cs"
---

# Tests

- xUnit v3 on Microsoft.Testing.Platform; test names describe behavior as a sentence (`Renaming_to_the_current_name_changes_nothing`).
- Levels:
  - `{Service}.Domain.Tests`: aggregates, value objects, IDs; plain unit tests, no mocks; assert `Result` and error codes
    with `ResultAssert.Success(...)`/`ResultAssert.Failure(...)` (`SuperApp.Framework.Testing`, ADR-0047)
    (`Assert.Equal(CategoryErrors.SlugTaken, result.Error)`).
  - `{Service}.Application.Tests`: handlers, validators, translators and the MediatR pipeline with fakes from `Fakes/` (one type per file).
  - `{Service}.IntegrationTests`: query handlers, repositories, EF configuration, migrations, database constraints, unique-index
    mapping, cache invalidation; real MSSQL via Testcontainers; send requests with `fixture.SendAsync(...)`.
- Integration test classes sharing the fixture use `[Collection(PipelineCollection.Name)]` and set the caller at the start
  (`fixture.CurrentUser.Scopes.Clear(); fixture.CurrentUser.Scopes.Add(...)`); use unique data per test (`$"slug-{Guid.NewGuid():N}"`).
- Integration events are asserted on the fake publisher (`fixture.Publisher`); the bus is not started in tests.
- `SuperApp.AnalyticsForwarder.Tests` (ADR-0036): consumers are tested on the MassTransit test harness (`AddMassTransitTestHarness`,
  publish the integration event, `harness.Consumed.Any<T>()`) with `RecordingProductEventSink` from `Fakes/`; assert the event name,
  the subject (user `sub` or `null` for system events) and the exact set of property keys, so a new property cannot slip through
  without a test change. No PostHog connection in tests: analytics stays disabled and flags come from `FeatureFlags:{key}`.
- `{Experience}.Bff.Tests` (no database, no Testcontainers): the split and security of the two committed contracts
  (`ContractSplitTests`), relaying of service answers unchanged (`DownstreamResponseTests`, Refit answers built by `Responses`),
  generated clients with the framework settings and token forwarding (`DownstreamClientTests`, `UserTokenForwardingTests`, a recording
  HTTP handler) and partial rendering of composed endpoints (`PartialResponseFetcherTests`).
- Architecture rules 12–14 (`tests/SuperApp.ArchitectureTests`): a BFF references no service project and no other BFF, has no `DbContext`, and
  no service references a BFF. A new BFF is checked only when `tests/SuperApp.ArchitectureTests/SuperApp.ArchitectureTests.csproj` references
  it (`.Bff` assembly name suffix); otherwise it is silently skipped.
- Code reading feature flags is tested for both values (a fake `IFeatureFlags` in Application tests, `FeatureFlags:{key}` in
  integration tests).
- Test projects are exempt from XML doc completeness, but comments are in English and the one-type-per-file rule applies.
