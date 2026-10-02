using System.Globalization;
using System.Text;

namespace SuperApp.Cli.Scaffolding.Editors;

/// <summary>Source code and edits of cross-cutting elements: feature flags (ADR-0036) and product events of the analytics forwarder.</summary>
internal static class ElementCode
{
    /// <summary>The <c>{Service}FeatureFlags</c> class of a service, created with its first flag.</summary>
    /// <param name="service">Service name.</param>
    /// <param name="key">Lower-case service name, the prefix of flag keys.</param>
    /// <returns>The file content without flags.</returns>
    public static string FlagsClass(string service, string key) => $$"""
        using SuperApp.Framework.Application.FeatureFlags;

        namespace {{service}}.Application;

        /// <summary>
        /// Feature flags of the {{service}} service (ADR-0036): switches for features being rolled out gradually and kill switches of
        /// existing features, evaluated through <see cref="IFeatureFlags"/>.
        /// </summary>
        /// <remarks>
        /// Keys are PostHog flag keys with the <c>{{key}}_</c> prefix, and configuration keys (<c>FeatureFlags:{key}</c>) where analytics is
        /// disabled. Each default value is what users get when PostHog cannot answer, so it must be safe to run with indefinitely. A flag is
        /// not a permission: scopes still apply (ADR-0012).
        /// </remarks>
        public static class {{service}}FeatureFlags
        {
        }

        """;

    /// <summary>Adds a flag constant to the flags class; no change when the key exists.</summary>
    /// <param name="content">Content of <c>{Service}FeatureFlags.cs</c>.</param>
    /// <param name="flagKey">Full flag key, e.g. <c>billing_invoice_reminders</c>.</param>
    /// <param name="defaultValue">Value when PostHog cannot answer.</param>
    /// <returns>The new content.</returns>
    /// <exception cref="ScaffoldException">The class has no closing brace.</exception>
    public static string AddFlag(string content, string flagKey, bool defaultValue)
    {
        if (content.Contains($"\"{flagKey}\"", StringComparison.Ordinal))
        {
            return content;
        }

        var lines = TextEdits.Lines(content);
        var close = lines.FindLastIndex(line => line == "}");
        if (close < 0)
        {
            throw new ScaffoldException("Cannot find the end of the feature flags class; add the flag by hand.");
        }

        List<string> added =
        [
            $"    /// <summary><c>{flagKey}</c>: TODO what it switches and why the default ({(defaultValue ? "on" : "off")}) is safe when PostHog cannot answer.</summary>",
            $"    public static readonly FeatureFlag {Pascal(flagKey)} = new(\"{flagKey}\", DefaultValue: {(defaultValue ? "true" : "false")});",
        ];
        if (lines[close - 1].TrimEnd() != "{")
        {
            added.Insert(0, string.Empty);
        }

        lines.InsertRange(close, added);
        return TextEdits.Join(lines, content);
    }

    /// <summary>Removes a flag constant with its summary line.</summary>
    /// <param name="content">Content of <c>{Service}FeatureFlags.cs</c>.</param>
    /// <param name="flagKey">Full flag key.</param>
    /// <returns>The new content.</returns>
    public static string RemoveFlag(string content, string flagKey) => RemoveMember(content, $"\"{flagKey}\"");

    /// <summary>Returns the constant name of a flag key: <c>billing_invoice_reminders</c> → <c>InvoiceReminders</c> (without the service prefix).</summary>
    /// <param name="flagKey">Full flag key.</param>
    /// <returns>The PascalCase name.</returns>
    public static string Pascal(string flagKey)
    {
        var parts = flagKey.Split('_', StringSplitOptions.RemoveEmptyEntries).Skip(1);
        return string.Concat(parts.Select(part => char.ToUpper(part[0], CultureInfo.InvariantCulture) + part[1..]));
    }

    /// <summary>Adds a constant to <c>ProductEventNames</c> of the forwarder.</summary>
    /// <param name="content">Content of <c>ProductEventNames.cs</c>.</param>
    /// <param name="constant">Constant name, e.g. <c>BillingInvoiceIssued</c>.</param>
    /// <param name="eventName">Product event name in snake_case, e.g. <c>billing_invoice_issued</c>.</param>
    /// <param name="contract">Integration event it comes from, for the summary.</param>
    /// <returns>The new content.</returns>
    /// <exception cref="ScaffoldException">The class has no closing brace.</exception>
    public static string AddEventName(string content, string constant, string eventName, string contract)
    {
        if (content.Contains($"\"{eventName}\"", StringComparison.Ordinal))
        {
            return content;
        }

        var lines = TextEdits.Lines(content);
        var close = lines.FindLastIndex(line => line == "}");
        if (close < 0)
        {
            throw new ScaffoldException("Cannot find the end of ProductEventNames; add the name by hand.");
        }

        lines.InsertRange(close,
        [
            string.Empty,
            $"    /// <summary><c>{eventName}</c>: forwarded from <c>{contract}</c>. TODO: what it measures in product analytics.</summary>",
            $"    public const string {constant} = \"{eventName}\";",
        ]);
        return TextEdits.Join(lines, content);
    }

