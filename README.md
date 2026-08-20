# Pagr .NET SDK

Official .NET client for the Pagr document-rendering API: manage templates, render
documents (single, batch, or fire-and-forget with webhooks), validate data, and read
organisation usage stats.

> [!TIP]
> Want to chat live with Pagr engineers? Join us on our
> [Discord server](https://discord.gg/GajJxfKXZ5).

## Requirements

- **.NET 8 SDK** or later.
- A **Pagr API key** — grab it from **Settings → API keys** in the Pagr web app.
  The prefix picks the mode: `pagr_test_*` renders are watermarked and batches are
  capped at 10 documents; `pagr_prod_*` renders for real and consumes credit.
- No other runtime packages are needed — the SDK depends only on the framework.

## Installation

Clone the repository and reference the project directly:

```bash
git clone https://github.com/Metanous-BV/pagr-dotnet.git
dotnet add YourApp reference ../pagr-dotnet/Pagr.Sdk/Pagr.Sdk.csproj
```

Or build a package into a local feed, for normal `dotnet add package` ergonomics:

```bash
dotnet pack pagr-dotnet/Pagr.Sdk -c Release -o ./local-feed
dotnet nuget add source ./local-feed --name pagr-local
dotnet add YourApp package Pagr.Sdk
```

## Quick start

```csharp
using Pagr.Sdk;

// The client owns a pooled HttpClient internally — create one and reuse it.
// Targets the hosted production API by default; pass a baseUrl to target another instance.
using var client = new PagrApiClient("pagr_...");

// List templates (paged)
var templates = await client.GetTemplatesAsync();
Console.WriteLine($"{templates.Total} templates, showing {templates.Count}");

// Render a single document against the latest published version.
// Pass a POCO/anonymous object, a JsonElement, or a JSON string.
var result = await client.RenderAsync(templateId,
    new { CustomerName = "Acme", Total = 42.50 }, includeDocument: true);

if (result.Ok)
    await result.Document!.SaveAsync(@"C:\out");   // directory → uses DocumentName + .pdf
else
    Console.WriteLine($"Render failed: {string.Join("; ", result.Issues)}");
```

Every API **error response** (4xx/5xx) throws a subclass of `PagrApiException`; business
outcomes like a failed validation or insufficient credit come back as data on the result
object instead.

## Webhook callbacks

Async-render callbacks are signed (`X-Pagr-Signature`) and also carry `X-Pagr-Event` and
`X-Pagr-Delivery`. Verify the signature over the **raw request bytes** and parse in one step:

```csharp
using Pagr.Sdk.Webhooks;

RenderCallback callback = WebhookSignature.ParseSignedCallback(
    rawBodyBytes, request.Headers[WebhookSignature.HeaderName], webhookSecret);
```

It throws `PagrSignatureException` for anything it cannot prove came from Pagr. Copy the secret
from **Settings → API keys** in the web app. Delivery is retried (5 attempts, exponential
backoff from 2 s), so callbacks can arrive out of order and more than once — dedupe on
`X-Pagr-Delivery`.

## Documentation

The full reference — client configuration, paging/sorting/filtering, validation, batch
and fire-and-forget rendering with signed webhooks, raw-PDF rendering, error
handling, and the complete model list — lives in the
**[User Guide](https://github.com/Metanous-BV/pagr-dotnet/blob/main/docs/user-guide.md)**.

- **[Contributing](https://github.com/Metanous-BV/pagr-dotnet/blob/main/CONTRIBUTING.md)** — for maintainers of the SDK.

Runnable, topic-by-topic examples live in
[`Pagr.Sdk.Examples`](https://github.com/Metanous-BV/pagr-dotnet/blob/main/Pagr.Sdk.Examples/README.md):

```bash
dotnet run --project Pagr.Sdk.Examples -- getting-started
```

## License

Apache-2.0. See [LICENSE](https://github.com/Metanous-BV/pagr-dotnet/blob/main/LICENSE).

- Repository: https://github.com/Metanous-BV/pagr-dotnet
- Issues: https://github.com/Metanous-BV/pagr-dotnet/issues
