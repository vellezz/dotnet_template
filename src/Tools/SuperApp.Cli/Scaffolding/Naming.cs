using System.Text.RegularExpressions;

namespace SuperApp.Cli.Scaffolding;

/// <summary>Validates names given to scaffolding commands.</summary>
/// <remarks>
/// A service or experience name becomes a C# namespace (<c>Billing.Domain</c>), a schema and login (<c>billing</c>, <c>billing_app</c>),
/// compose and Kubernetes names (<c>billing-api</c>) and a scope prefix (<c>billing.</c>). PascalCase letters and digits are valid in all
/// of them once lower-cased; anything else is rejected before a file changes.
/// </remarks>
internal static partial class Naming
{
    /// <summary>Checks a service or experience name.</summary>
    /// <param name="name">The name as given, e.g. <c>Billing</c>.</param>
    /// <param name="what">What the name is for the message, e.g. <c>service</c>.</param>
    /// <exception cref="ScaffoldException">The name is not PascalCase letters and digits, or is reserved.</exception>
    public static void EnsurePascalCase(string name, string what)
    {
        if (!PascalCase().IsMatch(name))
        {
            throw new ScaffoldException($"The {what} name must be PascalCase letters and digits starting with a capital letter (e.g. Billing); got \"{name}\".");
        }

        if (name.StartsWith("SuperApp", StringComparison.Ordinal) || name is "Gateway" or "Migrator" or "Framework")
        {
            throw new ScaffoldException($"\"{name}\" is reserved for the technical components of the solution; choose a domain name.");
        }
    }

    /// <summary>Checks a scope without the service prefix, e.g. <c>invoice.read</c>.</summary>
    /// <param name="scope">The scope as given.</param>
    /// <exception cref="ScaffoldException">The scope is not <c>{resource}.{action}</c> in lower case.</exception>
    public static void EnsureScope(string scope)
    {
        if (!Scope().IsMatch(scope))
        {
            throw new ScaffoldException($"A scope is {{resource}}.{{action}} in lower case (e.g. invoice.read, ADR-0012); got \"{scope}\".");
        }
    }

    [GeneratedRegex("^[A-Z][A-Za-z0-9]{1,40}$")]
    private static partial Regex PascalCase();

    [GeneratedRegex("^[a-z][a-z0-9]*\\.[a-z][a-z0-9_]*$")]
    private static partial Regex Scope();
}
