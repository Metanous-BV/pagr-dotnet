using System.Globalization;

namespace Pagr.Sdk;

/// <summary>
/// A single field filter for a list endpoint, serialised to the API's indexed
/// model-binding query form (<c>filters[0].field=...&amp;filters[0].op=...&amp;filters[0].value=...</c>).
/// </summary>
/// <remarks>
/// Which fields and operators are valid is endpoint-specific — see the <c>allowedFilters</c>
/// table each <c>PagrApiClient</c> list method validates against. <see cref="Guid"/> and
/// <see cref="DateTimeOffset"/> values do not serialise to a query string on their own; use
/// the typed static factories (e.g. <see cref="Eq(string, Guid)"/>,
/// <see cref="Gte(string, DateTimeOffset)"/>) to coerce them to their wire string form
/// instead of formatting them by hand.
/// </remarks>
public sealed class Filter
{
    /// <summary>Creates an equality filter (<see cref="FilterOp.Eq"/>).</summary>
    /// <param name="field">The field name to filter on.</param>
    /// <param name="value">The value to compare against.</param>
    public Filter(string field, string value)
        : this(field, FilterOp.Eq, value) { }

    /// <summary>Creates a filter with an explicit operator.</summary>
    /// <param name="field">The field name to filter on.</param>
    /// <param name="op">The comparison operator.</param>
    /// <param name="value">The value to compare against.</param>
    public Filter(string field, FilterOp op, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(value);
        Field = field;
        Op = op;
        Value = value;
    }

    /// <summary>The field name to filter on.</summary>
    public string Field { get; }

    /// <summary>The comparison operator.</summary>
    public FilterOp Op { get; }

    /// <summary>The value to compare against.</summary>
    public string Value { get; }

    /// <summary>Creates an equality filter (<see cref="FilterOp.Eq"/>) against a GUID field.</summary>
    public static Filter Eq(string field, Guid value) => new(field, FilterOp.Eq, value.ToString());

    /// <summary>Creates an equality filter (<see cref="FilterOp.Eq"/>) against a date/time field.</summary>
    public static Filter Eq(string field, DateTimeOffset value) => new(field, FilterOp.Eq, ToWireString(value));

    /// <summary>Creates a greater-than filter (<see cref="FilterOp.Gt"/>) against a date/time field.</summary>
    public static Filter Gt(string field, DateTimeOffset value) => new(field, FilterOp.Gt, ToWireString(value));

    /// <summary>Creates a greater-than-or-equal filter (<see cref="FilterOp.Gte"/>) against a date/time field.</summary>
    public static Filter Gte(string field, DateTimeOffset value) => new(field, FilterOp.Gte, ToWireString(value));

    /// <summary>Creates a less-than filter (<see cref="FilterOp.Lt"/>) against a date/time field.</summary>
    public static Filter Lt(string field, DateTimeOffset value) => new(field, FilterOp.Lt, ToWireString(value));

    /// <summary>Creates a less-than-or-equal filter (<see cref="FilterOp.Lte"/>) against a date/time field.</summary>
    public static Filter Lte(string field, DateTimeOffset value) => new(field, FilterOp.Lte, ToWireString(value));

    private static string ToWireString(DateTimeOffset value) =>
        value.ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFzzz", CultureInfo.InvariantCulture);
}
