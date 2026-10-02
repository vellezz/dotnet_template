namespace SuperApp.Cli.LocalEnvironment;

/// <summary>Result of one check of the end-to-end scenario.</summary>
/// <param name="Name">What was checked, e.g. <c>gateway-mobile: 401 without a token has code auth.invalid_token</c>.</param>
/// <param name="Ok">Whether the check passed.</param>
/// <param name="Detail">What was observed: status code and the start of the body.</param>
internal sealed record E2eCheck(string Name, bool Ok, string Detail);
