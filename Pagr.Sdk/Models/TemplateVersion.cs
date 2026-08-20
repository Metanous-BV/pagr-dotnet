using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pagr.Sdk.Models;

/// <summary>
/// A single version of a template.
/// </summary>
/// <remarks>
/// <see cref="TemplateJson"/> is the template DSL as a raw JSON string (there is no typed
/// model for it yet), and <see cref="Translations"/> likewise, or <see langword="null"/> when
/// the version has no translations. <see cref="SampleData"/> is the one field the SDK parses:
/// it comes back as a <see cref="JsonElement"/> that matches the version's bindings and can be
/// passed straight to <c>RenderAsync</c>/<c>ValidateAsync</c> as a starting point for your own
/// document data.
/// </remarks>
public sealed class TemplateVersion
{
    /// <summary>The unique identifier of the template version.</summary>
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>The version number.</summary>
    [JsonPropertyName("versionNumber")]
    public int VersionNumber { get; init; }

    /// <summary>The raw template definition as a JSON string.</summary>
    [JsonPropertyName("templateJson")]
    public string TemplateJson { get; init; } = string.Empty;

    /// <summary>
    /// Sample data matching the version's bindings, parsed from the raw JSON string the API
    /// transports it as.
    /// </summary>
    /// <remarks>
    /// The only free-form field the SDK decodes into <see cref="JsonElement"/> rather than
    /// leaving as a string — a sanctioned exception to the SDK's "typed models, never
    /// dictionaries" rule, because this is arbitrary caller data with no schema to model.
    /// Parsing is lenient: empty, malformed or non-object sample data all decode to an empty
    /// JSON object rather than throwing.
    /// </remarks>
    [JsonPropertyName("sampleData")]
    [JsonConverter(typeof(EmbeddedJsonConverter))]
    public JsonElement SampleData { get; init; } = PagrJson.EmptyObject;

    /// <summary>The pattern used to name documents rendered from this version, if any.</summary>
    [JsonPropertyName("documentNameTemplate")]
    public string? DocumentNameTemplate { get; init; }

    /// <summary>When this version was published, or <see langword="null"/> when unpublished.</summary>
    [JsonPropertyName("publishedAt")]
    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>Who published this version.</summary>
    [JsonPropertyName("publishedBy")]
    public string? PublishedBy { get; init; }

    /// <summary>The template this version belongs to.</summary>
    [JsonPropertyName("templateId")]
    public Guid TemplateId { get; init; }

    /// <summary>When this version was last updated.</summary>
    [JsonPropertyName("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>The version's translations as a raw JSON string, or <see langword="null"/> when it has none.</summary>
    [JsonPropertyName("translations")]
    public string? Translations { get; init; }

    /// <inheritdoc/>
    public override string ToString() => $"v{VersionNumber} ({Id})";
}
