using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Darbee.Gateway.Domain.Ports;

namespace Darbee.Gateway.Infrastructure.Embedding;

public sealed class OpenAiCompatibleEmbeddingClient : IEmbeddingClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _modelId;
    private readonly string? _apiKey;

    public int Dimension { get; }

    public OpenAiCompatibleEmbeddingClient(HttpClient http, string baseUrl, string modelId, int expectedDimension, string? apiKey = null)
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
        _modelId = modelId;
        _apiKey = apiKey;
        Dimension = expectedDimension;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var batch = await EmbedBatchAsync(new[] { text }, cancellationToken);
        return batch[0];
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        var url = $"{_baseUrl}/embeddings";
        var body = new EmbeddingRequest(_modelId, texts.Count == 1 ? (object)texts[0] : texts);
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body)
        };
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        }
        var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var parsed = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Embedding response was null.");
        var vectors = parsed.Data.Select(d => d.Embedding).ToArray();
        foreach (var v in vectors)
        {
            if (v.Length != Dimension)
            {
                throw new InvalidOperationException(
                    $"Embedding dimension mismatch: configured {Dimension}, received {v.Length}. " +
                    $"Reload the embedding model or update appsettings.");
            }
        }
        return vectors;
    }

    private sealed record EmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] object Input);

    private sealed record EmbeddingResponse(
        [property: JsonPropertyName("data")] List<EmbeddingData> Data);

    private sealed record EmbeddingData(
        [property: JsonPropertyName("embedding")] float[] Embedding);
}
