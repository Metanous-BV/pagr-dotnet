using System.Collections;
using System.Text.Json;

namespace Pagr.Sdk.Models;

/// <summary>
/// Result of a synchronous batch render.
/// </summary>
/// <remarks>
/// Enumerable and indexable over the per-input <see cref="BatchItem"/>s: each submitted
/// document is correlated to its rendered document or the errors that prevented it from
/// rendering. Correlation uses the <see cref="RenderedDocument.DocumentIndex"/> and
/// <see cref="RenderIssue.DocumentIndex"/> the API reports, not list position.
/// </remarks>
public sealed class BatchRenderResult : IReadOnlyList<BatchItem>
{
    /// <summary>The per-input outcomes, in request order.</summary>
    public IReadOnlyList<BatchItem> Items { get; private init; } = [];

    /// <summary>The status reported by the API (e.g. <c>"ok"</c>, <c>"insufficient_credit"</c>).</summary>
    public string Status { get; private init; } = "ok";

    /// <summary>An optional human-readable message from the API.</summary>
    public string? Message { get; private init; }

    /// <summary>The number of documents requested.</summary>
    public int RequestedCount { get; private init; }

    /// <summary>The number of documents that rendered successfully.</summary>
    public int RenderedCount { get; private init; }

    /// <summary>
    /// The number of requested documents that did not render — <see cref="RequestedCount"/>
    /// minus <see cref="RenderedCount"/>.
    /// </summary>
    /// <remarks>
    /// That subtraction <em>is</em> the field's definition, so it is computed from the two counts
    /// rather than read from the response, and <see cref="Ok"/> is derived from it.
    /// </remarks>
    public int MissingCount { get; private init; }

    // Derived views are computed once and cached; the items are fully populated before the
    // result is constructed (see FromApi), so these are safe to memoize.
    private IReadOnlyList<BatchItem>? _succeeded;
    private IReadOnlyList<BatchItem>? _failed;
    private IReadOnlyList<RenderedDocument>? _documents;

    /// <summary>The items that rendered successfully.</summary>
    public IReadOnlyList<BatchItem> Succeeded => _succeeded ??= Items.Where(it => it.Ok).ToList();

    /// <summary>The items that failed to render.</summary>
    public IReadOnlyList<BatchItem> Failed => _failed ??= Items.Where(it => !it.Ok).ToList();

    /// <summary>All successfully rendered documents.</summary>
    public IReadOnlyList<RenderedDocument> Documents =>
        _documents ??= Items.Where(it => it.Document is not null).Select(it => it.Document!).ToList();

    /// <summary><see langword="true"/> when the batch stopped because the organisation is out of credit.</summary>
    public bool InsufficientCredit => Status == "insufficient_credit";

    /// <summary><see langword="true"/> when every requested document rendered and credit was sufficient.</summary>
    public bool Ok => MissingCount == 0 && !InsufficientCredit;

    /// <inheritdoc/>
    public int Count => Items.Count;

    /// <inheritdoc/>
    public BatchItem this[int index] => Items[index];

    /// <inheritdoc/>
    public IEnumerator<BatchItem> GetEnumerator() => Items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Writes every rendered document to a directory.
    /// </summary>
    /// <remarks>Only documents that carry inline content (rendered with <c>includeDocument: true</c>) are written.</remarks>
    /// <param name="directory">Destination directory; created if it does not exist.</param>
    /// <param name="cancellationToken">A token to cancel the writes.</param>
    /// <returns>The paths that were written.</returns>
    public async Task<IReadOnlyList<string>> SaveAllAsync(string directory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        var written = new List<string>();
        foreach (var item in Items)
        {
            if (item.Document is { HasContent: true } doc)
                written.Add(await doc.SaveAsync(directory, cancellationToken).ConfigureAwait(false));
        }
        return written;
    }

    /// <summary>
    /// Builds a result from the API response, correlating inputs to outcomes.
    /// </summary>
    /// <remarks>
    /// Correlation contract: every rendered document reports its own
    /// <see cref="RenderedDocument.DocumentIndex"/>, so it is placed at exactly the slot of the
    /// input that produced it. That index is the only correlation — a document whose index is
    /// absent or out of range is dropped, never guessed onto a slot by position. Issues attach
    /// to their slot the same way, with batch-wide issues (no
    /// <see cref="RenderIssue.DocumentIndex"/>) attaching to every item. A slot left with
    /// neither a document nor a reason is marked failed with a synthetic issue.
    /// </remarks>
    /// <param name="data">The decoded render response.</param>
    /// <param name="inputs">
    /// The originally submitted document data sets; the input at position <c>i</c> is attached to
    /// item <c>i</c>. When <see langword="null"/>, items carry no
    /// <see cref="BatchItem.Input"/> reference.
    /// </param>
    internal static BatchRenderResult FromApi(RenderApiResponse data, IReadOnlyList<JsonElement>? inputs)
    {
        // A JSON `null` for these collections overrides the property initializers, so guard each one.
        var docs = data.Documents ?? [];
        var allIssues = data.Issues ?? [];

        var requestedCount = data.RequestedCount ?? 0;
        var n = requestedCount != 0
            ? requestedCount
            : (inputs?.Count ?? docs.Count);

        var items = new BatchItem[n];
        for (var i = 0; i < n; i++)
        {
            items[i] = new BatchItem
            {
                Index = i,
                Input = inputs is not null && i < inputs.Count ? inputs[i] : null,
            };
        }

        // Distribute issues to their document. Batch-wide issues (documentIndex is null)
        // attach to every item.
        foreach (var issue in allIssues)
        {
            if (issue.DocumentIndex is not int idx)
            {
                foreach (var item in items)
                    item.IssueList.Add(issue);
            }
            else if (idx >= 0 && idx < items.Length)
            {
                items[idx].IssueList.Add(issue);
            }
        }

        // Place each rendered document at the slot it reports via DocumentIndex. That index is
        // the only correlation: a document whose index is absent or out of range is dropped,
        // never guessed onto a slot by position.
        foreach (var doc in docs)
        {
            if (doc.DocumentIndex is int idx && idx >= 0 && idx < items.Length)
                items[idx].Document = doc;
        }

        // Anything left without a document or a reason is a silent render failure.
        foreach (var item in items)
        {
            if (item.Document is null && item.IssueList.Count == 0)
            {
                item.IssueList.Add(new RenderIssue
                {
                    Type = RenderIssueType.Unknown,
                    Severity = RenderIssueSeverity.Error,
                    Description = "not rendered",
                    DocumentIndex = item.Index,
                });
            }
        }

        var renderedCount = data.RenderedCount ?? 0;
        var requested = requestedCount != 0 ? requestedCount : n;
        var rendered = renderedCount != 0 ? renderedCount : docs.Count;
        return new BatchRenderResult
        {
            Items = items,
            Status = data.Status,
            Message = data.Message,
            RequestedCount = requested,
            RenderedCount = rendered,
            // By definition, not a value read from the response — see MissingCount. Clamped at 0
            // so a server sending rendered > requested can never produce a negative count.
            MissingCount = Math.Max(0, requested - rendered),
        };
    }
}
