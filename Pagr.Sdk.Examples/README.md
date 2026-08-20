# Pagr .NET SDK examples

Runnable examples, one per topic. Start with `getting-started` and pick the others as
you need them.

| Example | What it shows |
|---|---|
| [`GettingStarted.cs`](GettingStarted.cs) | Connect, pick a template, render a document, save the PDF. |
| [`Templates.cs`](Templates.cs) | Browse templates and versions: paging, search, filters, document-name template, preview image. |
| [`RenderSingle.cs`](RenderSingle.cs) | Single render options: issues, language override, `persist: false`, JSON-string data. |
| [`RenderBatch.cs`](RenderBatch.cs) | Synchronous batch render: succeeded/failed items, `SaveAllAsync`. |
| [`BatchAsync.cs`](BatchAsync.cs) | Fire-and-forget batch render with webhook callbacks, plus job status polling. |
| [`Validate.cs`](Validate.cs) | Data validation: severity levels, per-document issues, test vs. production keys. |
| [`Documents.cs`](Documents.cs) | Listing and downloading previously rendered documents. |
| [`Account.cs`](Account.cs) | Organisation usage stats, available fonts, API key rotation. |
| [`ErrorHandling.cs`](ErrorHandling.cs) | The exception hierarchy and when to catch what. |

## Setup

Copy [`.env.example`](.env.example) to `.env` in this directory (or set the same
environment variables):

```env
TEST_KEY_PUBLIC=your-test-api-key
PROD_KEY_PUBLIC=your-prod-api-key     # only needed by 'validate'
PAGR_WEBHOOK_URL=https://webhook.site/...   # only used by 'batch-async'
PAGR_BASE_URL=https://api.pagr.eu     # optional; defaults to the hosted Pagr API
```

> [!NOTE]
> These examples call the **live** Pagr API. A `pagr_test_*` key produces
> watermarked output and caps batches at 10 documents; a `pagr_prod_*` key
> renders for real and consumes credit.

Then run any example by name:

```bash
dotnet run --project Pagr.Sdk.Examples -- getting-started
```

Run without arguments to list all example names. Rendered PDFs are written to the build
output's `test_output/` directory.

> These examples hit a live Pagr API — they are not part of the automated test suite.
