---
applyTo: "**/Migrations/**/*.cs"
---

# EF Core migrations

- Every write-model change gets a NEW migration generated from the write context; never edit a migration that has been applied
  anywhere. Command: `dotnet ef migrations add {Name} -p <Infrastructure project> -s <Infrastructure project> --context {Service}WriteDbContext -o Migrations`
  (gateway: `-p/-s src/Gateway/SuperApp.Gateway --context GatewayDbContext -o Persistence/Migrations`).
- Rename the generated `{timestamp}_{Name}.cs` to `{Name}.cs` (file name = type, APP005); keep the generated `.Designer.cs` as is.
- Expand/contract: old and new application versions run on the same schema during a rolling update. In the expand step only add
  nullable columns, columns with defaults, tables and indexes. Drops, renames, type changes and `NOT NULL` without default belong
  to a later contract migration. Treat EF's "may result in the loss of data" warning as an error in an expand migration.
- Add an XML summary on the migration class stating what it changes, which phase it is and what is left for a contract step.
- `dotnet ef migrations has-pending-model-changes` must report no changes after adding the migration. Do not modify the model snapshot by hand.
- Migrations run through `SuperApp.Migrator` (dev/test) or as idempotent scripts by the DBA (prod); services never migrate at startup.
