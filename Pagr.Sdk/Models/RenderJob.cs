using System.Text.Json.Serialization;

namespace Pagr.Sdk.Models;

/// <summary>A reference to an enqueued async render job, returned by <c>EnqueueBatchRenderAsync</c>.</summary>
/// <remarks>
/// <see cref="State"/> is normally <see cref="RenderJobState.Queued"/> on creation. Track
/// progress via the webhook callbacks or by polling <c>GetJobStatusAsync</c> (or
/// <c>WaitForJobAsync</c>).
/// </remarks>
public sealed class RenderJob
{
    /// <summary>The unique identifier of the enqueued job.</summary>
    [JsonPropertyName("jobId")]
    public required Guid JobId { get; init; }

    /// <summary>The number of documents submitted with the job.</summary>
    [JsonPropertyName("requestedCount")]
    public int RequestedCount { get; init; }

    /// <summary>The job's lifecycle state, normally <see cref="RenderJobState.Queued"/> on creation.</summary>
    [JsonPropertyName("state")]
    public RenderJobState State { get; init; } = RenderJobState.Unknown;

    /// <inheritdoc/>
    public override string ToString() => $"RenderJob {JobId} — {RequestedCount} doc(s), state={State}";
}
