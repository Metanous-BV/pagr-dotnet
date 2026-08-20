using System.Text.Json;

namespace Pagr.Sdk.Models;

/// <summary>
/// Result of a <c>PagrApiClient.RenderPdfAsync</c> call.
/// </summary>
/// <remarks>
/// <see cref="Document"/> is the rendered <see cref="PdfDocument"/> on success, or
/// <see langword="null"/> when the render was blocked or failed — inspect <see cref="Issues"/>
/// and <see cref="Status"/> for why. That is a business outcome, not an exception: the API
/// answers a blocked render with HTTP 422 and a JSON envelope instead of a PDF stream, and the
/// SDK returns it as data. <see cref="Status"/> is one of <c>"ok"</c>, <c>"partial"</c>,
/// <c>"failed"</c> or <c>"insufficient_credit"</c>.
/// </remarks>
public sealed class PdfRenderResult
{
    /// <summary>The rendered PDF, or <see langword="null"/> if it did not render.</summary>
    public PdfDocument? Document { get; init; }

    /// <summary>The render status reported by the API (e.g. <c>"ok"</c>, <c>"insufficient_credit"</c>).</summary>
    public string Status { get; init; } = "ok";

    /// <summary>An optional human-readable message from the API.</summary>
    public string? Message { get; init; }

    /// <summary>The issues reported for this render, if any.</summary>
    public IReadOnlyList<RenderIssue> Issues { get; init; } = [];

    /// <summary><see langword="true"/> when a rendered PDF came back.</summary>
    public bool Ok => Document is not null;

    /// <summary><see langword="true"/> when the render was blocked for lack of credit.</summary>
    public bool InsufficientCredit => Status == "insufficient_credit";

    /// <summary>
    /// Builds a failed result from the JSON envelope the API returns (with HTTP 422) when
    /// there is no PDF to stream.
    /// </summary>
    /// <remarks>
    /// Lenient by design: a missing <c>status</c> falls back to <c>"failed"</c> and a missing
    /// or non-array <c>issues</c> to an empty list, so a truncated error body still produces a
    /// usable "did not render" result rather than a decode failure on top of the render
    /// failure.
    /// </remarks>
    internal static PdfRenderResult FromErrorEnvelope(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object)
            return new PdfRenderResult { Document = null, Status = "failed" };

        return new PdfRenderResult
        {
            Document = null,
            Status = data.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String
                ? status.GetString() ?? "failed"
                : "failed",
            Message = data.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String
                ? message.GetString()
                : null,
            Issues = data.TryGetProperty("issues", out var issues) && issues.ValueKind == JsonValueKind.Array
                ? PagrJson.Deserialize<List<RenderIssue>>(issues)
                : [],
        };
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        if (Ok)
            return Document!.ToString();
        var errors = Issues.Where(i => i.IsError).Select(i => i.Description).ToList();
        var reason = errors.Count > 0 ? string.Join("; ", errors)
            : Message ?? (string.IsNullOrEmpty(Status) ? "not rendered" : Status);
        return $"PdfRenderResult FAILED — {reason}";
    }
}
