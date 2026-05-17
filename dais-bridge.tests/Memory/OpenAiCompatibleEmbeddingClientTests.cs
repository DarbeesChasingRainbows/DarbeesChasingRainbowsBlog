using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Darbee.Gateway.Memory;

namespace Darbee.Gateway.Tests.Memory;

public class OpenAiCompatibleEmbeddingClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    [Fact]
    public async Task EmbedAsync_PostsExpectedShapeAndParsesFloatArray()
    {
        var responseJson = "{\"data\":[{\"embedding\":[0.1,0.2,0.3,0.4]}]}";
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
        });
        using var http = new HttpClient(handler);
        var client = new OpenAiCompatibleEmbeddingClient(http, "http://localhost:1234/v1", "nomic-embed-text-v1.5", expectedDimension: 4);

        var result = await client.EmbedAsync("hello world");

        Assert.Equal(new float[] { 0.1f, 0.2f, 0.3f, 0.4f }, result);
        Assert.Single(handler.Requests);
        var sent = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.EndsWith("/v1/embeddings", sent.RequestUri!.ToString());
        var bodyJson = await sent.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(bodyJson);
        Assert.Equal("nomic-embed-text-v1.5", doc.RootElement.GetProperty("model").GetString());
        Assert.Equal("hello world", doc.RootElement.GetProperty("input").GetString());
    }

    [Fact]
    public async Task EmbedAsync_ThrowsOnDimensionMismatch()
    {
        var responseJson = "{\"data\":[{\"embedding\":[0.1,0.2]}]}";
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
        });
        using var http = new HttpClient(handler);
        var client = new OpenAiCompatibleEmbeddingClient(http, "http://localhost:1234/v1", "nomic-embed-text-v1.5", expectedDimension: 4);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.EmbedAsync("x"));
    }

    [Fact]
    public async Task EmbedBatchAsync_SendsArrayInputAndReturnsEachVector()
    {
        var responseJson = "{\"data\":[{\"embedding\":[0.1,0.2]},{\"embedding\":[0.3,0.4]}]}";
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
        });
        using var http = new HttpClient(handler);
        var client = new OpenAiCompatibleEmbeddingClient(http, "http://localhost:1234/v1", "test-model", expectedDimension: 2);

        var result = await client.EmbedBatchAsync(new[] { "a", "b" });

        Assert.Equal(2, result.Count);
        Assert.Equal(new float[] { 0.1f, 0.2f }, result[0]);
        Assert.Equal(new float[] { 0.3f, 0.4f }, result[1]);
        var bodyJson = await handler.Requests[0].Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(bodyJson);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.GetProperty("input").ValueKind);
    }
}
