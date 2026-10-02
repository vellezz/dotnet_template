using System.Net;
using System.Text;
using SuperApp.Framework.Infrastructure.Http.Downstream;
using SuperApp.Framework.Infrastructure.Http.UserContext;
using Example.Bff.Clients.Knowledge;
using Example.Bff.Clients.SleepDiary;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Example.Bff.Tests;

/// <summary>
/// The generated clients registered with <c>AddDownstreamApi</c> are resolvable and talk the contract's language: ISO dates in the path,
/// the user's token in <c>Authorization</c>, polymorphic bodies deserialized by discriminator.
/// </summary>
public sealed class DownstreamClientTests
{
    [Fact]
    public async Task Date_in_path_is_iso_and_the_user_token_is_forwarded()
    {
        var recorder = new RecordingHandler("""{"id":"3f2c8a51-0d7e-4c0b-9a57-6c1f1f7b2e10","date":"2026-10-01","bedTime":"2026-09-30T23:00:00","wakeTime":"2026-10-01T07:00:00","sleepLatencyMinutes":15,"awakenings":1,"quality":4,"notes":null,"timeInBedMinutes":480,"sleepMinutes":465}""");
        await using var provider = Build<ISleepDiaryApi>("SleepDiary", recorder);

        var response = await provider.GetRequiredService<ISleepDiaryApi>()
            .SleepEntriesGetAsync(new DateOnly(2026, 10, 1), TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessful);
        Assert.Equal("/v1/entries/2026-10-01", recorder.Path);
        Assert.Equal("Bearer user-jwt", recorder.Authorization);
        Assert.Equal(new DateTime(2026, 9, 30, 23, 0, 0), response.Content!.BedTime);
    }

    [Fact]
    public async Task Polymorphic_content_blocks_are_deserialized_by_discriminator()
    {
        var recorder = new RecordingHandler("""{"id":"3f2c8a51-0d7e-4c0b-9a57-6c1f1f7b2e10","type":"Article","title":"Higiena snu","status":"Published","content":[{"type":"heading","level":2,"text":"Rytm"},{"type":"paragraph","text":[{"text":"Stałe pory snu."}]}]}""");
        await using var provider = Build<IKnowledgeApi>("Knowledge", recorder);

        var response = await provider.GetRequiredService<IKnowledgeApi>()
            .MaterialsGetAsync(Guid.Parse("3f2c8a51-0d7e-4c0b-9a57-6c1f1f7b2e10"), TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessful, response.Error?.Message);
        Assert.Collection(
            response.Content!.Content,
            block => Assert.Equal(2, Assert.IsType<ContentBlockDtoHeadingBlockDto>(block).Level),
            block => Assert.IsType<ContentBlockDtoParagraphBlockDto>(block));
    }

    private static ServiceProvider Build<TClient>(string name, RecordingHandler recorder)
        where TClient : class
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"Downstream:{name}:BaseAddress"] = "http://service" })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDownstreamApi<TClient>(configuration, name).AddUserTokenForwarding().ConfigurePrimaryHttpMessageHandler(() => recorder);
        var provider = services.BuildServiceProvider();

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer user-jwt";
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = context;
        return provider;
    }

    private sealed class RecordingHandler(string body) : HttpMessageHandler
    {
        public string? Path { get; private set; }

        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri?.AbsolutePath;
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
