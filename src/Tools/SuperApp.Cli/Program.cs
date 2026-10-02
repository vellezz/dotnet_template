using SuperApp.Cli;

// Entry point of `dotnet superapp` (ADR-0046). All commands are defined in CliApplication; this file only runs them.
return await CliApplication.Create().Parse(args).InvokeAsync();
