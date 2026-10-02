using System.Net;
using SuperApp.Framework.Infrastructure.Api;
using Microsoft.AspNetCore.Mvc;

namespace Example.Bff.Tests;

/// <summary>The BFF relays the answers of domain services unchanged: status, error body with its <c>code</c>, success body.</summary>
public sealed class DownstreamResponseTests
{
    private readonly TestController _controller = new();

    [Fact]
    public async Task Error_answer_is_relayed_with_status_body_and_content_type()
    {
        const string problem = """{"title":"Materiał nie istnieje.","status":404,"code":"knowledge.material.not_found","traceId":"abc"}""";

        var result = _controller.ToActionResult(await Responses.ErrorAsync<string>(HttpStatusCode.NotFound, problem));

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(404, content.StatusCode);
        Assert.Equal(problem, content.Content);
        Assert.StartsWith("application/problem+json", content.ContentType, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Error_answer_without_body_keeps_only_the_status()
    {
        var result = _controller.ToActionResult(await Responses.ErrorAsync<string>(HttpStatusCode.Unauthorized, problemJson: null));

        Assert.Equal(401, Assert.IsType<StatusCodeResult>(result).StatusCode);
    }

    [Fact]
    public void Success_is_relayed_with_the_service_status_or_replaced_by_the_caller()
    {
        var created = _controller.ToActionResult(Responses.Success(HttpStatusCode.Created, "id"));
        var reshaped = _controller.ToActionResult(Responses.Success(HttpStatusCode.OK, "data"), value => new OkObjectResult(value.ToUpperInvariant()));

        var createdObject = Assert.IsType<ObjectResult>(created);
        Assert.Equal(201, createdObject.StatusCode);
        Assert.Equal("id", createdObject.Value);
        Assert.Equal("DATA", Assert.IsType<OkObjectResult>(reshaped).Value);
    }

    private sealed class TestController : ControllerBase;
}
