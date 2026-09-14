using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Chatbot.Core.Rag;

/// <summary>
/// Konfiguration af RAG-delen. Bindes fra sektionen "Rag" i appsettings.json.
/// Chunk-størrelse og antal hits er bevidst konfiguration og ikke konstanter: det er netop
/// de knapper, evalueringssættet skal bruges til at skrue på.
/// </summary>
public sealed class RagOptions
{
    public const string SectionName = "Rag";

    /// <summary>Mappen med dokumentation (Markdown/tekst). En relativ sti er relativ til arbejdsmappen.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Rag:DocumentsPath mangler — mappen med dokumentation.")]
    public string DocumentsPath { get; set; } = "data/dokumentation";

    /// <summary>
    /// Maksimal chunk-længde i tegn. ~4 tegn pr. token, så 2000 tegn ≈ 500 tokens,
    /// hvilket er projektplanens udgangspunkt.
    /// </summary>
    [Range(200, 20_000)]
    public int ChunkSize { get; set; } = 2000;

    /// <summary>Overlap mellem to på hinanden følgende chunks i tegn, så en sætning på grænsen ikke går tabt.</summary>
    [Range(0, 5_000)]
    public int ChunkOverlap { get; set; } = 200;

    /// <summary>Hvor mange chunks søgningen giver modellen. 3-5 er projektplanens anbefaling.</summary>
    [Range(1, 20)]
    public int TopK { get; set; } = 4;

    /// <summary>
    /// Hits under denne lighed kasseres. 0 = behold alt. Sættes først, når evalueringssættet
    /// viser, hvor grænsen mellem relevant og støj ligger for den valgte embedding-model.
    /// </summary>
    [Range(0.0, 1.0)]
    public double MinScore { get; set; } = 0.0;

    [Required]
    [ValidateObjectMembers]
    public EmbeddingOptions Embedding { get; set; } = new();

    [Required]
    [ValidateObjectMembers]
    public VectorStoreOptions VectorStore { get; set; } = new();
}

public sealed class EmbeddingOptions
{
    /// <summary>Embedding-modellen i Ollama. Skiftes den, skal indekset genopbygges — vektorer er ikke kompatible på tværs.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Rag:Embedding:Model mangler — f.eks. nomic-embed-text.")]
    public string Model { get; set; } = "nomic-embed-text";

    /// <summary>
    /// Vektorens længde. nomic-embed-text giver 768. Tabellens kolonne oprettes med denne
    /// dimension, og ingestion stopper med en tydelig fejl, hvis modellen giver noget andet.
    /// </summary>
    [Range(1, 8192)]
    public int Dimensions { get; set; } = 768;

    /// <summary>Hvor mange chunks der embeddes pr. kald til Ollama.</summary>
    [Range(1, 256)]
    public int BatchSize { get; set; } = 16;
}

public sealed class VectorStoreOptions
{
    /// <summary>Npgsql-forbindelsesstreng til Postgres med pgvector-udvidelsen (se docker-compose.yml).</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Rag:VectorStore:ConnectionString mangler.")]
    public string ConnectionString { get; set; } = "Host=127.0.0.1;Port=5432;Database=chatbot;Username=chatbot;Password=chatbot";

    /// <summary>Tabelnavn. Kan skiftes, hvis flere indekser (f.eks. pr. embedding-model) skal leve side om side.</summary>
    [Required(AllowEmptyStrings = false)]
    [RegularExpression("^[a-z_][a-z0-9_]*$", ErrorMessage = "Rag:VectorStore:TableName må kun indeholde små bogstaver, tal og underscore.")]
    public string TableName { get; set; } = "document_chunks";
}
