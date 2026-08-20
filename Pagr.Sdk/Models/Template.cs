using System.Text.Json.Serialization;

namespace Pagr.Sdk.Models;

/// <summary>
/// A document template as listed by the API.
/// </summary>
/// <remarks>
/// Carries the template's identity and catalogue metadata (project, latest version number,
/// audit fields). The actual template content lives on its versions — fetch one with
/// <c>GetTemplateVersionAsync</c>.
/// </remarks>
public sealed class Template
{
    /// <summary>The unique identifier of the template.</summary>
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>The template's display name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>The pattern used to name documents rendered from this template, if any.</summary>
    [JsonPropertyName("documentNameTemplate")]
    public string? DocumentNameTemplate { get; init; }

    /// <summary>The identifier of the project the template belongs to, if any.</summary>
    [JsonPropertyName("projectId")]
    public Guid? ProjectId { get; init; }

    /// <summary>The name of the project the template belongs to, if any.</summary>
    [JsonPropertyName("projectName")]
    public string? ProjectName { get; init; }

    /// <summary>The most recent published version number, or <see langword="null"/> when unpublished.</summary>
    [JsonPropertyName("latestVersionNumber")]
    public int? LatestVersionNumber { get; init; }

    /// <summary>The total number of versions of this template.</summary>
    [JsonPropertyName("versionCount")]
    public int VersionCount { get; init; }

    /// <summary>When the template was last updated.</summary>
    [JsonPropertyName("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>Who last updated the template.</summary>
    [JsonPropertyName("updatedBy")]
    public string? UpdatedBy { get; init; }

    /// <summary>The identifier of the master template this derives from, if any.</summary>
    [JsonPropertyName("masterTemplateId")]
    public Guid? MasterTemplateId { get; init; }

    /// <summary>The name of the master template this derives from, if any.</summary>
    [JsonPropertyName("masterTemplateName")]
    public string? MasterTemplateName { get; init; }

    /// <inheritdoc/>
    public override string ToString() => $"{Name} ({Id})";
}
