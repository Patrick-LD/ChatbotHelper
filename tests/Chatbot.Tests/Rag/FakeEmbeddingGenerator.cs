using Microsoft.Extensions.AI;

namespace Chatbot.Tests.Rag;

/// <summary>
/// Deterministisk erstatning for nomic-embed-text: vektoren afledes af teksten, så samme tekst
/// altid giver samme vektor, og tekster med fælles ord ligger tættere på hinanden end tekster uden.
/// Godt nok til at teste pipelinen — ikke til at teste søgekvalitet.
/// </summary>
internal sealed class FakeEmbeddingGenerator(int dimensions = 8) : IEmbeddingGenerator<string, Embedding<float>>
{
    public List<string> Inputs { get; } = [];

    public int CallCount { get; private set; }

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        var list = values.ToList();
        Inputs.AddRange(list);
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(list.Select(v => new Embedding<float>(Embed(v)))));
    }

    /// <summary>Bag-of-words hashet ned i et fast antal dimensioner og normaliseret til længde 1.</summary>
    public float[] Embed(string text)
    {
        var vector = new float[dimensions];
        foreach (var word in text.ToLowerInvariant().Split([' ', '\n', ',', '.', '!', '?'], StringSplitOptions.RemoveEmptyEntries))
        {
            var bucket = Math.Abs(StableHash(word)) % dimensions;
            vector[bucket] += 1;
        }

        var norm = MathF.Sqrt(vector.Sum(x => x * x));
        return norm == 0 ? vector : vector.Select(x => x / norm).ToArray();
    }

    private static int StableHash(string s)
    {
        unchecked
        {
            var hash = 23;
            foreach (var c in s)
            {
                hash = (hash * 31) + c;
            }

            return hash;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
