using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pagr.Sdk.Models;

/// <summary>
/// Category of a <see cref="RenderIssue"/>, mirroring the API's <c>RenderIssueType</c>.
/// </summary>
/// <remarks>
/// Unknown values parse to <see cref="Unknown"/> rather than failing, so new server
/// behaviour never crashes an older client.
/// </remarks>
[JsonConverter(typeof(RenderIssueTypeConverter))]
public enum RenderIssueType
{
    /// <summary>The issue category was missing or not recognised by this SDK version.</summary>
    Unknown,

    /// <summary>The submitted document data is not valid JSON.</summary>
    InvalidJson,

    /// <summary>The document data does not match the template's schema.</summary>
    SchemaInvalid,

    /// <summary>The document data contains content flagged as dangerous.</summary>
    DangerousContent,

    /// <summary>A template binding has no matching value in the document data.</summary>
    MissingBinding,

    /// <summary>An image referenced by the template could not be resolved.</summary>
    UnresolvedImage,

    /// <summary>A font referenced by the template could not be resolved.</summary>
    UnresolvedFont,

    /// <summary>A colour value in the template is invalid.</summary>
    InvalidColor,

    /// <summary>A conditional expression in the template is invalid.</summary>
    InvalidCondition,

    /// <summary>A repeater's data source is not an enumerable value.</summary>
    DataSourceNotEnumerable,

    /// <summary>A chart's configuration is invalid.</summary>
    InvalidChartConfig,

    /// <summary>A page background definition is invalid.</summary>
    InvalidPageBackground,

    /// <summary>A binding evaluated but failed while rendering.</summary>
    BindingFailedAtRender,

    /// <summary>The render exceeded its time budget.</summary>
    RenderTimeout,

    /// <summary>The render completed but the layout degraded (e.g. overflow).</summary>
    RenderLayoutDegraded,

    /// <summary>The template layout is invalid.</summary>
    InvalidLayout,

    /// <summary>A value could not be formatted as requested.</summary>
    UnformattedValue,
}

/// <summary>
/// Parses the API's string value case-insensitively; unknown or <see langword="null"/>
/// values fail open to <see cref="RenderIssueType.Unknown"/>.
/// </summary>
internal sealed class RenderIssueTypeConverter : JsonConverter<RenderIssueType>
{
    public override bool HandleNull => true;

    public override RenderIssueType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String &&
            Enum.TryParse<RenderIssueType>(reader.GetString(), ignoreCase: true, out var value) &&
            Enum.IsDefined(value))
        {
            return value;
        }
        return RenderIssueType.Unknown;
    }

    public override void Write(Utf8JsonWriter writer, RenderIssueType value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
