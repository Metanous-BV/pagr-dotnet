namespace Pagr.Sdk;

/// <summary>
/// Paging, sorting, filtering and search options for list endpoints
/// (templates, versions, documents).
/// </summary>
/// <remarks>
/// Only the options you set are sent; a <see langword="null"/> options object sends no
/// query at all. Use <see cref="Models.PagedResult{T}.HasMore"/> on the returned page to
/// drive <see cref="Skip"/>/<see cref="Take"/> paging.
/// </remarks>
public sealed class ListOptions
{
    /// <summary>The number of items to skip (paging offset).</summary>
    public int? Skip { get; init; }

    /// <summary>The page size.</summary>
    public int? Take { get; init; }

    /// <summary>The field name to sort by.</summary>
    public string? SortBy { get; init; }

    /// <summary>The sort order.</summary>
    public SortDirection? SortDirection { get; init; }

    /// <summary>Field filters, serialised to the API's indexed <c>filters[i].field</c> form.</summary>
    public IReadOnlyList<Filter>? Filters { get; init; }

    /// <summary>Free-text search.</summary>
    public string? Search { get; init; }

    /// <summary>
    /// Builds the query pairs for a <c>ListQuery</c>-bound endpoint; unset options are
    /// omitted so the request carries only what was explicitly set.
    /// </summary>
    /// <param name="allowedFilters">
    /// The calling endpoint's field/operator table (see <see cref="Pagr.Sdk.Filters"/>).
    /// <see cref="Filters"/> is endpoint-agnostic and reusable, so each list method passes
    /// its own table rather than this validating against a single global vocabulary.
    /// </param>
    /// <exception cref="ArgumentException">
    /// A filter's field is not in <paramref name="allowedFilters"/>, or its operator is not
    /// valid for that field. The server would otherwise silently ignore the filter and return
    /// the unfiltered result set, so this is rejected client-side instead.
    /// </exception>
    internal IReadOnlyCollection<KeyValuePair<string, string>>? ToQuery(IReadOnlyDictionary<string, FilterOp[]> allowedFilters)
    {
        var query = new List<KeyValuePair<string, string>>();
        if (Skip is { } skip)
            query.Add(new("skip", skip.ToString()));
        if (Take is { } take)
            query.Add(new("take", take.ToString()));
        if (SortBy is not null)
            query.Add(new("sortBy", SortBy));
        if (SortDirection is { } direction)
            query.Add(new("sortDirection", direction == Pagr.Sdk.SortDirection.Ascending ? "asc" : "desc"));
        if (Search is not null)
            query.Add(new("search", Search));
        for (var i = 0; i < (Filters?.Count ?? 0); i++)
        {
            var filter = Filters![i];
            if (!allowedFilters.TryGetValue(filter.Field, out var allowedOps))
            {
                throw new ArgumentException(
                    $"filters[{i}]: unknown field '{filter.Field}' for this endpoint; " +
                    $"allowed fields: {string.Join(", ", allowedFilters.Keys.OrderBy(k => k, StringComparer.Ordinal))}");
            }
            if (!allowedOps.Contains(filter.Op))
            {
                throw new ArgumentException(
                    $"filters[{i}]: operator '{filter.Op.ToString().ToLowerInvariant()}' is not valid for field " +
                    $"'{filter.Field}'; allowed operators: {string.Join(", ", allowedOps.Select(o => o.ToString().ToLowerInvariant()))}");
            }
            query.Add(new($"filters[{i}].field", filter.Field));
            query.Add(new($"filters[{i}].op", filter.Op.ToString().ToLowerInvariant()));
            query.Add(new($"filters[{i}].value", filter.Value));
        }
        return query.Count > 0 ? query : null;
    }
}
