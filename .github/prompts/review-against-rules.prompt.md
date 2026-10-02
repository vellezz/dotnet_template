---
mode: ask
description: Review the current changes against the repository rules before a pull request
---

Review the current changes (or the files I point to) against the ADRs in `docs/adr/`, the rules chapter `docs/przewodnik/03-zasady.md`, the layer instructions in
`.github/instructions/` and the checklist `docs/przewodnik/18-checklista.md`.

Report only concrete findings, each with file and line, the rule it breaks (with the ADR or instruction file), why it matters and
the minimal fix. Check in particular: business logic outside aggregates, saving or second-aggregate changes in handlers, domain types
in DTOs or contracts, missing `[RequiresScope]`, ignored or chained `Result` values instead of `TryGetValue`, cache invalidation
not using `IUnitOfWork.OnCommitted`, unmapped unique indexes, missing `[ProducesResponseType]`, edited applied migrations or
destructive expand migrations, more than one type per file, namespace not matching the folder, shallow or missing XML docs,
logging without `[LoggerMessage]`, personal data in logs, new packages, PostHog SDK or analytics code in a service, feature flags
used as permissions or with an unsafe default, product event properties outside the allow-list (user text, `sub`, sleep diary data),
the `/ingest` proxy moved to database routes or losing its header removals, experience logic added to `SuperApp.Gateway` or new
gateway routes to domain services or `internal` paths (ADR-0037, ADR-0039), synchronous calls that use client credentials in the
user's context or trust a user ID from the payload, calls to another experience's domain services instead of its BFF internal API
(ADR-0040, ADR-0041), breaking changes in a BFF internal API, business logic, validation or state changes of several domains in a
BFF, BFF references to service projects or other BFFs, hand-edited Refitter output or a service contract change without the
regenerated BFF client and contract, and docs describing open items (shared gateway, token exchange, NetworkPolicy manifests) as
existing code. If everything conforms, say so in one sentence.
