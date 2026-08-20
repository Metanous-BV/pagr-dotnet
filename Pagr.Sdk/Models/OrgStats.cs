using System.Text.Json.Serialization;

namespace Pagr.Sdk.Models;

/// <summary>
/// Usage and credit statistics for the authenticated organisation, covering the current
/// billing period (<see cref="PeriodStart"/>–<see cref="PeriodEnd"/>).
/// </summary>
public sealed class OrgStats
{
    /// <summary>The organisation's display name.</summary>
    [JsonPropertyName("organisationName")]
    public string? OrganisationName { get; init; }

    /// <summary>The start of the current billing period.</summary>
    [JsonPropertyName("periodStart")]
    public DateTimeOffset? PeriodStart { get; init; }

    /// <summary>The end of the current billing period.</summary>
    [JsonPropertyName("periodEnd")]
    public DateTimeOffset? PeriodEnd { get; init; }

    /// <summary>The organisation's subscription tier.</summary>
    [JsonPropertyName("tier")]
    public string? Tier { get; init; }

    /// <summary>
    /// The number of renders included per month in the current tier, or <see langword="null"/>
    /// when the server omits the field (distinct from a genuine <c>0</c>).
    /// </summary>
    [JsonPropertyName("includedRendersPerMonth")]
    public int? IncludedRendersPerMonth { get; init; }

    /// <summary>The number of pages used in the current period, or <see langword="null"/> when omitted.</summary>
    [JsonPropertyName("pagesUsedThisPeriod")]
    public int? PagesUsedThisPeriod { get; init; }

    /// <summary>
    /// The number of pages still available in the current period, or <see langword="null"/>
    /// when omitted. <c>-1</c> means unlimited for the organisation's <see cref="Tier"/>.
    /// </summary>
    [JsonPropertyName("pagesAvailable")]
    public int? PagesAvailable { get; init; }

    /// <summary>
    /// The number of AI tokens included per month in the current tier, or
    /// <see langword="null"/> when omitted. <c>-1</c> means unlimited.
    /// </summary>
    [JsonPropertyName("includedTokensPerMonth")]
    public int? IncludedTokensPerMonth { get; init; }

    /// <summary>The number of AI tokens used in the current period, or <see langword="null"/> when omitted.</summary>
    [JsonPropertyName("tokensUsedThisPeriod")]
    public int? TokensUsedThisPeriod { get; init; }

    /// <summary>
    /// The number of AI tokens still available in the current period, or <see langword="null"/>
    /// when omitted. <c>-1</c> means unlimited.
    /// </summary>
    [JsonPropertyName("tokensAvailable")]
    public int? TokensAvailable { get; init; }

    /// <summary>The number of users in the organisation, or <see langword="null"/> when omitted.</summary>
    [JsonPropertyName("userCount")]
    public int? UserCount { get; init; }

    /// <inheritdoc/>
    public override string ToString()
    {
        var period = PeriodStart is not null && PeriodEnd is not null
            ? $"{PeriodStart:yyyy-MM-dd} → {PeriodEnd:yyyy-MM-dd}"
            : "—";
        return
            $"OrgStats | {OrganisationName ?? "?"} ({Tier})" + Environment.NewLine +
            $"  Period:  {period}" + Environment.NewLine +
            $"  Pages:   {PagesUsedThisPeriod} used / {IncludedRendersPerMonth} included / {PagesAvailable} remaining" + Environment.NewLine +
            $"  Tokens:  {TokensUsedThisPeriod} used / {IncludedTokensPerMonth} included / {TokensAvailable} remaining" + Environment.NewLine +
            $"  Users:   {UserCount}";
    }
}