    /// <summary>Removes a constant of <c>ProductEventNames</c> with its summary line.</summary>
    /// <param name="content">Content of <c>ProductEventNames.cs</c>.</param>
    /// <param name="eventName">Product event name.</param>
    /// <returns>The new content.</returns>
    public static string RemoveEventName(string content, string eventName) => RemoveMember(content, $"\"{eventName}\"");

    /// <summary>The forwarder consumer that maps an integration event to a product event.</summary>
    /// <param name="publisher">Service that publishes the contract.</param>
    /// <param name="contract">The integration event and its parameters.</param>
    /// <param name="consumer">Consumer class name.</param>
    /// <param name="constant">Constant of <c>ProductEventNames</c>.</param>
    /// <returns>The file content.</returns>
    public static string ProductEventConsumer(string publisher, ContractRecord contract, string consumer, string constant)
    {
        var time = contract.Parameters.FirstOrDefault(parameter => parameter.Type == "DateTimeOffset").Name;
        var subject = contract.Parameters.FirstOrDefault(parameter => parameter is { Type: "string", Name: "UserId" }).Name;
        var identifiers = contract.Parameters.Where(parameter => parameter.Type == "Guid").ToList();
        var properties = new StringBuilder();
        foreach (var (_, name) in identifiers)
        {
            properties.Append($"\n                [\"{Snake(name)}\"] = message.{name}.ToString(),");
        }

        var body = identifiers.Count == 0 ? "new Dictionary<string, object>()" : $"new Dictionary<string, object>\n            {{{properties}\n            }}";
        return $$"""
            using SuperApp.AnalyticsForwarder.Events;
            using MassTransit;
            using {{publisher}}.Contracts;

            namespace SuperApp.AnalyticsForwarder.Consumers;

            /// <summary>Forwards <see cref="{{contract.Name}}"/> to product analytics as <see cref="ProductEventNames.{{constant}}"/> (ADR-0036).</summary>
            /// <remarks>
            /// Properties are identifiers only; personal data and data about health never go to analytics. The user, when the event has one,
            /// is the CIAM <c>sub</c>, turned into the analytics pseudonym by the sink. TODO: review the properties with the privacy rules of
            /// chapter 21 before merging.
            /// </remarks>
            /// <param name="sink">Destination of product events (PostHog, or the log when analytics is disabled).</param>
            public sealed class {{consumer}}(IProductEventSink sink) : IConsumer<{{contract.Name}}>
            {
                /// <summary>Maps one delivery of the integration event to a product event.</summary>
                /// <param name="context">The consumed message with its identifier, used for deduplication in PostHog.</param>
                /// <returns>A completed task.</returns>
                public Task Consume(ConsumeContext<{{contract.Name}}> context)
                {
                    var message = context.Message;
                    sink.Capture(new ProductEvent(
                        ProductEventNames.{{constant}},
                        {{(time is null ? "DateTimeOffset.UtcNow" : $"message.{time}")}},
                        context.MessageId,
                        {{(subject is null ? "Subject: null" : $"message.{subject}")}},
                        {{body}}));
                    return Task.CompletedTask;
                }
            }

            """;
    }

    /// <summary>Converts a PascalCase name to snake_case: <c>InvoiceIssued</c> → <c>invoice_issued</c>.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The snake_case form.</returns>
    public static string Snake(string name)
    {
        var text = new StringBuilder();
        foreach (var character in name)
        {
            if (char.IsUpper(character) && text.Length > 0)
            {
                text.Append('_');
            }

            text.Append(char.ToLowerInvariant(character));
        }

        return text.ToString();
    }

    private static string RemoveMember(string content, string marker)
    {
        var lines = TextEdits.Lines(content);
        var index = lines.FindIndex(line => line.Contains(marker, StringComparison.Ordinal));
        if (index < 0)
        {
            return content;
        }

        var start = index;
        while (start > 0 && lines[start - 1].TrimStart().StartsWith("///", StringComparison.Ordinal))
        {
            start--;
        }

        if (start > 0 && lines[start - 1].Length == 0)
        {
            start--;
        }
        else if (index + 1 < lines.Count && lines[index + 1].Length == 0)
        {
            index++;
        }

        lines.RemoveRange(start, index - start + 1);
        return TextEdits.Join(lines, content);
    }
}
