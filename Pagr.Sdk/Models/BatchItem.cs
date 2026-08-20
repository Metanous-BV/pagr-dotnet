using System.Text.Json;

namespace Pagr.Sdk.Models;

/// <summary>
/// The outcome of a single document within a batch render.
/// </summary>
/// <remarks>
/// Correlates one submitted input to its rendered document or the issues that prevented it
/// from rendering. The input is attached by its position in the submitted array; the document
/// and issues are matched via the <c>documentIndex</c> the API reports for each.
/// </remarks>
public sealed class BatchItem
{
    /// <summary>The position of this item within the submitted batch.</summary>
    public int Index { get; init; }

    /// <summary>
    /// The originally submitted input for this position, or <see langword="null"/> when the
    /// inputs were not supplied for correlation.
    /// </summary>
    public JsonElement? Input { get; internal set; }

    /// <summary>The rendered document for this position, or <see langword="null"/> if it did not render.</summary>
    public RenderedDocument? Document { get; internal set; }

    /// <summary>The issues reported for this document, if any.</summary>
    public IReadOnlyList<RenderIssue> Issues => IssueList;

    internal List<RenderIssue> IssueList { get; set; } = [];

    /// <summary><see langword="true"/> when this document rendered successfully.</summary>
    public bool Ok => Document is not null;

    /// <inheritdoc/>
    public override string ToString()
    {
        if (Ok)
            return $"[{Index}] OK — {Document!.DocumentName}";
        var errors = Issues.Where(i => i.IsError).Select(i => i.Description).ToList();
        var reason = errors.Count > 0 ? string.Join("; ", errors) : "not rendered";
        return $"[{Index}] FAILED — {reason}";
    }
}
