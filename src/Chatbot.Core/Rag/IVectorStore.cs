namespace Chatbot.Core.Rag;

/// <summary>
/// Adgangen til vektor-databasen. Implementeres mod pgvector i fase 2, men interfacet
/// kender ikke til Postgres — så kan databasen skiftes (f.eks. til Qdrant) uden at røre
/// ingestion, søgning eller tests.
/// </summary>
public interface IVectorStore
{
    /// <summary>Opretter tabel og indeks, hvis de mangler. Idempotent — kan kaldes ved hver opstart.</summary>
    Task EnsureCreatedAsync(CancellationToken cancellationToken = default);

    /// <summary>Sletter alle chunks. Bruges af ingestion-jobbet, før indekset genopbygges.</summary>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>Gemmer chunks. Findes id'et allerede, overskrives rækken.</summary>
    Task UpsertAsync(IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken = default);

    /// <summary>Finder de <paramref name="top"/> chunks, der ligger tættest på spørgsmålets embedding.</summary>
    Task<IReadOnlyList<SearchHit>> SearchAsync(ReadOnlyMemory<float> queryEmbedding, int top, CancellationToken cancellationToken = default);

    /// <summary>Antal chunks i indekset. Bruges til logning og sundhedstjek.</summary>
    Task<long> CountAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Ét stykke af et dokument med den metadata, botten skal bruge for at kunne henvise til kilden.
/// </summary>
/// <param name="Id">Stabilt id: kilde + løbenummer, så en genkørsel overskriver i stedet for at duplikere.</param>
/// <param name="Source">Filnavn relativt til dokumentmappen, f.eks. "personalehaandbog.md".</param>
/// <param name="Heading">Den nærmeste overskrift over teksten — eller dokumentets titel.</param>
/// <param name="ChunkIndex">Løbenummer i dokumentet (0-baseret).</param>
/// <param name="Content">Selve teksten, der blev embeddet.</param>
/// <param name="Embedding">Vektoren. Tom, indtil ingestion har genereret den.</param>
public sealed record DocumentChunk(
    string Id,
    string Source,
    string Heading,
    int ChunkIndex,
    string Content,
    ReadOnlyMemory<float> Embedding);

/// <param name="Chunk">Den fundne chunk (embedding udelades af hensyn til båndbredde).</param>
/// <param name="Score">Lighed i intervallet 0-1, hvor 1 er identisk. Cosinus-lighed for pgvector.</param>
public sealed record SearchHit(DocumentChunk Chunk, double Score);
