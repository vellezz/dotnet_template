using System.Net;
using System.Text;
using Refit;

namespace Example.Bff.Tests;

/// <summary>Builds Refit answers of a domain service as the generated client would return them.</summary>
internal static class Responses
{
    private static readonly RefitSettings Settings = new();

    public static IApiResponse<T> Success<T>(HttpStatusCode status, T content) =>
        new ApiResponse<T>(new HttpResponseMessage(status) { RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://service/v1/x") }, content, Settings);

    public static async Task<IApiResponse<T>> ErrorAsync<T>(HttpStatusCode status, string? problemJson)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://service/v1/x");
        var response = new HttpResponseMessage(status)
        {
            RequestMessage = request,
            Content = problemJson is null ? null : new StringContent(problemJson, Encoding.UTF8, "application/problem+json"),
        };
        var error = await ApiException.Create(request, HttpMethod.Get, response, Settings);
        return new ApiResponse<T>(response, default, Settings, error);
    }
}
