using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pagr.Sdk.Models;

/// <summary>
/// Render outcome of a job or webhook callback, mirroring the synchronous envelope's status
/// vocabulary.
/// </summary>
/// <remarks>
/// <see langword="null"/> (not this enum) is used while a job is still pending; once decided
/// it is one of these. <see cref="Unknown"/> is a client-side fail-open fallback for an
/// unrecognised server value, so new outcomes never crash an older client.
/// </remarks>
[JsonConverter(typeof(RenderOutcomeConverter))]
public enum RenderOutcome
{
    /// <summary>Every requested document rendered.</summary>
    Ok,

    /// <summary>Some documents rendered; others were blocked or failed.</summary>
    Partial,

    /// <summary>No document rendered.</summary>
    Failed,

    /// <summary>The render stopped because the organisation is out of credit.</summary>
    InsufficientCredit,

    /// <summary>The outcome was missing or not recognised by this SDK version.</summary>
    Unknown,
}

/// <summary>
/// Parses the API's string value case-insensitively; unknown values fail open to
/// <see cref="RenderOutcome.Unknown"/>.
/// </summary>
internal sealed class RenderOutcomeConverter : JsonConverter<RenderOutcome>
{
    public override bool HandleNull => true;

    public override RenderOutcome Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String &&
            Enum.TryParse<RenderOutcome>(NormalizeToken(reader.GetString()), ignoreCase: true, out var value) &&
            Enum.IsDefined(value))
        {
            return value;
        }
        return RenderOutcome.Unknown;
    }

    // The wire value is snake_case ("insufficient_credit"); the enum member is PascalCase.
    private static string? NormalizeToken(string? value) =>
        value?.Replace("_", string.Empty);

    public override void Write(Utf8JsonWriter writer, RenderOutcome value, JsonSerializerOptions options)
        => writer.WriteStringValue(value switch
        {
            RenderOutcome.InsufficientCredit => "insufficient_credit",
            _ => value.ToString().ToLowerInvariant(),
        });
}
