namespace SuperApp.Cli.Contracts;

/// <summary>How a change of an OpenAPI contract affects its consumers (ADR-0019, chapter 08 of the developer guide).</summary>
internal enum ContractChangeKind
{
    /// <summary>Backward compatible: a new operation, an optional parameter, a new response property.</summary>
    Compatible,

    /// <summary>May break strict clients: a new enum value in a response, a response property that became nullable, a removed parameter.</summary>
    Warning,

    /// <summary>Breaks existing clients: a removed operation or response property, a new required input, a changed type.</summary>
    Breaking,
}
