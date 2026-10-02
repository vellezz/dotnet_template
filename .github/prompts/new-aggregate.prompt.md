---
mode: agent
description: Add a new dataset (aggregate, table, repository, read model, migration) to an existing bounded context
---

Add a new aggregate to an existing service. First check `docs/przewodnik/04-wybor-kontekstu.md` and state in one paragraph why the
data belongs to this context and why it is a separate aggregate (own identity, lifecycle and invariants). Ask for missing details.

Follow `docs/przewodnik/przepisy/03-nowy-zbior-danych.md`. Start with the repository tool:
`dotnet superapp add aggregate {Service} {Folder} {Aggregate}` creates the aggregate, ID, errors, repository port and implementation, EF
configuration, registration and a domain test as a compiling skeleton; then `dotnet superapp migration add {Service} Add{Aggregate}`
once the state is mapped. Then:

1. Domain folder `{Aggregate}/`: aggregate (`AggregateRoot<TId>`, private constructor, factory returning `Result<T>`, domain verbs),
   strongly typed ID, value objects, `{Aggregate}Errors`, `I{Aggregate}Repository`, `Events/`.
2. Infrastructure: write configuration (`Persistence/Write/Configurations`), repository (`Persistence/Write/Repositories`, sync `Add`),
   registration in the composition root, unique index names mapped in `UniqueConstraintErrors`, read model and configuration
   (`Persistence/Read/Models`, `Persistence/Read/Configurations`) and `IQueryable` on the read context.
3. Migration with `dotnet ef migrations add ...`, file renamed to `{Name}.cs`, XML summary with the expand/contract phase,
   `has-pending-model-changes` clean.
4. Use cases through the command/query recipes; tests on all three levels (domain, application, integration incl. `SchemaTests`).
5. Build and test the solution.
