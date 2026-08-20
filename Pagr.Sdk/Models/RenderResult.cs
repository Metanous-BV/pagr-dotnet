namespace Pagr.Sdk.Models;

/// <summary>
/// Result of a single-document render.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Document"/> is <see langword="null"/> when the document did not render — e.g.
/// it failed validation or the organisation had insufficient credit. Inspect <see cref="Ok"/>,
/// <see cref="Issues"/> and <see cref="Status"/> to find out why.
/// </para>
/// <para>
/// <see cref="Issues"/> is the flat list of <see cref="RenderIssue"/>s returned by the API;
/// filter it by <see cref="RenderIssue.Severity"/> to find blocking errors versus warnings.
/// </para>
/// </remarks>
public sealed class RenderResult
{
    /// <summary>The rendered document, or <see langword="null"/> if it did not render.</summary>
    public RenderedDocument? Document { get; init; }

    /// <summary>The render status reported by the API (e.g. <c>"ok"</c>, <c>"insufficient_credit"</c>).</summary>
    public string Status { get; init; } = "ok";

    /// <summary>The number of documents that rendered successfully.</summary>
    public int RenderedCount { get; init; }

    /// <summary>The number of documents requested.</summary>
    public int RequestedCount { get; init; }

    /// <summary>The number of requested documents that did not render.</summary>
    public int MissingCount { get; init; }

    /// <summary>An optional human-readable message from the API.</summary>
    public string? Message { get; init; }

    /// <summary>The issues reported for this render, if any.</summary>
    public IReadOnlyList<RenderIssue> Issues { get; init; } = [];

    /// <summary><see langword="true"/> when the document rendered successfully.</summary>
    public bool Ok => Document is not null;

    /// <summary><see langword="true"/> when the render stopped because the organisation is out of credit.</summary>
    public bool InsufficientCredit => Status == "insufficient_credit";

    internal static RenderResult FromApi(RenderApiResponse data)
    {
        // A JSON `null` for these collections overrides the property initializers, so guard each one.
        var documents = data.Documents ?? [];
        var document = documents.Count > 0 ? documents[0] : null;

        return new RenderResult
        {
            Document = document,
            Status = data.Status,
            RenderedCount = data.RenderedCount ?? (document is not null ? 1 : 0),
            RequestedCount = data.RequestedCount ?? 1,
            MissingCount = data.MissingCount ?? (document is not null ? 0 : 1),
            Message = data.Message,
            Issues = data.Issues ?? [],
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
        return $"RenderResult FAILED — {reason}";
    }
}
