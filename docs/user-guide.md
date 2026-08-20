# Pagr .NET SDK — User Guide

The official .NET client for the Pagr document-rendering API
(`/v1`). It covers templates and versions, rendering (synchronous, and
fire-and-forget jobs with webhook callbacks or polling), data validation, document
browsing, organisation statistics, and webhook payload parsing.

- **Target framework:** .NET 8.0
- **Dependencies:** none beyond the framework (uses `System.Text.Json` and `HttpClient`)
- **Namespace:** `Pagr.Sdk` (models in `Pagr.Sdk.Models`, webhooks in `Pagr.Sdk.Webhooks`, exceptions in `Pagr.Sdk.Exceptions`)

> This guide is the comprehensive reference. For a quick tour see the
> [README](../README.md); for runnable, topic-by-topic samples see
> [`Pagr.Sdk.Examples`](../Pagr.Sdk.Examples/README.md).

## Contents

1. [Installation](#1-installation)
2. [Creating a client](#2-creating-a-client)
3. [Passing document data](#3-passing-document-data)
4. [Rendering](#4-rendering)
5. [Validation](#5-validation)
6. [Templates and versions](#6-templates-and-versions)
7. [Browsing rendered documents](#7-browsing-rendered-documents)
8. [Fonts, organisation stats, and meta](#8-fonts-organisation-stats-and-meta)
9. [Working with results and documents](#9-working-with-results-and-documents)
10. [Paging, sorting, and filtering](#10-paging-sorting-and-filtering)
11. [Async jobs: webhooks and polling](#11-async-jobs-webhooks-and-polling)
12. [Error handling](#12-error-handling)
13. [Cancellation, timeouts and retries](#13-cancellation-timeouts-and-retries)
14. [Model reference](#14-model-reference)
15. [Known limitations](#15-known-limitations)

---

## 1. Installation

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

Then import the namespace:

```csharp
using Pagr.Sdk;
```

---

## 2. Creating a client

`PagrApiClient` is the single entry point. Construct it with your organisation API key:

```csharp
using var client = new PagrApiClient("pagr_...");
```

This targets the hosted production API (`PagrApiClient.DefaultBaseUrl`). Pass a `baseUrl`
to target another instance, e.g. during local development where the API runs at
`http://localhost:5000`:

```csharp
using var client = new PagrApiClient("pagr_...", "http://localhost:5000");
```

### Lifetime and thread safety

`PagrApiClient` owns a pooled `HttpClient` over a shared, connection-pooling handler.

- **Create one and reuse it** for the lifetime of your application. Do **not** construct a
  client per request.
- The client is **thread-safe**: every method (including `SetApiKey`) may be called
  concurrently from multiple threads on the same instance.
- **Dispose** it on shutdown (`using`, or `IDisposable` via your container). Disposing one
  client never tears down the shared connection handler, so other clients keep working.
- The shared handler sets `PooledConnectionLifetime = 2min` and nothing else; there is no
  pool-size knob exposed (no equivalent of `httpx.Limits`) — if you need to tune pool size,
  you'd have to construct your own `HttpClient`/handler, which this SDK does not currently
  support.

### Dependency injection

Register it as a singleton:

```csharp
builder.Services.AddSingleton(_ => new PagrApiClient("pagr_..."));
```

### Options

Pass a `PagrClientOptions` to tune the per-request timeout (default: 30 seconds). The
timeout covers the full request/response exchange:

```csharp
using var client = new PagrApiClient(
    "pagr_...", options: new PagrClientOptions { Timeout = TimeSpan.FromSeconds(60) });
```

### Rotating the API key

Swap the key at runtime without recreating the client (e.g. after a key rotation). It
applies to all subsequent requests and is safe to call while other requests are in flight:

```csharp
client.SetApiKey("pagr_rotated...");
```

---

## 3. Passing document data

Every render and validate method accepts document data in **three interchangeable forms**,
selected by overload — they all produce identical wire payloads:

| Form | Example | When to use |
|------|---------|-------------|
| A serialisable object (POCO / anonymous type / `Dictionary<string, object>`) | `new { CustomerName = "Acme", Total = 42.50 }` | Most C# code |
| A `System.Text.Json.JsonElement` | `JsonSerializer.Deserialize<JsonElement>(raw)` | You already hold parsed JSON |
| A JSON string | `"""{ "customerName": "Acme" }"""` | You have raw JSON from a file/queue |

```csharp
// POCO / anonymous object
await client.RenderAsync(templateId, new { CustomerName = "Acme", Total = 42.50 });

// JsonElement
var element = JsonSerializer.Deserialize<JsonElement>(raw);
await client.RenderAsync(templateId, element);

// JSON string
await client.RenderAsync(templateId, """{ "customerName": "Acme" }""");
```

> **Property names are preserved exactly as declared.** The SDK does **not** camel-case your
> keys — `CustomerName` reaches the template as `CustomerName`. Name your POCO properties to
> match your template's bindings (or use `[JsonPropertyName]`).

> A malformed JSON **string** throws `System.Text.Json.JsonException` before any HTTP request
> is made. See [Error handling](#12-error-handling).


## 4. Rendering

### Single document

```csharp
RenderResult result = await client.RenderAsync(
    templateId,
    new { CustomerName = "Acme", Total = 42.50 },
    version: null,            // null (default) = latest published version; or an int
    includeDocument: true,    // return the PDF bytes inline (Base64)
    language: null,           // language variant for multilingual templates
    persist: true);           // store the render server-side

if (result.Ok)
    await result.Document!.SaveAsync(@"C:\out");     // directory → DocumentName + ".pdf"
else
    Console.WriteLine($"Render failed: {string.Join("; ", result.Issues)}");
```

`version` is optional on every render/validate method: omit it (or pass `null`) for the
latest published version, or pass a number to pin a specific one.

- Set `includeDocument: true` to get the PDF bytes back inline (`result.Document.ToBytes()` /
  `SaveAsync`). With `includeDocument: false` you get only metadata (id, name, view URL, page
  count, …) and download the bytes later via `DownloadDocumentAsync`.
- A failed render is **not** an exception — inspect `result.Ok`, `result.Issues`, and
  `result.Status`. See [Working with results](#9-working-with-results-and-documents).

### Batch (multiple documents in one request)

```csharp
var inputs = new[]
{
    new { CustomerName = "Acme",  Total = 42.50 },
    new { CustomerName = "Globex", Total = 99.00 },
};

BatchRenderResult batch = await client.RenderBatchAsync(templateId, inputs, includeDocument: true);

foreach (BatchItem item in batch)          // BatchRenderResult is enumerable + indexable
{
    if (item.Ok)
        Console.WriteLine($"[{item.Index}] {item.Document!.DocumentName}");
    else
        Console.WriteLine($"[{item.Index}] failed: {string.Join("; ", item.Issues)}");
}

// Convenience views:
IReadOnlyList<BatchItem> ok     = batch.Succeeded;
IReadOnlyList<BatchItem> failed = batch.Failed;

var written = await batch.SaveAllAsync(@"C:\out");   // writes only items with inline content
```

Each submitted input is correlated to its rendered document or the issues that prevented it.
`item.Input` carries the original data you submitted (as a `JsonElement?`).

**Correlation contract:** every rendered document reports its own `DocumentIndex` — the
zero-based position of the input that produced it — so it lands on exactly that slot. That
index is the only correlation: a document whose index is absent or out of range is dropped,
never matched by list position. Issues attach the same way via `RenderIssue.DocumentIndex`
(batch-wide issues, whose index is `null`, attach to every item). A slot left with neither a
document nor an issue gets a synthetic `Unknown`/`Error` "not rendered" issue.

`MissingCount` is `RequestedCount - RenderedCount` — computed by the SDK, since that
subtraction is the field's definition — and `Ok` is derived from it, so `Ok` answers "did
the whole batch render" while `Failed` answers it slot by slot.

### Non-persisted render

`persist: false` on a normal render skips server-side storage. You still get the ordinary
JSON envelope back — the document's `Id` and `ViewUrl` are simply `null`, since there is no
stored document to point at:

```csharp
var unstored = await client.RenderAsync(
    templateId, data, includeDocument: true, persist: false);

byte[] bytes = unstored.Document!.ToBytes();
// unstored.Document.Id      == null
// unstored.Document.ViewUrl == null
```

### Raw-PDF render (`RenderPdfAsync`)

`RenderPdfAsync` is the opt-in `Accept: application/pdf` path: instead of the JSON envelope,
the API streams the PDF binary directly and carries the document metadata in `X-Pagr-*`
response headers. Use it when you want the bytes without Base64-decoding a JSON field.

```csharp
PdfRenderResult result = await client.RenderPdfAsync(templateId, data);

if (result.Ok)
{
    PdfDocument doc = result.Document!;
    Console.WriteLine($"{doc.DocumentName}: {doc.PageCount} page(s), {doc.RenderDuration}ms");

    byte[] bytes = doc.ToBytes();
    string written = await doc.SaveAsync(@"C:\out");   // → C:\out\<DocumentName>.pdf
}
else
{
    // A blocked render has no PDF to stream: the API answers 422 with a JSON envelope,
    // which the SDK returns as data. This is never an exception.
    Console.WriteLine($"{result.Status}: {result.Message}");
    foreach (RenderIssue issue in result.Issues)
        Console.WriteLine(issue);
}
```

Only single-document renders are supported — the request always carries exactly one document
(the API rejects a raw-PDF request for a batch with HTTP 406). Takes the same
`version`/`language`/`persist`/`timeout` options as `RenderAsync`, but no `includeDocument`:
the bytes *are* the response body.

`PdfDocument` carries only what the raw-PDF response actually provides — the bytes plus the
`X-Pagr-*` header metadata. Fields the headers do not carry (template id, version,
environment, timestamp, type, language) are deliberately absent rather than fabricated, and
with `persist: false` both `DocumentId` and `ViewUrl` are `null`.

| Member | Meaning |
|--------|---------|
| `Ok` | `true` when a rendered PDF came back (`Document is not null`) |
| `Document` | the `PdfDocument`, or `null` when the render was blocked/failed |
| `Status` | `"ok"`, `"partial"`, `"failed"` or `"insufficient_credit"` |
| `InsufficientCredit` | `Status == "insufficient_credit"` |
| `Message` / `Issues` | why nothing rendered |


## 5. Validation

`ValidateAsync` checks document data against a template **without rendering** (and without
consuming render credit). The API returns a flat list of typed `RenderIssue`s. `IsValid` is
the **production gate**: it is `true` only when there is no `Warning`- or `Error`-severity
issue. For the narrower, Error-only check, inspect `Errors` directly.

```csharp
ValidationResponse validation = await client.ValidateAsync(templateId, data);

if (!validation.IsValid)
    foreach (RenderIssue issue in validation.Errors)
        Console.WriteLine(issue);   // "Error: MissingBinding [e1] — customerName is missing"
```

Validate a **batch** by passing an `IEnumerable<...>`, or a JSON string that encodes an
array — each element is treated as one document. Issues then carry the `DocumentIndex` they
pertain to:

```csharp
var validation = await client.ValidateAsync(templateId, """[{"a": 1}, {"a": 2}]""");

IReadOnlyList<RenderIssue> forDoc0 = validation.IssuesFor(0);   // includes batch-wide issues
```

| Member | Meaning |
|--------|---------|
| `IsValid` | the production gate: `true` when there are no `Warning`- or `Error`-severity issues |
| `Errors` | issues with severity `Error` |
| `Warnings` | issues with severity `Warning` |
| `IssuesFor(int documentIndex)` | issues for one document, **including** batch-wide issues (those with a `null` `DocumentIndex`) |

`ValidationResponse` is itself enumerable/indexable over all issues.


## 6. Templates and versions

```csharp
// List templates (paged) — optionally scoped to a project
PagedResult<Template> templates = await client.GetTemplatesAsync();
PagedResult<Template> inProject = await client.GetTemplatesAsync(projectId);

// A single template's catalogue metadata
Template template = await client.GetTemplateAsync(templateId);

// Versions of a template (paged)
PagedResult<TemplateVersion> versions = await client.GetTemplateVersionsAsync(templateId);

// A specific version, or the latest published one (version: null)
TemplateVersion latest = await client.GetTemplateVersionAsync(templateId);
TemplateVersion v3     = await client.GetTemplateVersionAsync(templateId, 3);

// Update (or clear, with null) a version's document-name pattern
TemplateVersion updated = await client.UpdateDocumentNameTemplateAsync(
    templateId, versionNumber: 1, documentNameTemplate: "Invoice {nr}");

// A version's preview image URL, if any
string? previewUrl = await client.GetPreviewImageUrlAsync(templateId, versionNumber: 1);
```

`TemplateVersion.TemplateJson` and `.Translations` are raw JSON **strings** (there is no typed
model for the template DSL). `.SampleData` is the one free-form field the SDK parses for you:
it comes back as a `JsonElement` matching the version's bindings, so it can be passed straight
into `RenderAsync`/`ValidateAsync` as a starting point, or inspected directly.

```csharp
TemplateVersion latest = await client.GetTemplateVersionAsync(templateId);

foreach (JsonProperty binding in latest.SampleData.EnumerateObject())
    Console.WriteLine(binding.Name);

var result = await client.RenderAsync(templateId, latest.SampleData);
```

Parsing is lenient: empty, malformed or non-object sample data all decode to an empty JSON
object (`{}`) rather than throwing, so a template author's broken sample data can never fail
an otherwise fine `GetTemplateVersionAsync` call.


## 7. Browsing rendered documents

```csharp
// List previously rendered documents (paged)
PagedResult<RenderDocument> docs = await client.GetDocumentsAsync(new ListOptions { Take = 10 });

// A single document's metadata
RenderDocument meta = await client.GetDocumentAsync(documentId);

// Download the PDF bytes
byte[] pdf = await client.DownloadDocumentAsync(documentId);
```

`RenderDocument.IsPdfDeleted` tells you whether the stored PDF has since been purged
server-side; `HasContent` tells you whether inline bytes are present without a separate
download.


## 8. Fonts, organisation stats, and meta

```csharp
IReadOnlyList<string> fonts = await client.GetFontsAsync();      // available font families

OrgStats stats = await client.GetOrgStatsAsync();                // usage + credit for the period
Console.WriteLine($"{stats.PagesAvailable} pages / {stats.TokensAvailable} AI tokens left");

bool healthy   = await client.GetStatusAsync();                  // true, or throws on 503
string? apiVer = await client.GetVersionAsync();                 // deployed API version
```

`GetStatusAsync` returns `true` when the service reports healthy and throws a
`PagrGenericApiException` otherwise.


## 9. Working with results and documents

### `RenderResult` (single render)

| Member | Meaning |
|--------|---------|
| `Ok` | `true` when a document rendered (`Document is not null`) |
| `Document` | the `RenderedDocument`, or `null` if it did not render |
| `Status` | API status string, e.g. `"ok"`, `"insufficient_credit"` |
| `InsufficientCredit` | `true` when the render stopped for lack of credit |
| `Issues` | flat list of `RenderIssue`s (filter by `Severity`) |
| `RenderedCount` / `RequestedCount` / `MissingCount` | counts |
| `Message` | optional human-readable message |

### `BatchRenderResult` (batch render)

Enumerable and indexable over `BatchItem`s. Plus:

| Member | Meaning |
|--------|---------|
| `Ok` | `true` when every requested document rendered and credit was sufficient |
| `Succeeded` / `Failed` | items that did / didn't render |
| `Documents` | all successfully rendered `RenderedDocument`s |
| `InsufficientCredit` | `true` when the batch stopped for lack of credit |
| `SaveAllAsync(directory)` | writes every item that carries inline content; returns the paths |

Each `BatchItem` has `Index`, `Input` (the original data), `Document`, `Issues`, and `Ok`.

### `RenderedDocument`

| Member | Meaning |
|--------|---------|
| `HasContent` | `true` when inline PDF bytes are present (rendered with `includeDocument: true`) |
| `ToBytes()` | decoded PDF bytes — **throws a `PagrApiException` subclass if there is no inline content** |
| `SaveAsync(path)` | writes the PDF. If `path` is an existing directory, uses `DocumentName` as the filename and appends `.pdf` unless the name already ends in `.pdf` (case-insensitive) — so `Invoice 2024.10` is written as `Invoice 2024.10.pdf`. Returns the path written |

Metadata: `Id`, `DocumentName`, `TemplateId`, `VersionNumber`, `Environment`,
`FileSizeBytes`, `PageCount`, `RenderedAt`, `RenderDuration` (ms), `ViewUrl`, `DocumentType`.

```csharp
if (result.Document is { HasContent: true } doc)
{
    byte[] bytes = doc.ToBytes();
    string path  = await doc.SaveAsync(@"C:\out");   // C:\out\<DocumentName>.pdf
}
```


## 10. Paging, sorting, and filtering

List endpoints (`GetTemplatesAsync`, `GetTemplateVersionsAsync`, `GetDocumentsAsync`) accept
a `ListOptions` and return a `PagedResult<T>`:

```csharp
var page = await client.GetTemplatesAsync(new ListOptions
{
    Skip = 0,
    Take = 20,
    SortBy = "name",
    SortDirection = SortDirection.Ascending,
    Search = "invoice",
    Filters =
    [
        Filter.Eq("project.guid", projectId),            // Guid overload; equality
        new Filter("name", FilterOp.Contains, "quote"),  // explicit operator
    ],
});
```

Filters are validated client-side against the calling endpoint's allowed
field/operator table, so a typo or a field the endpoint doesn't support throws
`ArgumentException` instead of silently returning the **unfiltered** result set —
which is what the server does with a filter it doesn't recognise. The fields each
endpoint accepts:

| Endpoint | Filterable fields |
|---|---|
| `GetTemplatesAsync` (both overloads) | `name`, `project.guid`, `createdAt`, `updatedAt` |
| `GetTemplateVersionsAsync` | `versionNumber`, `publishedAt`, `createdAt`, `updatedAt` |
| `GetDocumentsAsync` | `documentName`, `template.guid`, `versionNumber`, `fileSizeBytes`, `pageCount`, `renderedAt`, `createdAt`, `updatedAt`, `environment`, `language` |

Operators are constrained per field too: id/guid fields take `Eq` only, text
fields `Eq`/`Contains`, numeric and datetime fields `Eq`/`Gt`/`Gte`/`Lt`/`Lte`,
and closed-vocabulary fields (`environment`, `language`) `Eq`/`Neq`. The
`ArgumentException` message names the allowed set for the field you got wrong.

`PagedResult<T>` is enumerable/indexable over `Items`, and exposes `Total` (across all
pages), `Skip`, `Take`, `Count` (this page), and `HasMore`.

**Walking every page** — advance `Skip` by the number of items returned until `HasMore` is
false:

```csharp
var all = new List<Template>();
var skip = 0;
while (true)
{
    var page = await client.GetTemplatesAsync(new ListOptions { Skip = skip, Take = 50 });
    all.AddRange(page.Items);
    if (!page.HasMore)
        break;
    skip += page.Count;
}
```

### Filter operators

`FilterOp` values (serialised lowercase on the wire):

| `FilterOp` | Wire | Meaning |
|-----------|------|---------|
| `Eq` | `eq` | equal to |
| `Neq` | `neq` | not equal to |
| `Gt` | `gt` | greater than |
| `Gte` | `gte` | greater than or equal |
| `Lt` | `lt` | less than |
| `Lte` | `lte` | less than or equal |
| `Contains` | `contains` | substring match |

`new Filter(field, value)` is shorthand for `new Filter(field, FilterOp.Eq, value)`.



## 11. Async jobs: webhooks and polling

`EnqueueBatchRenderAsync` is a fire-and-forget batch render. It returns immediately with a
job reference; the Pagr server renders in the background and POSTs webhooks to your
`callbackUrl`. You can react to webhooks, poll for status, or both.

```csharp
RenderJob job = await client.EnqueueBatchRenderAsync(templateId, inputs, callbackUrl);
```

### Receiving webhooks

The server sends **N + 1 callbacks**: one `RenderProgress` per rendered document, then one
final `RenderCompletion`. Every callback carries three headers:

| Header | Value |
|---|---|
| `X-Pagr-Signature` | `t=<unix seconds>,v1=<hex>[,v1=<hex>]` — see [Verifying the signature](#verifying-the-signature) |
| `X-Pagr-Event` | `render.progress`, `render.completed` or `render.failed` |
| `X-Pagr-Delivery` | Stable id for one logical delivery; **retries repeat it**, so deduplicate on it |

Verify the signature and parse the body in one call with
`WebhookSignature.ParseSignedCallback` — the preferred entry point:

```csharp
using Pagr.Sdk.Webhooks;

// In your webhook endpoint, working from the RAW request bytes:
RenderCallback callback = WebhookSignature.ParseSignedCallback(
    rawBodyBytes,                                  // byte[] / ReadOnlySpan<byte> as received
    request.Headers[WebhookSignature.HeaderName],   // "X-Pagr-Signature"
    webhookSecret);

switch (callback)
{
    case RenderProgress p:
        Console.WriteLine($"{p.ProgressPct:F0}% — {p.Document.DocumentName}");
        break;
    case RenderCompletion c:
        Console.WriteLine($"done: state={c.State} status={c.Status} ({c.RenderedCount}/{c.RequestedCount})");
        break;
}
```

`RenderCallback.Parse(string)` / `Parse(JsonElement)` still exist for an already-verified (or
already-parsed) body, but they only decode — they say nothing about who sent the POST. Both
throw `PagrDecodeException` if the body is not valid JSON or matches neither the progress nor
the completion shape.

> **Webhook delivery characteristics:** every callback is **signed** (`X-Pagr-Signature`);
> delivery is **retried up to 5 attempts** with exponential backoff from 2 s and a **30-second
> timeout per attempt**, and deliveries run with bounded concurrency (16). So callbacks can
> arrive **out of order** *and* **more than once** — make your endpoint fast and
> **idempotent**, deduplicate on `X-Pagr-Delivery`, correlate progress callbacks by
> `DocumentIndex` rather than arrival order, and treat polling as the authoritative fallback.

### Verifying the signature

Each `v1` is `HMAC-SHA256(secret, "{t}.{rawBody}")` in lowercase hex. The timestamp is inside
the signed material, so rejecting an old `t` also rejects replays of a captured delivery.
A second `v1` appears only while a rotated-out secret is inside its 24-hour grace period;
verification succeeds when **any** `v1` matches, so you can move to a new secret without
dropping deliveries.

**Verify against the raw request body bytes.** The digest covers the exact bytes that were
POSTed. A body that was deserialised and re-serialised — even to JSON with identical values —
will not reproduce the digest, because key order, escaping and whitespace change. This is by
far the most common cause of a signature that "should" match but doesn't. In ASP.NET Core,
read the body yourself before any model binding turns it into an object:

```csharp
app.MapPost("/pagr-callback", async (HttpRequest request) =>
{
    using var buffer = new MemoryStream();
    await request.Body.CopyToAsync(buffer);          // raw bytes, exactly as received

    RenderCallback callback;
    try
    {
        callback = WebhookSignature.ParseSignedCallback(
            buffer.ToArray(),
            request.Headers[WebhookSignature.HeaderName],
            webhookSecret);
    }
    catch (PagrSignatureException)
    {
        return Results.BadRequest();                 // not from Pagr — do not act on it
    }

    if (!TryClaimDelivery(request.Headers["X-Pagr-Delivery"]))
        return Results.Ok();                         // a retry of a callback already handled

    // ... handle the callback
    return Results.Ok();
});
```

Use `WebhookSignature.Verify(...)` instead when you need to check the signature without
parsing (an intermediate proxy, say). It returns `void` and throws on every failure, so a
caller who ignores the result still fails closed:

```csharp
WebhookSignature.Verify(rawBodyBytes, signatureHeader, webhookSecret);            // 5-min window
WebhookSignature.Verify(rawBodyBytes, signatureHeader, webhookSecret,
    tolerance: TimeSpan.FromMinutes(2), now: myClock.UtcNow);                     // custom window/clock
```

- **Failures throw `PagrSignatureException`** (a `PagrApiException`): no header, a malformed
  header, a `t` outside the tolerance (default `WebhookSignature.DefaultTolerance`, 5 minutes,
  absolute drift in both directions), or no `v1` matching your secret. There is no bool to
  drop and no "no secret configured → pass through" mode.
- **A missing or empty `secret` throws `ArgumentException`**, not `PagrSignatureException`:
  an unset environment variable is a misconfiguration of *your* receiver and must stay
  distinguishable from a forged callback.
- An unknown scheme version (a future `v2=`) beside `v1=` is ignored, not treated as malformed.

The signing secret is per organisation: copy it from **Settings → API keys** in the Pagr web
app and store it with your other credentials. It is deliberately **not** exposed on the public
`/v1` API, so no client method fetches it.

### Polling

```csharp
RenderJobStatus status = await client.GetJobStatusAsync(job.JobId);
if (status.Done)
    Console.WriteLine(status.Ok ? "completed" : $"failed: {status.FailureReason}");

// Or use the pre-written poll loop:
RenderJobStatus finalStatus = await client.WaitForJobAsync(job.JobId, pollInterval: TimeSpan.FromSeconds(2));
```

Poll until `status.Done` — `State` is the lifecycle (`Queued`/`Pending` are non-terminal;
`Completed`/`Failed`/`Unknown` are terminal, the last as a fail-open fallback for a state
this SDK version does not recognise). `Status` is the render *outcome*
(`Ok`/`Partial`/`Failed`/`InsufficientCredit`/`Unknown`), `null` while the job is pending;
`status.Ok` is `true` only when `State == Completed && Status == RenderOutcome.Ok`.
`WaitForJobAsync` throws `PagrTimeoutException` if its own `timeout` elapses first.

**That `timeout` defaults to `PagrApiClient.DefaultWaitForJobTimeout` (5 minutes)**, so a job
that never reaches a terminal state — a stuck server, a lost webhook, a bug — cannot hang the
caller forever. Pass `Timeout.InfiniteTimeSpan` to opt out and poll with no deadline at all
(still cancellable via the `CancellationToken`):

```csharp
await client.WaitForJobAsync(job.JobId);                              // 5-minute deadline
await client.WaitForJobAsync(job.JobId, timeout: Timeout.InfiniteTimeSpan);  // no deadline
```

If you were previously passing a large sentinel expecting unbounded polling, this default is a
breaking change — use `Timeout.InfiniteTimeSpan` explicitly.


## 12. Error handling

Every API **error response** (any 4xx/5xx) throws a subclass of `PagrApiException`. Transport
failures are wrapped too, so a single `catch (PagrApiException)` handles everything the SDK
can throw:

```csharp
using Pagr.Sdk.Exceptions;

try
{
    var result = await client.RenderAsync(templateId, data);
}
catch (PagrRateLimitException ex)          // 429 — never retried, reflects your own call volume
{
    // back off using ex.RetryAfter (seconds), if present
}
catch (PagrApiException ex)                 // any other API error or transport failure
{
    Console.WriteLine($"HTTP {ex.StatusCode?.ToString() ?? "n/a"} ({ex.Code}): {ex.Message}");
}
```

| HTTP status | Exception |
|-------------|-----------|
| 401 | `PagrAuthenticationException` |
| 403 | `PagrForbiddenException` |
| 404 | `PagrNotFoundException` |
| 413 | `PagrPayloadTooLargeException` |
| 422 | `PagrValidationFailedException` |
| 429 | `PagrRateLimitException` (carries `RetryAfter`) |
| any other 4xx/5xx | `PagrGenericApiException` |
| request timed out | `PagrTimeoutException` |
| connection/DNS failure | `PagrConnectionException` |
| response body could not be decoded (non-JSON, missing required field) | `PagrDecodeException` |
| webhook callback failed signature verification (not an API response) | `PagrSignatureException` |

`PagrApiException.StatusCode` carries the HTTP status and `PagrApiException.Code` the API
error code, read from the `{ "error": { "code", "message" } }` envelope (falling back to the
raw body when the response is not that shape). `StatusCode`/`Code` are `null` for the
transport-failure subclasses (`PagrTimeoutException`, `PagrConnectionException`), since those
never got an HTTP response.

`PagrApiException` is **abstract**: it is purely the catch-all supertype and is never itself
thrown. A status with no dedicated subclass surfaces as `PagrGenericApiException`, so
`catch (PagrGenericApiException)` means exactly "an unmapped failure" and can never silently
swallow a subclass you meant to handle separately.

### What is *not* a `PagrApiException`

- **Caller-initiated cancellation** of a `CancellationToken` you passed in propagates as a
  plain `OperationCanceledException` (never wrapped) — only the SDK's *own* per-request
  timeout becomes `PagrTimeoutException`.
- **Malformed caller-supplied JSON strings** (e.g. an invalid `jsonData` string you pass to
  `RenderAsync`) → `System.Text.Json.JsonException`, thrown before any request is sent.
- **A missing/empty webhook signing secret** passed to `WebhookSignature.Verify` /
  `ParseSignedCallback` → `ArgumentException`: a misconfigured receiver, not an untrustworthy
  callback (which is `PagrSignatureException`).

### Business outcomes are data, not exceptions

Failed validation, insufficient credit, and per-document render failures are returned as
**data on the result objects**, never thrown:

```csharp
var result = await client.RenderAsync(templateId, data);
if (result.InsufficientCredit)   // NOT an exception
    Console.WriteLine("Out of render credit.");
```

Inspect `RenderResult.InsufficientCredit` / `BatchRenderResult.InsufficientCredit`,
`result.Ok`, and `result.Issues`.


## 13. Cancellation, timeouts and retries

Every asynchronous method takes an optional `CancellationToken` as its last argument:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
var result = await client.RenderAsync(templateId, data, cancellationToken: cts.Token);
```

The per-request timeout defaults to `PagrClientOptions.Timeout` (30s); several methods
(`RenderAsync`, `RenderPdfAsync`, `RenderBatchAsync`,
`EnqueueBatchRenderAsync`, `DownloadDocumentAsync`) also accept a `timeout` parameter to override it for that one call:

```csharp
var result = await client.RenderAsync(templateId, data, timeout: TimeSpan.FromSeconds(90));
```

When the SDK's own timeout fires, it throws `PagrTimeoutException`; cancelling your own
`CancellationToken` instead propagates as a plain `OperationCanceledException`.

**Idempotent GET requests are retried automatically** on transient failures — HTTP
500/502/503/504, timeouts, and connection errors — with capped exponential backoff and full
jitter, honoring a `Retry-After` response header when present. Configure the retry count via
`PagrClientOptions.MaxRetries` (default 2; `0` disables retries):

```csharp
using var client = new PagrApiClient(apiKey, baseUrl, new PagrClientOptions { MaxRetries = 4 });
```

**429 is never retried** (it reflects your own request volume — catch
`PagrRateLimitException` and use `RetryAfter` yourself), and **writes (POST/PATCH — render,
validate, enqueue, template edits) are never retried**, since the API has no idempotency keys
and a request that was applied but whose response was lost must not be repeated.


## 14. Model reference

Types live in `Pagr.Sdk.Models` unless noted. Timestamps are always `DateTimeOffset`
(timezone-aware; offset-less API timestamps are interpreted as UTC).

### `PagrClientOptions` (namespace `Pagr.Sdk`)
`Timeout` (`TimeSpan`, default `DefaultTimeout` = 30s), `MaxRetries` (`int`, default
`DefaultMaxRetries` = 2). The base URL and API key are constructor arguments, not options.

### `PagrApiClient` constants (namespace `Pagr.Sdk`)
`DefaultBaseUrl` (the hosted API), and `DefaultWaitForJobTimeout` (`TimeSpan`, 5 minutes) —
the deadline `WaitForJobAsync` applies when you pass no `timeout`. Pass
`Timeout.InfiniteTimeSpan` to poll without one; see [§13](#13-cancellation-timeouts-and-retries).

### `Template`
`Id`, `Name`, `DocumentNameTemplate?`, `ProjectId?`, `ProjectName?`, `LatestVersionNumber?`,
`VersionCount`, `UpdatedAt?`, `UpdatedBy?`, `MasterTemplateId?`, `MasterTemplateName?`.

### `TemplateVersion`
`Id`, `VersionNumber`, `TemplateJson` (raw), `SampleData` (parsed `JsonElement`),
`DocumentNameTemplate?`, `PublishedAt?`, `PublishedBy?`, `TemplateId`, `UpdatedAt?`,
`Translations?` (raw).

### `RenderResult`
`Ok`, `Document?`, `Status`, `InsufficientCredit`, `Issues`, `RenderedCount`,
`RequestedCount`, `MissingCount`, `Message?`.

### `BatchRenderResult` : `IReadOnlyList<BatchItem>`
`Items`, `Ok`, `Succeeded`, `Failed`, `Documents`, `Status`, `Message?`, `RequestedCount`,
`RenderedCount`, `MissingCount` (computed as `RequestedCount - RenderedCount`, not read from
the response), `InsufficientCredit`, `SaveAllAsync(directory, ct)`.

### `BatchItem`
`Index`, `Input?` (`JsonElement?`), `Document?`, `Issues`, `Ok`.

### `RenderedDocument`
`Id?`, `DocumentName`, `TemplateId`, `VersionNumber`, `Environment`, `FileSizeBytes`,
`PageCount`, `RenderedAt`, `RenderDuration`, `ViewUrl?`, `DocumentType`, `DocumentBase64?`,
`Language?`, `DocumentIndex?`, `HasContent`, `ToBytes()`, `SaveAsync(path, ct)`. `Id`/`ViewUrl`
are `null` when the render was made with `persist: false` (nothing was stored). `Language?` is
the language variant the document was rendered in, for templates with translations.

### `RenderDocument` (document-browsing model)
As `RenderedDocument`, but `Id`/`ViewUrl` are non-nullable (only persisted renders are
listed), plus `IsPdfDeleted`.

### `PdfDocument` (raw-PDF render, from `RenderPdfAsync`)
`DocumentName`, `Content`, `DocumentId?`, `PageCount`, `RenderDuration`, `ViewUrl?`,
`IssueCount`, `ToBytes()`, `SaveAsync(path, ct)`. Built from the response body plus the
`X-Pagr-*` headers; `DocumentId`/`ViewUrl` are `null` for a non-persisted render.

### `PdfRenderResult`
`Ok`, `Document?` (`PdfDocument`), `Status`, `Message?`, `Issues`, `InsufficientCredit`.

### `RenderIssue`
`Type` (`RenderIssueType`), `Severity` (`RenderIssueSeverity`), `Description`, `ElementId?`,
`DocumentIndex?`, `IsError`.

- **`RenderIssueSeverity`**: `Information`, `Warning`, `Error`. Unknown/missing values from
  the server **fail closed to `Error`**. Extension methods: `IsAtLeast(other)`,
  `IsBlockingProduction()` (true for `Warning`/`Error`).
- **`RenderIssueType`**: e.g. `InvalidJson`, `SchemaInvalid`, `MissingBinding`,
  `UnresolvedImage`, `RenderTimeout`, … Unknown values **fail open to `Unknown`**, so new
  server behaviour never breaks an older client.

### `ValidationResponse` : `IReadOnlyList<RenderIssue>`
`Issues`, `IsValid`, `Errors`, `Warnings`, `IssuesFor(documentIndex)`.

### `RenderJob`
`JobId`, `RequestedCount`, `State` (`RenderJobState`, normally `Queued` on creation).

### `RenderJobStatus`
`JobId`, `State` (`RenderJobState`), `Status?` (`RenderOutcome`, `null` while pending),
`RenderedCount`, `RequestedCount`, `MissingCount`, `Issues`, `StartedAt`, `CompletedAt?`,
`FailureReason?`, `Done`, `Ok`, `InsufficientCredit`.

- **`RenderJobState`**: `Queued`, `Pending` (non-terminal), `Completed`, `Failed`,
  `Unknown` (terminal — a fail-open fallback for a lifecycle value this SDK version does not
  recognise, so a poll loop can never spin forever). `IsTerminal()` extension method.
- **`RenderOutcome`**: `Ok`, `Partial`, `Failed`, `InsufficientCredit`, `Unknown` (fail-open).

### `OrgStats`
`OrganisationName?`, `Tier?`, `PeriodStart?`, `PeriodEnd?`, `IncludedRendersPerMonth?`,
`PagesUsedThisPeriod?`, `PagesAvailable?`, `IncludedTokensPerMonth?`, `TokensUsedThisPeriod?`,
`TokensAvailable?`, `UserCount?`. The usage/count fields are `null` when the server omits
them — distinct from a genuine `0`.

### `PagedResult<T>` : `IReadOnlyList<T>`
`Items`, `Total`, `Skip`, `Take`, `Count`, `HasMore`.

### `ListOptions` (namespace `Pagr.Sdk`)
`Skip?`, `Take?`, `SortBy?`, `SortDirection?`, `Filters?` (`IReadOnlyList<Filter>`),
`Search?`. Filters are validated per endpoint against a canonical field/operator table;
an unknown field or operator throws `ArgumentException` rather than silently returning the
unfiltered result set (which is what the server does).

### `Filter`, `FilterOp`, `SortDirection` (namespace `Pagr.Sdk`)
`Filter(field, value)` or `Filter(field, op, value)` (string values); typed factories
`Filter.Eq(field, Guid)`, `Filter.Eq/Gt/Gte/Lt/Lte(field, DateTimeOffset)` coerce GUID/date
values to their wire string form. `SortDirection` is `Ascending` / `Descending`. See
[Filter operators](#filter-operators).

### Webhooks (namespace `Pagr.Sdk.Webhooks`)
- `WebhookSignature` — static; `HeaderName` (`"X-Pagr-Signature"`), `DefaultTolerance`
  (5 minutes), and
  `Verify(rawBody, signatureHeader, secret, tolerance?, now?)` /
  `ParseSignedCallback(rawBody, signatureHeader, secret, tolerance?, now?)`. `rawBody` is a
  `ReadOnlySpan<byte>` (so a `byte[]` works directly) or a `string`, and must be the **raw**
  body as received. `Verify` returns `void`; both throw `PagrSignatureException` on any
  unproven callback and `ArgumentException` on a missing secret. See
  [§11](#11-async-jobs-webhooks-and-polling).
- `RenderCallback` — abstract base; `Parse(string)` / `Parse(JsonElement)` return the right
  subtype, throwing `PagrDecodeException` if the body matches neither shape. Decoding only —
  prefer `WebhookSignature.ParseSignedCallback`, which authenticates the sender first.
- `RenderProgress` — `JobId`, `Processed`, `RequestedCount`, `DocumentIndex`, `Document`,
  `ProgressPct`.
- `RenderCompletion` — `JobId`, `State` (`RenderJobState`, terminal), `Status`
  (`RenderOutcome`), `RenderedCount`, `RequestedCount`, `MissingCount`, `Issues`, `Message?`,
  `Ok`, `InsufficientCredit`.


## 15. Known limitations

- **`RenderPdfAsync` is single-document only.** The raw-PDF response can carry exactly one
  document, so there is no batch equivalent; the API rejects a raw-PDF request for a batch
  with HTTP 406. Use `RenderBatchAsync` (JSON envelope, `includeDocument: true`) for batches.
- **The template DSL has no typed model.** `TemplateVersion.TemplateJson` and
  `.Translations` are raw JSON strings. `.SampleData` is the one free-form field the SDK
  parses (into a `JsonElement`), because it is arbitrary caller data with no schema to model.

