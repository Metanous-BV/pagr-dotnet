using System.Text.Json;
using System.Text.Json.Serialization;
using Pagr.Sdk.Models;

namespace Pagr.Sdk.Webhooks;

/// <summary>
/// The final webhook delivered once an async render job finishes.
/// </summary>
/// <remarks>
/// <see cref="State"/> is the terminal lifecycle value — <see cref="RenderJobState.Completed"/>
/// (one or more documents produced, including partial and credit-stopped runs) or
/// <see cref="RenderJobState.Failed"/> (nothing produced); the callback only ever fires at a
/// terminal state, so <see cref="State"/> is never <see cref="RenderJobState.Pending"/> here.
/// <see cref="MissingCount"/> is <see cref="RequestedCount"/> minus <see cref="RenderedCount"/>;
/// <see cref="Issues"/> carries the per-document diagnostics, each with its own
/// <see cref="RenderIssue.DocumentIndex"/>.
/// </remarks>
public sealed class RenderCompletion : RenderCallback
{
    /// <summary>The job that finished.</summary>
    [JsonPropertyName("jobId")]
    public required Guid JobId { get; init; }

    /// <summary>The terminal lifecycle value: <see cref="RenderJobState.Completed"/> or <see cref="RenderJobState.Failed"/>.</summary>
    [JsonPropertyName("state")]
    public RenderJobState State { get; init; } = RenderJobState.Unknown;

    /// <summary>The render outcome (e.g. <see cref="RenderOutcome.Ok"/>, <see cref="RenderOutcome.InsufficientCredit"/>).</summary>
    [JsonPropertyName("status")]
    public RenderOutcome Status { get; init; } = RenderOutcome.Unknown;

    /// <summary>The number of documents that rendered successfully.</summary>
    [JsonPropertyName("renderedCount")]
    public int RenderedCount { get; init; }

    /// <summary>The number of documents that were requested.</summary>
    [JsonPropertyName("requestedCount")]
    public int RequestedCount { get; init; }

    /// <summary>The number of requested documents that did not render, whatever the reason.</summary>
    [JsonPropertyName("missingCount")]
    public int MissingCount { get; init; }

    /// <summary>An optional human-readable message from the API.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    /// <summary>The per-document diagnostics for this job, if any.</summary>
    [JsonPropertyName("issues")]
    public IReadOnlyList<RenderIssue> Issues { get; init; } = [];

    /// <summary><see langword="true"/> when every document in the job rendered.</summary>
    [JsonIgnore]
    public bool Ok => Status == RenderOutcome.Ok;

    /// <summary><see langword="true"/> when the job stopped early because the organisation is out of credit.</summary>
    [JsonIgnore]
    public bool InsufficientCredit => Status == RenderOutcome.InsufficientCredit;

    internal static RenderCompletion FromJson(JsonElement json) => PagrJson.Deserialize<RenderCompletion>(json);

    /// <inheritdoc/>
    public override string ToString() =>
        $"RenderCompletion {JobId} — state={State} status={Status}, {RenderedCount}/{RequestedCount} rendered";
}
