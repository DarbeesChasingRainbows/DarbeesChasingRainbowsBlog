using Darbee.Gateway.Domain.Models;

namespace Darbee.Gateway.Domain.Ports;

/// <summary>
/// SRP: Schema lifecycle is a separate concern from document CRUD.
/// Consumers: Program.cs startup, integration tests.
/// </summary>
public interface ISchemaManager
{
    Task EnsureSchemaAsync(CancellationToken ct = default);
    Task EnsureSchemaIfNeededAsync(CancellationToken ct = default);
    Task EnsureVectorIndexAsync(string collection, CancellationToken ct = default);
    Task<bool> IsVectorIndexReadyAsync(string collection, CancellationToken ct = default);
    Task<List<string>> ListCollectionsAsync(CancellationToken ct = default);
}
