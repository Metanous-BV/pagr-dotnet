using System.Text.Json.Serialization;

namespace Pagr.Sdk.Models;

/// <summary>
/// A project in the authenticated organisation. Projects group templates; every organisation
/// has at least one.
/// </summary>
/// <remarks>
/// Projects are read-only on the public API. Use <see cref="Id"/> to create a template in the
/// project (<c>CreateTemplateAsync</c>) or to list its templates (<c>GetTemplatesAsync(projectId)</c>).
/// </remarks>
public sealed class Project
{
    /// <summary>The unique identifier of the project.</summary>
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>The project's display name, unique within the organisation.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>The project's description, if any.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>When the project was created.</summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }

    /// <inheritdoc/>
    public override string ToString() => $"{Name} ({Id})";
}
