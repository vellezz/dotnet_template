namespace Knowledge.IntegrationTests.Infrastructure;

/// <summary>
/// Name of the test collection shared by all test classes that send requests through the pipeline. They change the shared
/// <see cref="ServiceFixture.CurrentUser"/> and read shared cache entries, so they must not run in parallel with each other.
/// </summary>
public static class PipelineCollection
{
    /// <summary>The collection name used with <c>[Collection(PipelineCollection.Name)]</c>.</summary>
    public const string Name = "Knowledge pipeline";
}
