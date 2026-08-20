using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Pagr.Sdk.Exceptions;

namespace Pagr.Sdk.Webhooks;

/// <summary>
/// Verifies the <c>X-Pagr-Signature</c> header Pagr puts on every async-render webhook
/// callback, so your endpoint acts only on callbacks that genuinely came from Pagr rather than
/// on any POST that reaches the listening URL.
/// </summary>
/// <remarks>
/// <para>
/// The header is <c>t=&lt;unix seconds&gt;,v1=&lt;hex&gt;[,v1=&lt;hex&gt;]</c>, where each
/// <c>v1</c> is <c>HMAC-SHA256(secret, "{t}.{rawBody}")</c> in lowercase hex. The timestamp is
/// <em>inside</em> the signed material, so rejecting an old <c>t</c> also rejects replays of a
/// captured delivery. More than one <c>v1</c> appears only while a rotated-out secret is still
/// inside its grace period — verification succeeds when <em>any</em> <c>v1</c> matches, so a
/// receiver can move to a new secret without dropping deliveries.
/// </para>
/// <para>
/// <b>Always verify against the raw request body bytes.</b> The digest covers the exact bytes
/// that were POSTed. A body that was deserialised and then re-serialised — even to JSON with
/// the identical values — will not reproduce the digest, because key order, escaping and
/// whitespace change. This is by far the most common cause of a signature that "should" match
/// but doesn't. In ASP.NET Core, buffer <c>HttpRequest.Body</c> yourself (or enable
/// <c>EnableBuffering()</c>) and hand those bytes to this class <em>before</em> any model
/// binding turns them into an object.
/// </para>
/// <para>
/// The signing secret is per organisation: copy it from <b>Settings → API keys</b> in the Pagr
/// web app and keep it wherever you keep credentials. It is not available over the public
/// <c>/v1</c> API, so there is no client method that fetches it.
/// </para>
/// <para>
/// Prefer <see cref="ParseSignedCallback(ReadOnlySpan{byte}, string?, string, TimeSpan?, DateTimeOffset?)"/>
/// over calling <see cref="Verify(ReadOnlySpan{byte}, string?, string, TimeSpan?, DateTimeOffset?)"/>
/// and <see cref="RenderCallback.Parse(string)"/> separately: it takes the raw body (the only
/// form the signature can be checked against) and decodes the JSON itself, so verification
/// cannot be forgotten or run in the wrong order.
/// </para>
/// <example>
/// <code>
/// // Minimal ASP.NET Core endpoint.
/// app.MapPost("/pagr-callback", async (HttpRequest request) =&gt;
/// {
///     using var buffer = new MemoryStream();
///     await request.Body.CopyToAsync(buffer);          // raw bytes, exactly as received
///
///     RenderCallback callback;
///     try
///     {
///         callback = WebhookSignature.ParseSignedCallback(
///             buffer.ToArray(),
///             request.Headers[WebhookSignature.HeaderName],
///             secret);
///     }
///     catch (PagrSignatureException)
///     {
///         return Results.BadRequest();                 // not from Pagr — do not act on it
///     }
///
///     // Deliveries are retried, so the same callback can arrive more than once:
///     // deduplicate on the X-Pagr-Delivery header before acting.
///     return Results.Ok();
/// });
/// </code>
/// </example>
/// </remarks>
public static class WebhookSignature
{
    /// <summary>Name of the header carrying the signature.</summary>
    public const string HeaderName = "X-Pagr-Signature";

    /// <summary>
    /// Name of the header carrying the event type: <c>render.progress</c>,
    /// <c>render.completed</c> or <c>render.failed</c>.
    /// </summary>
    public const string EventHeaderName = "X-Pagr-Event";

    /// <summary>
    /// Name of the header carrying the stable id for one logical delivery.
    /// </summary>
    /// <remarks>
    /// Retries repeat the id, so a receiver deduplicates on it. This is not optional
    /// housekeeping: the server makes up to 5 delivery attempts, so a handler that does not
    /// deduplicate will process the same callback more than once.
    /// </remarks>
    public const string DeliveryHeaderName = "X-Pagr-Delivery";

