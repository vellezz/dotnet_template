namespace SuperApp.AnalyticsForwarder.Events;

/// <summary>
/// Catalogue of backend product analytics events: one constant per event, named <c>{service}_{object}_{past-tense verb}</c> in snake case
/// (ADR-0036).
/// </summary>
/// <remarks>
/// <para>
/// The names are a contract with everyone who builds insights, funnels and cohorts in PostHog: renaming an event breaks them silently. Add
/// new events here, never rename existing ones; when the meaning changes, add a new name and stop sending the old one. Events captured by the
/// web and mobile clients follow the same convention, without a service prefix when they describe the UI (<c>material_opened</c>).
/// </para>
/// <para>Each constant documents the integration event it comes from and the properties it carries.</para>
/// </remarks>
public static class ProductEventNames
{
    /// <summary>
    /// <c>knowledge_material_published</c>, from <c>MaterialPublishedV1</c>; system event. Properties: <c>material_id</c>, <c>material_type</c>.
    /// </summary>
    public const string KnowledgeMaterialPublished = "knowledge_material_published";

    /// <summary><c>knowledge_material_archived</c>, from <c>MaterialArchivedV1</c>; system event. Properties: <c>material_id</c>.</summary>
    public const string KnowledgeMaterialArchived = "knowledge_material_archived";

    /// <summary><c>knowledge_collection_archived</c>, from <c>CollectionArchivedV1</c>; system event. Properties: <c>collection_id</c>.</summary>
    public const string KnowledgeCollectionArchived = "knowledge_collection_archived";

    /// <summary>
    /// <c>sleepdiary_entry_recorded</c>, from <c>SleepEntryRecordedV1</c>; event of the user who recorded the entry. No properties beyond the
    /// common ones: the entry's date, duration and quality are health data and are not sent.
    /// </summary>
    public const string SleepDiaryEntryRecorded = "sleepdiary_entry_recorded";
}
