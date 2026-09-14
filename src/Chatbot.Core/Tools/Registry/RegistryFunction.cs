using System.Text.Json;
using Chatbot.Core.Actions;
using Chatbot.Core.Chat;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Chatbot.Core.Tools.Registry;

/// <summary>
/// Fase 4's runtime-loader i én klasse: en <see cref="AIFunction"/>, der ikke er bygget af en C#-metode
/// med reflection, men af en <see cref="ToolDefinition"/>. Navn, beskrivelse og JSON-skema kommer
/// direkte fra rækken; <c>UseFunctionInvocation</c> i chat-pipelinen ser ingen forskel.
///
/// Kaldet fra modellen går én af to veje:
/// <list type="bullet">
/// <item>Læse-tools udføres straks af handleren for tool-typen.</item>
/// <item>Tools med <c>RequiresConfirmation</c> udføres ALDRIG her. De valideres mod skemaets
/// <c>required</c>, gemmes som en <see cref="PendingAction"/>, og modellen bedes vise opsummeringen.
/// Selve kaldet sker først i <see cref="DynamicActionExecutor"/>, når brugeren har sagt ja —
/// præcis som fase 3, bare uden at hvert tool skal skrive flowet selv.</item>
/// </list>
/// </summary>
public sealed class RegistryFunction : AIFunction
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ToolDefinition _tool;
    private readonly IToolHandler _handler;
    private readonly IPendingActionStore _pending;
    private readonly TurnContext _turn;
    private readonly ILogger _logger;

    public RegistryFunction(
        ToolDefinition tool,
        IToolHandler handler,
        IPendingActionStore pending,
        TurnContext turn,
        ILogger logger)
    {
        _tool = tool;
        _handler = handler;
        _pending = pending;
        _turn = turn;
        _logger = logger;
    }

    public ToolDefinition Definition => _tool;

    public override string Name => _tool.Name;

    public override string Description => _tool.Description;

    public override JsonElement JsonSchema => _tool.ParametersSchema;

    public override JsonSerializerOptions JsonSerializerOptions => JsonOptions;

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var args = ToJson(arguments, _tool.ParametersSchema);

        if (!_tool.RequiresConfirmation)
        {
            _logger.LogInformation(
                "Tool {Tool} ({HandlerType}) kaldes i samtale {ConversationId}.",
                _tool.Name, _tool.HandlerType, _turn.ConversationId);
            return await _handler.InvokeAsync(_tool, args, cancellationToken);
        }

        var missing = MissingRequired(_tool.ParametersSchema, args);
        if (missing.Count > 0)
        {
            return $"Kan ikke forberede handlingen — mangler: {string.Join(", ", missing)}. " +
                   "Spørg brugeren om de manglende oplysninger. Gæt ikke.";
        }

        var summary = string.IsNullOrWhiteSpace(_tool.SummaryTemplate)
            ? $"{_tool.Name} med {args.GetRawText()}"
            : TemplateRenderer.Render(_tool.SummaryTemplate, TemplateRenderer.Flatten(args));

        await _pending.SetAsync(
            _turn.ConversationId,
            new PendingAction(_tool.Name, summary, args.GetRawText(), DateTimeOffset.UtcNow),
            cancellationToken);

        _logger.LogInformation("Handling forberedt for samtale {ConversationId}: {Summary}", _turn.ConversationId, summary);

        return $"Handlingen er forberedt, men IKKE udført: {summary}. " +
               "Vis brugeren opsummeringen og spørg, om oplysningerne er korrekte, og om du skal udføre den. " +
               "Fortæl at brugeren kan svare 'ja' for at bekræfte, 'nej' for at annullere, eller rette oplysningerne.";
    }

    /// <summary>
    /// Modellens argumenter som ét JSON-objekt. Værdierne kommer typisk allerede som JsonElement
    /// fra pipelinen; alt andet serialiseres. Argumenter uden for skemaets <c>properties</c> fjernes —
    /// modellen skal ikke kunne smugle ekstra felter med ind i en HTTP-body.
    /// </summary>
    internal static JsonElement ToJson(AIFunctionArguments arguments, JsonElement schema)
    {
        HashSet<string>? allowed = null;
        if (schema.ValueKind == JsonValueKind.Object
            && schema.TryGetProperty("properties", out var properties)
            && properties.ValueKind == JsonValueKind.Object)
        {
            allowed = properties.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        }

        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in arguments)
        {
            if (allowed is null || allowed.Contains(key))
            {
                dict[key] = value;
            }
        }

        return JsonSerializer.SerializeToElement(dict, JsonOptions);
    }

    /// <summary>Påkrævede felter (skemaets <c>required</c>), der mangler eller er tomme. En tom streng er ikke et svar.</summary>
    internal static IReadOnlyList<string> MissingRequired(JsonElement schema, JsonElement args)
    {
        var missing = new List<string>();
        if (schema.ValueKind != JsonValueKind.Object
            || !schema.TryGetProperty("required", out var required)
            || required.ValueKind != JsonValueKind.Array)
        {
            return missing;
        }

        foreach (var item in required.EnumerateArray())
        {
            var name = item.GetString();
            if (name is null)
            {
                continue;
            }

            var present = args.ValueKind == JsonValueKind.Object
                          && args.TryGetProperty(name, out var value)
                          && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                          && !(value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()));

            if (!present)
            {
                missing.Add(name);
            }
        }

        return missing;
    }
}