    /// <summary>
    /// How far the signed timestamp may drift from the current time before a callback is
    /// rejected: 5 minutes.
    /// </summary>
    /// <remarks>
    /// Bounds how long a captured callback stays replayable; wide enough to absorb clock skew
    /// and the sender's retry backoff. This is the window the Pagr server assumes receivers
    /// enforce.
    /// </remarks>
    public static readonly TimeSpan DefaultTolerance = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Verifies the <c>X-Pagr-Signature</c> header of an async-render callback, throwing if the
    /// callback cannot be proven to have come from Pagr.
    /// </summary>
    /// <remarks>
    /// Returns normally on success and throws on every failure, so a caller who ignores the
    /// (absent) return value still fails closed.
    /// </remarks>
    /// <param name="rawBody">
    /// The <b>raw</b> request body, exactly as received off the wire. A body that was parsed
    /// into an object and re-serialised will not reproduce the digest — see the
    /// <see cref="WebhookSignature"/> remarks.
    /// </param>
    /// <param name="signatureHeader">
    /// The <c>X-Pagr-Signature</c> header value, or <see langword="null"/> when the request
    /// carried none (which is itself a failure).
    /// </param>
    /// <param name="secret">The organisation's webhook signing secret (Settings → API keys).</param>
    /// <param name="tolerance">
    /// Maximum accepted difference between the signed timestamp and <paramref name="now"/>, in
    /// either direction. Defaults to <see cref="DefaultTolerance"/>.
    /// </param>
    /// <param name="now">
    /// The current time; defaults to <see cref="DateTimeOffset.UtcNow"/>. Present for testing
    /// and for callers with their own clock.
    /// </param>
    /// <exception cref="PagrSignatureException">
    /// The header is absent, malformed, carries a timestamp outside <paramref name="tolerance"/>,
    /// or no signature in it matches <paramref name="secret"/> — i.e. anything short of a
    /// proven-genuine callback.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="secret"/> is <see langword="null"/>, empty or whitespace. That is a
    /// misconfiguration of the receiver, not an untrustworthy callback, so it is deliberately
    /// not a <see cref="PagrSignatureException"/>.
    /// </exception>
    public static void Verify(
        ReadOnlySpan<byte> rawBody,
        string? signatureHeader,
        string secret,
        TimeSpan? tolerance = null,
        DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new ArgumentException(
                "A webhook signing secret is required to verify a callback; copy it from " +
                "Settings → API keys in the Pagr web app.",
                nameof(secret));
        }

        if (string.IsNullOrWhiteSpace(signatureHeader))
            throw new PagrSignatureException($"Request carried no {HeaderName} header.");

        if (!TryParseHeader(signatureHeader, out var signedAtText, out var candidates))
            throw new PagrSignatureException($"Unparsable {HeaderName} header: '{signatureHeader}'.");

        if (!long.TryParse(signedAtText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var signedAt))
        {
            throw new PagrSignatureException(
                $"{HeaderName} timestamp is not an integer: '{signedAtText}'.");
        }

        var window = tolerance ?? DefaultTolerance;
        var clock = now ?? DateTimeOffset.UtcNow;

        // Compared in seconds rather than as DateTimeOffset values: an absurd t (a forged
        // header, or a sender bug) must be rejected as a signature failure, not blow up
        // DateTimeOffset.FromUnixTimeSeconds with an out-of-range argument.
        var driftSeconds = Math.Abs((double)clock.ToUnixTimeSeconds() - signedAt);
        if (driftSeconds > window.TotalSeconds)
        {
            throw new PagrSignatureException(string.Format(
                CultureInfo.InvariantCulture,
                "Callback was signed {0:F0}s from now, outside the {1:F0}s tolerance — " +
                "stale delivery or a replay.",
                driftSeconds,
                window.TotalSeconds));
        }

        var expected = ComputeHex(secret, signedAt, rawBody);

        // Any match wins: during a rotation Pagr signs with both the new and the outgoing
        // secret, and only one of them is the one this receiver holds.
        foreach (var candidate in candidates)
        {
            if (FixedTimeEquals(expected, candidate))
                return;
        }

