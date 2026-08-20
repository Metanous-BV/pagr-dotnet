using System.Text.Json.Serialization;

namespace Pagr.Sdk.Models;

/// <summary>
/// Raw shape of the synchronous render endpoint's JSON response. Deserialised internally
/// and then projected into <see cref="RenderResult"/> / <see cref="BatchRenderResult"/>.
/// </summary>
internal sealed class RenderApiResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "ok";

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    // Counts are nullable so RenderResult/BatchRenderResult can tell a missing key
    // apart from an explicit zero and apply their own fallbacks.
    [JsonPropertyName("renderedCount")]
    public int? RenderedCount { get; set; }

    [JsonPropertyName("requestedCount")]
    public int? RequestedCount { get; set; }

    [JsonPropertyName("missingCount")]
    public int? MissingCount { get; set; }

    [JsonPropertyName("documents")]
    public List<RenderedDocument> Documents { get; set; } = [];

    /// <summary>Flat list of issues; entries may carry a <c>documentIndex</c>.</summary>
    [JsonPropertyName("issues")]
    public List<RenderIssue> Issues { get; set; } = [];
}
