using System.Text.Json.Serialization;

namespace Pagr.Sdk.Models;

/// <summary>
/// Status of an async render job, returned by the polling endpoint
/// <c>GET /v1/render/jobs/{jobId}</c>.
/// </summary>
/// <remarks>
/// A reliable alternative to webhook callbacks: poll until <see cref="Done"/>. Lifecycle and
/// outcome are separate fields: <see cref="State"/> is the job's lifecycle (queued/rendering
/// vs finished); <see cref="Status"/> is the render outcome using the same vocabulary as the
/// synchronous envelope, and is <see langword="null"/> while the job is still pending.
/// <see cref="Issues"/> carries the per-document diagnostics (capped at 100 server-side); the
/// counts stay exact.
/// </remarks>
public sealed class RenderJobStatus
{
    /// <summary>The unique identifier of the job.</summary>
    [JsonPropertyName("jobId")]
    public required Guid JobId { get; init; }

    /// <summary>The job's lifecycle state: queued or rendering (non-terminal), or finished (terminal).</summary>
    [JsonPropertyName("state")]
    public RenderJobState State { get; init; } = RenderJobState.Unknown;

    /// <summary>
    /// The render outcome, using the same vocabulary as the synchronous envelope. <see langword="null"/>
    /// while the job is still pending.
    /// </summary>
    [JsonPropertyName("status")]
    public RenderOutcome? Status { get; init; }

    /// <summary>The number of documents the job has rendered so far.</summary>
    [JsonPropertyName("renderedCount")]
    public int RenderedCount { get; init; }

    /// <summary>The number of documents submitted with the job.</summary>
    [JsonPropertyName("requestedCount")]
    public int RequestedCount { get; init; }

    /// <summary>The number of requested documents that did not render.</summary>
    [JsonPropertyName("missingCount")]
    public int MissingCount { get; init; }

    /// <summary>The per-document diagnostics for this job, if any.</summary>
    [JsonPropertyName("issues")]
    public IReadOnlyList<RenderIssue> Issues { get; init; } = [];

    /// <summary>When the job started.</summary>
    [JsonPropertyName("startedAt")]
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>When the job finished, or <see langword="null"/> while it is still running.</summary>
    [JsonPropertyName("completedAt")]
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>Why the job failed, or <see langword="null"/> when it did not fail.</summary>
    [JsonPropertyName("failureReason")]
    public string? FailureReason { get; init; }

    /// <summary>
    /// <see langword="true"/> once the job reached a terminal state. Terminal means
    /// <see cref="RenderJobState.Completed"/> or <see cref="RenderJobState.Failed"/> — and also
    /// <see cref="RenderJobState.Unknown"/> (fail-open), so an unrecognised server state ends a
    /// poll loop rather than spinning forever.
    /// </summary>
    [JsonIgnore]
    public bool Done => State.IsTerminal();

    /// <summary><see langword="true"/> when the job completed and every document rendered.</summary>
    [JsonIgnore]
    public bool Ok => State == RenderJobState.Completed && Status == RenderOutcome.Ok;

    /// <summary><see langword="true"/> when the job stopped early because the organisation is out of credit.</summary>
    [JsonIgnore]
    public bool InsufficientCredit => Status == RenderOutcome.InsufficientCredit;

    /// <inheritdoc/>
    public override string ToString() =>
        $"RenderJobStatus {JobId} — state={State} status={(Status is { } s ? s.ToString() : "null")} ({RenderedCount} rendered)";
}