        throw new PagrSignatureException(
            $"None of the {candidates.Count} signature(s) in {HeaderName} matched the configured secret.");
    }

    /// <inheritdoc cref="Verify(ReadOnlySpan{byte}, string?, string, TimeSpan?, DateTimeOffset?)"/>
    /// <remarks>
    /// Convenience overload for frameworks that hand you the body as text. It must still be the
    /// <b>raw</b> body as received — the string is UTF-8 encoded back to the bytes the digest
    /// covers, which only reproduces them if nothing re-serialised the payload in between.
    /// </remarks>
    public static void Verify(
        string rawBody,
        string? signatureHeader,
        string secret,
        TimeSpan? tolerance = null,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(rawBody);
        Verify(Encoding.UTF8.GetBytes(rawBody), signatureHeader, secret, tolerance, now);
    }

    /// <summary>
    /// Verifies a callback's signature and parses its body, in one call. <b>The preferred entry
    /// point for a webhook endpoint.</b>
    /// </summary>
    /// <remarks>
    /// Verification happens <em>before</em> the body is decoded, so application code never sees
    /// a payload that was not proven to come from Pagr. Because this overload takes the raw
    /// bytes and does the JSON decoding itself, there is no way to call it in the wrong order or
    /// to forget the verification step.
    /// </remarks>
    /// <param name="rawBody">
    /// The <b>raw</b> request body, exactly as received off the wire — see the
    /// <see cref="WebhookSignature"/> remarks for why a re-serialised body never verifies.
    /// </param>
    /// <param name="signatureHeader">The <c>X-Pagr-Signature</c> header value.</param>
    /// <param name="secret">The organisation's webhook signing secret (Settings → API keys).</param>
    /// <param name="tolerance">Replay window; defaults to <see cref="DefaultTolerance"/>.</param>
    /// <param name="now">The current time; defaults to <see cref="DateTimeOffset.UtcNow"/>.</param>
    /// <returns>
    /// A <see cref="RenderProgress"/> for per-document callbacks, or a
    /// <see cref="RenderCompletion"/> for the final one.
    /// </returns>
    /// <exception cref="PagrSignatureException">
    /// The callback cannot be proven to come from Pagr. Thrown before the body is decoded.
    /// </exception>
    /// <exception cref="PagrDecodeException">
    /// The verified body is not valid JSON, or matches neither the progress nor the completion
    /// shape.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="secret"/> is null, empty or whitespace.</exception>
    public static RenderCallback ParseSignedCallback(
        ReadOnlySpan<byte> rawBody,
        string? signatureHeader,
        string secret,
        TimeSpan? tolerance = null,
        DateTimeOffset? now = null)
    {
        Verify(rawBody, signatureHeader, secret, tolerance, now);
        return RenderCallback.Parse(Encoding.UTF8.GetString(rawBody));
    }

    /// <inheritdoc cref="ParseSignedCallback(ReadOnlySpan{byte}, string?, string, TimeSpan?, DateTimeOffset?)"/>
    /// <remarks>
    /// Convenience overload for frameworks that hand you the body as text; it must still be the
    /// <b>raw</b> body as received.
    /// </remarks>
    public static RenderCallback ParseSignedCallback(
        string rawBody,
        string? signatureHeader,
        string secret,
        TimeSpan? tolerance = null,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(rawBody);
        Verify(rawBody, signatureHeader, secret, tolerance, now);
        return RenderCallback.Parse(rawBody);
    }

    /// <summary>
    /// Splits the header into its signed timestamp and every <c>v1</c> signature in it.
    /// </summary>
    /// <remarks>
    /// Elements whose scheme version is neither <c>t</c> nor <c>v1</c> are ignored, so a future
    /// <c>v2=</c> alongside <c>v1=</c> does not make an otherwise-verifiable header look
    /// malformed. Returns <see langword="false"/> when the timestamp or every signature is
    /// missing — without a <c>t</c> nothing bounds a replay, and without a <c>v1</c> there is
    /// nothing to compare against.
    /// </remarks>
    private static bool TryParseHeader(
        string signatureHeader,
        out string signedAtText,
        out List<string> candidates)
    {
        string? timestamp = null;
        candidates = [];

        foreach (var element in signatureHeader.Split(','))
        {
            var part = element.Trim();
            var separator = part.IndexOf('=');
            if (separator < 0)
                continue;

            var key = part[..separator];
            var value = part[(separator + 1)..];

            if (key == "t")
                timestamp = value;
            else if (key == "v1")
                candidates.Add(value);
        }

        signedAtText = timestamp ?? string.Empty;
        return timestamp is not null && candidates.Count > 0;
    }

    /// <summary>
    /// Lowercase hex <c>HMAC-SHA256(secret, "{signedAt}." + rawBody)</c>.
    /// </summary>
    /// <remarks>
    /// The body is hashed as the bytes that arrived; re-encoding or re-serialising the JSON
    /// first would not reproduce the digest.
    /// </remarks>
    private static string ComputeHex(string secret, long signedAt, ReadOnlySpan<byte> rawBody)
    {
        var prefix = Encoding.UTF8.GetBytes(
            signedAt.ToString(CultureInfo.InvariantCulture) + ".");

        var signed = new byte[prefix.Length + rawBody.Length];
        prefix.CopyTo(signed, 0);
        rawBody.CopyTo(signed.AsSpan(prefix.Length));

        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signed);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Constant-time comparison of two hex signatures — a length or byte-position timing leak
    /// here would let an attacker recover a valid signature one character at a time.
    /// </summary>
    private static bool FixedTimeEquals(string expected, string candidate) =>
        expected.Length == candidate.Length
        && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(candidate));
}
