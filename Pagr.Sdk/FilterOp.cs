namespace Pagr.Sdk;

/// <summary>Comparison operator of a list <see cref="Filter"/>, serialised lowercase on the wire.</summary>
public enum FilterOp
{
    /// <summary>Equal to (<c>"eq"</c>).</summary>
    Eq,

    /// <summary>Not equal to (<c>"neq"</c>).</summary>
    Neq,

    /// <summary>Greater than (<c>"gt"</c>).</summary>
    Gt,

    /// <summary>Greater than or equal to (<c>"gte"</c>).</summary>
    Gte,

    /// <summary>Less than (<c>"lt"</c>).</summary>
    Lt,

    /// <summary>Less than or equal to (<c>"lte"</c>).</summary>
    Lte,

    /// <summary>Substring match (<c>"contains"</c>).</summary>
    Contains,
}
