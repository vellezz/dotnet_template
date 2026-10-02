namespace SuperApp.Cli.Contracts;

/// <summary>One difference between the baseline and the current version of a contract.</summary>
/// <param name="Kind">Whether it breaks consumers.</param>
/// <param name="Contract">Contract file relative to the repository root.</param>
/// <param name="Location">Where in the contract, e.g. <c>GET /v1/materials/{id} 200 .title</c>.</param>
/// <param name="Description">What changed.</param>
internal sealed record ContractChange(ContractChangeKind Kind, string Contract, string Location, string Description);
