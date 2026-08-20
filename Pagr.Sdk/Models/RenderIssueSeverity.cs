using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pagr.Sdk.Models;

/// <summary>
/// How much a <see cref="RenderIssue"/> blocks rendering.
/// </summary>
/// <remarks>
/// Ordered by severity: production blocks any issue at or above <see cref="Warning"/>;
/// test/preview blocks only <see cref="Error"/>; a document is production-valid only when
/// all its issues are <see cref="Information"/>.
/// </remarks>
[JsonConverter(typeof(RenderIssueSeverityConverter))]
public enum RenderIssueSeverity
{
    /// <summary>Informational; never blocks rendering.</summary>
    Information,

    /// <summary>Blocks production rendering but not test/preview.</summary>
    Warning,

    /// <summary>Blocks rendering everywhere.</summary>
    Error,
}

/// <summary>Extension methods for <see cref="RenderIssueSeverity"/>.</summary>
public static class RenderIssueSeverityExtensions
{
    /// <summary>
    /// <see langword="true"/> when this severity is <paramref name="other"/> or more severe.
    /// C# enums already support <c>&gt;=</c> natively, but this mirrors the ordered
    /// comparison the other Pagr SDKs expose for discoverability.
    /// </summary>
    public static bool IsAtLeast(this RenderIssueSeverity severity, RenderIssueSeverity other) => severity >= other;

    /// <summary>
    /// <see langword="true"/> when an issue of this severity blocks a production render
    /// (i.e. it is <see cref="RenderIssueSeverity.Warning"/> or <see cref="RenderIssueSeverity.Error"/>).
    /// </summary>
    public static bool IsBlockingProduction(this RenderIssueSeverity severity) =>
        severity.IsAtLeast(RenderIssueSeverity.Warning);
}

/// <summary>
/// Parses the API's string value case-insensitively; unknown or <see langword="null"/>
/// values fail closed to <see cref="RenderIssueSeverity.Error"/>.
/// </summary>
internal sealed class RenderIssueSeverityConverter : JsonConverter<RenderIssueSeverity>
{
    public override bool HandleNull => true;

    public override RenderIssueSeverity Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String &&
            Enum.TryParse<RenderIssueSeverity>(reader.GetString(), ignoreCase: true, out var value) &&
            Enum.IsDefined(value))
        {
            return value;
        }
        return RenderIssueSeverity.Error;
    }

    public override void Write(Utf8JsonWriter writer, RenderIssueSeverity value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
