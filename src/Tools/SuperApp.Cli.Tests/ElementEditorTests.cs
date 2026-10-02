using SuperApp.Cli.LocalEnvironment;
using SuperApp.Cli.Repository;
using SuperApp.Cli.Scaffolding.Editors;

namespace SuperApp.Cli.Tests;

/// <summary>Editors of stage 4 on the real files: additions land in the right form and removals restore the files byte for byte.</summary>
public sealed class ElementEditorTests
{
    private static readonly RepositoryFiles Files = new(RepositoryRoot.Find(AppContext.BaseDirectory)
        ?? throw new InvalidOperationException("Tests must run inside the repository."));

    [Fact]
    public void Scope_constant_follows_the_style_of_the_file()
    {
        var knowledge = Files.ReadOrEmpty("src/Services/Knowledge/Knowledge.Application/KnowledgeScopes.cs");
        var template = Files.ReadOrEmpty($"{RepositoryFiles.TemplatesDirectory}/superapp-service/ServiceName.Application/ServiceNameScopes.cs");

        var literal = ScopesEditor.AddConstants(knowledge, "knowledge", ["note.write"]);
        var prefixed = ScopesEditor.AddConstants(template, "servicename", ["note.write"]);

        Assert.Contains("public const string NoteWrite = \"knowledge.note.write\";", literal, StringComparison.Ordinal);
        Assert.Contains("public const string NoteWrite = Prefix + \"note.write\";", prefixed, StringComparison.Ordinal);
        Assert.Equal(knowledge, ScopesEditor.RemoveConstant(literal, "knowledge.note.write"));
        Assert.Equal(template, ScopesEditor.RemoveConstant(prefixed, "note.write"));
    }

    [Fact]
    public void Repository_registration_round_trip()
    {
        var registration = Files.ReadOrEmpty(RegistrationEditor.Path("src/Services/Knowledge", "Knowledge"));

        var added = RegistrationEditor.AddRepository(registration, "Knowledge", "Notes", "Note");

        Assert.Contains("services.AddScoped<INoteRepository, NoteRepository>();", added, StringComparison.Ordinal);
        Assert.Contains("using Knowledge.Domain.Notes;", added, StringComparison.Ordinal);
        Assert.Equal(added, RegistrationEditor.AddRepository(added, "Knowledge", "Notes", "Note"));
        Assert.Equal(registration, RegistrationEditor.RemoveRepository(added, "Knowledge", "Notes", "Note", featureStillUsed: false));
    }

    [Fact]
    public void Flags_and_product_event_names_round_trip()
    {
        var flags = ElementCode.FlagsClass("Billing", "billing");
        var names = Files.ReadOrEmpty("src/Analytics/SuperApp.AnalyticsForwarder/Events/ProductEventNames.cs");

        var withFlag = ElementCode.AddFlag(flags, "billing_invoice_reminders", defaultValue: false);
        var withName = ElementCode.AddEventName(names, "BillingInvoiceIssued", "billing_invoice_issued", "InvoiceIssuedV1");

        Assert.Contains("public static readonly FeatureFlag InvoiceReminders = new(\"billing_invoice_reminders\", DefaultValue: false);", withFlag, StringComparison.Ordinal);
        Assert.Equal(flags, ElementCode.RemoveFlag(withFlag, "billing_invoice_reminders"));
        Assert.Equal(names, ElementCode.RemoveEventName(withName, "billing_invoice_issued"));
    }

    [Fact]
    public void Product_event_consumer_maps_time_user_and_identifiers_only()
    {
        var source = Files.ReadOrEmpty("src/Services/SleepDiary/SleepDiary.Contracts/SleepEntryRecordedV1.cs");

        var record = ContractRecord.Parse(source, "SleepEntryRecordedV1");
        var consumer = ElementCode.ProductEventConsumer("SleepDiary", record!, "SleepEntryRecordedConsumer", "SleepDiarySleepEntryRecorded");

        Assert.Contains(("Guid", "EntryId"), record!.Parameters);
        Assert.Contains("message.RecordedAt", consumer, StringComparison.Ordinal);
        Assert.Contains("message.UserId,", consumer, StringComparison.Ordinal);
        Assert.Contains("[\"entry_id\"] = message.EntryId.ToString(),", consumer, StringComparison.Ordinal);
        Assert.DoesNotContain("Quality", consumer, StringComparison.Ordinal);
    }

    [Fact]
    public void Local_endpoints_come_from_the_compose_file()
    {
        var endpoints = new LocalEndpoints(RepositoryScanner.Scan(Files.Root));

        Assert.Equal("http://localhost:8081", endpoints.Keycloak);
        Assert.Equal("https://localhost:5002", endpoints.GatewayMobile);
        Assert.Contains(("example", "http://localhost:5120"), endpoints.Bffs);
        Assert.Contains(endpoints.Probes(), probe => probe.Url == "http://localhost:5101/health/live");
    }
}
