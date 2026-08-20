using System.Text.Json;
using Pagr.Sdk.Exceptions;

namespace Pagr.Sdk.Webhooks;

/// <summary>
/// A webhook payload POSTed by the Pagr server during an async render: either a
/// per-document <see cref="RenderProgress"/> or the final <see cref="RenderCompletion"/>.
/// </summary>
/// <remarks>
/// <para>
/// A job produces N+1 callbacks: one <see cref="RenderProgress"/> per rendered document plus one
/// final <see cref="RenderCompletion"/>. Every callback is signed
/// (<c>X-Pagr-Signature</c>) and also carries <c>X-Pagr-Event</c> and <c>X-Pagr-Delivery</c>.
/// Delivery is retried (up to 5 attempts, exponential backoff from 2s, 30s per attempt) and runs
/// with bounded concurrency, so callbacks can arrive <b>out of order</b> and <b>more than
/// once</b> — deduplicate on <c>X-Pagr-Delivery</c>, which repeats across retries of the same
/// logical delivery.
/// </para>
/// <para>
/// <see cref="Parse(string)"/> only decodes; it does not authenticate the sender. Prefer
/// <see cref="WebhookSignature.ParseSignedCallback(ReadOnlySpan{byte}, string?, string, TimeSpan?, DateTimeOffset?)"/>,
/// which verifies the signature over the raw request bytes and then parses.
/// </para>
/// </remarks>
public abstract class RenderCallback
{
    /// <summary>Shared (de)serialisation options for webhook payloads.</summary>
    internal static readonly JsonSerializerOptions SerializerOptions = PagrJson.Options;

    private protected RenderCallback() { }

    /// <summary>
    /// Parses an incoming async-render webhook body into the right typed object.
    /// </summary>
    /// <remarks>
    /// Progress callbacks carry a <c>document</c>; the final completion callback does not
    /// (it carries <c>status</c>/<c>renderedCount</c>/<c>requestedCount</c>).
    /// </remarks>
    /// <param name="json">The JSON body POSTed to the callback URL.</param>
    /// <returns>A <see cref="RenderProgress"/> for per-document callbacks, or a <see cref="RenderCompletion"/> for the final callback.</returns>
    /// <exception cref="PagrApiException">Thrown when the body is not valid JSON.</exception>
    public static RenderCallback Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return Parse(doc.RootElement);
        }
        catch (JsonException ex)
        {
            throw new PagrDecodeException("Failed to parse webhook payload as JSON.", innerException: ex);
        }
    }

    /// <summary>
    /// Parses an already-decoded async-render webhook body into the right typed object.
    /// </summary>
    /// <remarks>
    /// The full expected shape is validated before dispatch, so a payload matching neither
    /// shape raises <see cref="PagrDecodeException"/> rather than being silently mis-parsed
    /// into a bogus-but-valid-looking completion.
    /// </remarks>
    /// <param name="json">The decoded JSON body POSTed to the callback URL.</param>
    /// <returns>A <see cref="RenderProgress"/> for per-document callbacks, or a <see cref="RenderCompletion"/> for the final callback.</returns>
    /// <exception cref="PagrDecodeException">
    /// <paramref name="json"/> is not a JSON object, or matches neither the progress nor the
    /// completion shape.
    /// </exception>
    public static RenderCallback Parse(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object)
            throw new PagrDecodeException($"Webhook payload must be a JSON object, not {json.ValueKind}.");

        var hasDocument = json.TryGetProperty("document", out var document)
            && document.ValueKind != JsonValueKind.Null
            && document.ValueKind != JsonValueKind.Undefined;

        if (hasDocument)
        {
            RequireKeys(json, "progress", "jobId", "processed", "requestedCount", "documentIndex");
            return RenderProgress.FromJson(json);
        }
        RequireKeys(json, "completion", "jobId", "state", "status");
        return RenderCompletion.FromJson(json);
    }

    /// <summary>Throws <see cref="PagrDecodeException"/> if <paramref name="json"/> is missing any of <paramref name="keys"/>.</summary>
    private static void RequireKeys(JsonElement json, string shape, params string[] keys)
    {
        var missing = keys.Where(k => !json.TryGetProperty(k, out _)).ToList();
        if (missing.Count > 0)
        {
            throw new PagrDecodeException(
                $"Webhook payload looks like a {shape} callback but is missing required field(s): " +
                string.Join(", ", missing));
        }
    }
}
