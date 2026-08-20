namespace Pagr.Sdk.Examples;

/// <summary>Account-level features: organisation usage stats, available fonts, key rotation.</summary>
internal static class Account
{
    public static async Task RunAsync()
    {
        using var client = ExampleEnv.CreateClient();
        if (client is null)
            return;

        // Usage and quota for the organisation the API key belongs to —
        // useful to check remaining credit before queueing a large batch:
        var stats = await client.GetOrgStatsAsync();
        Console.WriteLine($"{stats.OrganisationName} ({stats.Tier} tier)");
        Console.WriteLine($"  period:  {stats.PeriodStart:yyyy-MM-dd} – {stats.PeriodEnd:yyyy-MM-dd}");
        Console.WriteLine(
            $"  pages:   {stats.PagesUsedThisPeriod} used, {stats.PagesAvailable} available " +
            $"(included per month: {stats.IncludedRendersPerMonth})");
        Console.WriteLine($"  tokens:  {stats.TokensUsedThisPeriod} used, {stats.TokensAvailable} available");
        Console.WriteLine($"  users:   {stats.UserCount}");

        // Font families available to templates:
        var fonts = await client.GetFontsAsync();
        Console.WriteLine($"\n{fonts.Count} font families available, e.g.: {string.Join(", ", fonts.Take(5))}");

        // Swap the API key on a live client — no reconnect needed. Useful when keys are
        // rotated or when one client serves multiple environments:
        client.SetApiKey(ExampleEnv.TestKey!);
        Console.WriteLine("\nSetApiKey() — subsequent requests use the new key");
    }
}
