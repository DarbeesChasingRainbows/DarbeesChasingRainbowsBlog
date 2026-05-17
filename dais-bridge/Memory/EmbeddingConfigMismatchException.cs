using Darbee.Gateway.Memory.Models;

namespace Darbee.Gateway.Memory;

public sealed class EmbeddingConfigMismatchException : InvalidOperationException
{
    public EmbeddingConfig Previous { get; }
    public EmbeddingConfig Current { get; }

    public EmbeddingConfigMismatchException(EmbeddingConfig previous, EmbeddingConfig current)
        : base(BuildMessage(previous, current))
    {
        Previous = previous;
        Current = current;
    }

    private static string BuildMessage(EmbeddingConfig previous, EmbeddingConfig current) =>
        $$"""
        Embedding config mismatch.
          In Arango: { model: {{previous.Model}}, dimension: {{previous.Dimension}} }
          Bridge:    { model: {{current.Model}}, dimension: {{current.Dimension}} }
        Existing vector indexes and embeddings are incompatible with the configured model.
        Remediation:
          curl -X POST http://localhost:5000/api/admin/migrate-embeddings \
               -H 'content-type: application/json' \
               -d '{ "confirm": "preserve-and-reembed" }'
        """;
}
