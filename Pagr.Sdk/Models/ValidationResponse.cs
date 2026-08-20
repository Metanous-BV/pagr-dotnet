using System.Collections;

namespace Pagr.Sdk.Models;

/// <summary>
/// Validation results for a batch of documents.
/// </summary>
/// <remarks>
/// The API returns a single flat list of <see cref="RenderIssue"/>s; each issue carries the
/// <see cref="RenderIssue.DocumentIndex"/> of the document it pertains to
/// (<see langword="null"/> for batch-wide issues). <see cref="IsValid"/> is the production
/// gate: it is <see langword="true"/> only when no issue is blocking production (i.e.
/// <see cref="RenderIssueSeverity.Warning"/> or <see cref="RenderIssueSeverity.Error"/>).
/// Callers who want the narrower, Error-only check should inspect <see cref="Errors"/>
/// directly instead. Enumerable and indexable over the issues.
/// </remarks>
public sealed class ValidationResponse : IReadOnlyList<RenderIssue>
{
    /// <summary>All issues reported for the submitted documents.</summary>
    public IReadOnlyList<RenderIssue> Issues { get; }

    internal ValidationResponse(IReadOnlyList<RenderIssue> issues) => Issues = issues;

    internal static ValidationResponse FromApi(ValidationApiResponse data) => new(data.Issues ?? []);

    /// <summary>
    /// <see langword="true"/> when no issue is <see cref="RenderIssueSeverity.Warning"/> or
    /// <see cref="RenderIssueSeverity.Error"/> severity (i.e. no issue blocks a production
    /// render). For the narrower, Error-only check, use <see cref="Errors"/> directly.
    /// </summary>
    public bool IsValid => !Issues.Any(i => i.Severity.IsBlockingProduction());

    /// <summary>The issues of <see cref="RenderIssueSeverity.Error"/> severity.</summary>
    public IReadOnlyList<RenderIssue> Errors =>
        Issues.Where(i => i.Severity == RenderIssueSeverity.Error).ToList();

    /// <summary>The issues of <see cref="RenderIssueSeverity.Warning"/> severity.</summary>
    public IReadOnlyList<RenderIssue> Warnings =>
        Issues.Where(i => i.Severity == RenderIssueSeverity.Warning).ToList();

    /// <summary>
    /// The issues pertaining to a specific document, including batch-wide issues (those
    /// whose <see cref="RenderIssue.DocumentIndex"/> is <see langword="null"/>).
    /// </summary>
    /// <param name="documentIndex">The zero-based position of the document within the submitted batch.</param>
    /// <returns>The matching issues.</returns>
    public IReadOnlyList<RenderIssue> IssuesFor(int documentIndex) =>
        Issues.Where(i => i.DocumentIndex is null || i.DocumentIndex == documentIndex).ToList();

    /// <inheritdoc/>
    public int Count => Issues.Count;

    /// <inheritdoc/>
    public RenderIssue this[int index] => Issues[index];

    /// <inheritdoc/>
    public IEnumerator<RenderIssue> GetEnumerator() => Issues.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc/>
    public override string ToString()
    {
        var header = IsValid ? "valid" : $"{Errors.Count} error(s), {Warnings.Count} warning(s)";
        if (Issues.Count == 0)
            return $"ValidationResponse ({header})";
        var body = string.Join(Environment.NewLine, Issues.Select(i => $"  {i}"));
        return $"ValidationResponse ({header}){Environment.NewLine}{body}";
    }
}
