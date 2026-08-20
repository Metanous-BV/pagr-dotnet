using System.Text.Json;
using System.Text.Json.Serialization;
using Pagr.Sdk.Models;

namespace Pagr.Sdk.Webhooks;

/// <summary>
/// A per-document progress webhook delivered during an async render.
/// </summary>
/// <remarks>
/// One is sent for each document that successfully renders. Documents render in parallel, so
/// callbacks arrive out of input order — <see cref="DocumentIndex"/> is the field that
/// correlates this document back to its input (the embedded <see cref="Document"/> carries
/// the same value).
/// </remarks>
public sealed class RenderProgress : RenderCallback
{
    /// <summary>The job this progress update belongs to.</summary>
    [JsonPropertyName("jobId")]
    public required Guid JobId { get; init; }

    /// <summary>The number of documents processed so far (completion order).</summary>
    [JsonPropertyName("processed")]
    public required int Processed { get; init; }

    /// <summary>The number of documents submitted with the job.</summary>
    [JsonPropertyName("requestedCount")]
    public required int RequestedCount { get; init; }

    /// <summary>
    /// The zero-based position of this document in the submitted batch — the field that
    /// correlates a rendered document back to its input, since callbacks arrive out of order.
    /// </summary>
    [JsonPropertyName("documentIndex")]
    public required int DocumentIndex { get; init; }

    /// <summary>The document that was just rendered. Always present on a progress callback.</summary>
    [JsonPropertyName("document")]
    public required RenderedDocument Document { get; init; }

    /// <summary>Progress through the job as a percentage (0–100).</summary>
    [JsonIgnore]
    public double ProgressPct => RequestedCount != 0 ? (Processed / (double)RequestedCount) * 100 : 0.0;

    internal static RenderProgress FromJson(JsonElement json) => PagrJson.Deserialize<RenderProgress>(json);

    /// <inheritdoc/>
    public override string ToString() =>
        $"RenderProgress {Processed}/{RequestedCount} — {Document.DocumentName}";
}
