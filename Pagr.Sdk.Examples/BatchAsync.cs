using Pagr.Sdk.Webhooks;

namespace Pagr.Sdk.Examples;

/// <summary>Fire-and-forget batch render with webhook callbacks, plus job status polling.</summary>
internal static class BatchAsync
{
    public static async Task RunAsync()
    {
        using var client = ExampleEnv.CreateClient();
        if (client is null)
            return;

        if (await ExampleData.PickPublishedTemplateAsync(client) is not var (template, version))
            return;
        var inputs = Enumerable.Repeat(version.SampleData, 5).ToList();

        // A publicly reachable URL the Pagr server POSTs webhooks to. For local
        // experiments, a https://webhook.site inbox works well.
        var callbackUrl = Environment.GetEnvironmentVariable("PAGR_WEBHOOK_URL")
            ?? "https://example.invalid/pagr-callback";

        var job = await client.EnqueueBatchRenderAsync(
            template.Id, inputs, callbackUrl, version: version.VersionNumber);
        Console.WriteLine($"Enqueued job {job.JobId}: {job.RequestedCount} document(s), state={job.State}");

        // The server POSTs N+1 callbacks to callbackUrl: one RenderProgress per rendered
        // document plus a final RenderCompletion. Each is signed with the organisation's
        // webhook secret (Settings → API keys), so in your webhook endpoint verify it over
        // the RAW request bytes — a re-serialized body never reproduces the digest:
        //
        //   var callback = WebhookSignature.ParseSignedCallback(
        //       rawBodyBytes, request.Headers[WebhookSignature.HeaderName], secret);
        //   switch (callback)
        //   {
        //       case RenderProgress p:   /* p.ProgressPct, p.Document */          break;
        //       case RenderCompletion c: /* c.Ok, c.RenderedCount, c.MissingCount */ break;
        //   }
        //
        // Deliveries are retried (5 attempts, exponential backoff), so a callback can arrive
        // more than once and out of order: dedupe on the X-Pagr-Delivery header and correlate
        // documents by DocumentIndex, not arrival order.

        // Polling is a reliable alternative (or complement) to webhooks — or use
        // client.WaitForJobAsync(job.JobId) for the same loop pre-written:
        while (true)
        {
            var status = await client.GetJobStatusAsync(job.JobId);
            Console.WriteLine($"  state={status.State} status={status.Status}: {status.RenderedCount} rendered");
            if (status.Done)
            {
                Console.WriteLine(status.Ok
                    ? $"Job completed at {status.CompletedAt}"
                    : $"Job failed: {status.FailureReason}");
                break;
            }
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }
}
