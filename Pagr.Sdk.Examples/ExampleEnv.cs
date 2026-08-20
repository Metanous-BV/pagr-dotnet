namespace Pagr.Sdk.Examples;

/// <summary>
/// Shared configuration for the examples: reads a <c>.env</c> file next to the project
/// and falls back to process environment
/// variables.
/// </summary>
internal static class ExampleEnv
{
    static ExampleEnv()
    {
        foreach (var dir in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var envFile = FindUpwards(dir, ".env");
            if (envFile is null)
                continue;
            foreach (var line in File.ReadAllLines(envFile))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#') || !trimmed.Contains('='))
                    continue;
                var (key, value) = (trimmed[..trimmed.IndexOf('=')].Trim(),
                    trimmed[(trimmed.IndexOf('=') + 1)..].Trim().Trim('"'));
                if (Environment.GetEnvironmentVariable(key) is null)
                    Environment.SetEnvironmentVariable(key, value);
            }
            break;
        }
    }

    /// <summary>
    /// Optional API base URL override. Null means "use the SDK default", i.e. the hosted
    /// Pagr API; set <c>PAGR_BASE_URL</c> only to target another instance.
    /// </summary>
    public static string? BaseUrl => Environment.GetEnvironmentVariable("PAGR_BASE_URL");

    /// <summary>The test-environment API key most examples use.</summary>
    public static string? TestKey => Environment.GetEnvironmentVariable("TEST_KEY_PUBLIC");

    /// <summary>A production API key; only needed by the validate example.</summary>
    public static string? ProdKey => Environment.GetEnvironmentVariable("PROD_KEY_PUBLIC");

    /// <summary>Where rendered PDFs are written.</summary>
    public static string OutputDir
    {
        get
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "test_output");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Creates a client with the test key, or explains what is missing.</summary>
    public static PagrApiClient? CreateClient()
    {
        if (string.IsNullOrWhiteSpace(TestKey))
        {
            Console.Error.WriteLine(
                "TEST_KEY_PUBLIC is not set. Create a .env file (or set env vars) with:\n" +
                "  TEST_KEY_PUBLIC=your-test-api-key\n" +
                "  PROD_KEY_PUBLIC=your-prod-api-key   # only needed by 'validate'");
            return null;
        }
        return new PagrApiClient(TestKey, BaseUrl);
    }

    private static string? FindUpwards(string start, string fileName)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }
}
