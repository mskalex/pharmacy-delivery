using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PharmacyDelivery.Common.Serialization;

/// <summary>
/// Single source of JSON conventions for every channel: HTTP API, SignalR, MassTransit, Redis, seed files,
/// the courier simulator and tests. Two flavours exist:
/// <list type="bullet">
/// <item><see cref="Strict"/> — HTTP input: unknown members are rejected (a typo yields 400 instead of being ignored).</item>
/// <item><see cref="Tolerant"/> — bus contracts and cache payloads: unknown members are skipped (tolerant reader,
/// required for additive contract versioning).</item>
/// </list>
/// </summary>
public static class PharmacyDeliveryJsonOptions
{
    /// <summary>Maximum object nesting accepted by the deserializer.</summary>
    public const int MaxDepth = 32;

    public static JsonSerializerOptions Strict { get; } = Create(strict: true);

    public static JsonSerializerOptions Tolerant { get; } = Create(strict: false);

    /// <summary>Creates a new, mutable options instance with project conventions.</summary>
    public static JsonSerializerOptions Create(bool strict)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Apply(options, strict);
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    /// <summary>Applies project conventions to an options instance owned by a framework (MVC, SignalR, MassTransit).</summary>
    public static void Apply(JsonSerializerOptions options, bool strict)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        options.PropertyNameCaseInsensitive = true;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.NumberHandling = JsonNumberHandling.Strict;
        options.MaxDepth = MaxDepth;
        options.UnmappedMemberHandling = strict ? JsonUnmappedMemberHandling.Disallow : JsonUnmappedMemberHandling.Skip;
        options.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

        if (!options.Converters.OfType<JsonStringEnumConverter>().Any())
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
    }
}
