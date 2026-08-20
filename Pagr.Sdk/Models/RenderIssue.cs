using System.Text.Json.Serialization;

namespace Pagr.Sdk.Models;

/// <summary>
/// A single render or validation issue.
/// </summary>
/// <remarks>
/// The category is carried by <see cref="Type"/> and the blocking-ness by
/// <see cref="Severity"/>. <see cref="DocumentIndex"/> is the zero-based position of the
/// document the issue pertains to in a batch, or <see langword="null"/> for
/// single-document operations and batch-wide issues.
/// </remarks>
public sealed class RenderIssue
{
    /// <summary>The issue category.</summary>
    [JsonPropertyName("type")]
    public RenderIssueType Type { get; init; } = RenderIssueType.Unknown;

    /// <summary>How much the issue blocks rendering.</summary>
    [JsonPropertyName("severity")]
    public RenderIssueSeverity Severity { get; init; } = RenderIssueSeverity.Error;

    /// <summary>A human-readable description of the issue.</summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>The template element the issue pertains to, if any.</summary>
    [JsonPropertyName("elementId")]
    public string? ElementId { get; init; }

    /// <summary>
    /// The zero-based position of the affected document within the submitted batch, or
    /// <see langword="null"/> for single-document operations and batch-wide issues.
    /// </summary>
    [JsonPropertyName("documentIndex")]
    public int? DocumentIndex { get; init; }

    /// <summary><see langword="true"/> when the issue is of <see cref="RenderIssueSeverity.Error"/> severity.</summary>
    [JsonIgnore]
    public bool IsError => Severity == RenderIssueSeverity.Error;

    /// <inheritdoc/>
    public override string ToString()
    {
        var location = ElementId is not null ? $" [{ElementId}]" : "";
        return $"{Severity}: {Type}{location} — {Description}";
    }
}
