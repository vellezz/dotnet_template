using Knowledge.Application.Content;
using Knowledge.Application.Content.Blocks;
using Knowledge.Application.Features.Materials.CreateMaterial;
using Knowledge.Application.Features.Materials.PublishMaterial;
using Knowledge.Application.Features.Materials.ReplaceMaterialContent;
using Knowledge.Domain.Materials;
using SuperApp.Framework.Testing;

namespace Knowledge.IntegrationTests.Infrastructure;

/// <summary>Shortcuts for common steps of the integration scenarios.</summary>
public static class ServiceFixtureExtensions
{
    /// <summary>Replaces the scopes of the current test user.</summary>
    public static void ActWith(this ServiceFixture fixture, params string[] scopes)
    {
        fixture.CurrentUser.Scopes.Clear();
        fixture.CurrentUser.Scopes.UnionWith(scopes);
    }

    /// <summary>Creates an article with one paragraph and publishes it; the caller must hold the catalog write scope.</summary>
    public static async Task<Guid> CreatePublishedArticleAsync(this ServiceFixture fixture, string title)
    {
        var created = ResultAssert.Success(await fixture.SendAsync(new CreateMaterial(MaterialType.Article, title, null, null, null)));
        Assert.True((await fixture.SendAsync(new ReplaceMaterialContent(created, [new ParagraphBlockDto([new SpanDto("Treść")])]))).IsSuccess);
        Assert.True((await fixture.SendAsync(new PublishMaterial(created))).IsSuccess);
        return created;
    }
}
