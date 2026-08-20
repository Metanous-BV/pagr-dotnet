namespace Pagr.Sdk;

/// <summary>
/// Canonical per-endpoint filter field/operator tables, transcribed from the reference
/// implementation's <c>Python/pagr/filters.py</c> (the authoritative source).
/// </summary>
/// <remarks>
/// The server silently ignores an unknown filter field or operator and returns the
/// unfiltered result set — so a typo would not error, it would silently return everything.
/// <see cref="ListOptions.ToQuery"/> validates against these tables so a typo turns into an
/// immediate, obvious exception instead of a silently wrong result set. Field names are the
/// API's camelCase wire names.
/// </remarks>
internal static class Filters
{
    // Operator sets, reused across fields of the same kind.
    private static readonly FilterOp[] Eq = [FilterOp.Eq];                                          // exact match only (ids/guids)
    private static readonly FilterOp[] StringOps = [FilterOp.Eq, FilterOp.Contains];                 // text fields
    private static readonly FilterOp[] OrdOps = [FilterOp.Eq, FilterOp.Gt, FilterOp.Gte, FilterOp.Lt, FilterOp.Lte]; // numbers and datetimes
    private static readonly FilterOp[] EnumOps = [FilterOp.Eq, FilterOp.Neq];                        // closed-vocabulary fields

    /// <summary>Filters accepted by <c>GetTemplatesAsync</c> (both the org-wide and project-scoped overloads).</summary>
    public static readonly IReadOnlyDictionary<string, FilterOp[]> TemplateFilters = new Dictionary<string, FilterOp[]>
    {
        ["name"] = StringOps,
        ["project.guid"] = Eq,
        ["createdAt"] = OrdOps,
        ["updatedAt"] = OrdOps,
    };

    /// <summary>Filters accepted by <c>GetTemplateVersionsAsync</c>.</summary>
    public static readonly IReadOnlyDictionary<string, FilterOp[]> TemplateVersionFilters = new Dictionary<string, FilterOp[]>
    {
        ["versionNumber"] = OrdOps,
        ["publishedAt"] = OrdOps,
        ["createdAt"] = OrdOps,
        ["updatedAt"] = OrdOps,
    };

    /// <summary>
    /// Filters accepted by <c>GetDocumentsAsync</c>. Note <c>renderDuration</c> can be sorted
    /// on but not filtered, and <c>documentType</c> supports neither — so neither appears here.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, FilterOp[]> DocumentFilters = new Dictionary<string, FilterOp[]>
    {
        ["documentName"] = StringOps,
        ["template.guid"] = Eq,
        ["versionNumber"] = OrdOps,
        ["fileSizeBytes"] = OrdOps,
        ["pageCount"] = OrdOps,
        ["renderedAt"] = OrdOps,
        ["createdAt"] = OrdOps,
        ["updatedAt"] = OrdOps,
        ["environment"] = EnumOps,
        ["language"] = EnumOps,
    };
}
