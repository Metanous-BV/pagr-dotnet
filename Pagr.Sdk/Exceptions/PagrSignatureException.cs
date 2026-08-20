namespace Pagr.Sdk.Exceptions;

/// <summary>
/// An async-render webhook callback could not be proven to have come from Pagr.
/// </summary>
/// <remarks>
/// Thrown by <see cref="Webhooks.WebhookSignature.Verify(System.ReadOnlySpan{byte}, string?, string, System.TimeSpan?, System.DateTimeOffset?)"/>
/// and <see cref="Webhooks.WebhookSignature.ParseSignedCallback(System.ReadOnlySpan{byte}, string?, string, System.TimeSpan?, System.DateTimeOffset?)"/>
/// when the request carried no <c>X-Pagr-Signature</c> header, the header was malformed, its
/// timestamp fell outside the accepted tolerance (a stale delivery or a replay), or no signature
/// in it matched the configured secret. Verification throws rather than returning a
/// <see langword="bool"/> so a caller who forgets to check a result still fails closed.
/// <para>
/// A <em>missing or empty secret</em> is deliberately <b>not</b> this exception but an
/// <see cref="ArgumentException"/>: that is a misconfiguration of the receiver (an unset
/// environment variable, typically), which must stay distinguishable from an untrustworthy
/// callback. <see cref="PagrApiException.StatusCode"/> and <see cref="PagrApiException.Code"/>
/// are always <see langword="null"/> — this failure is receiver-side, not an API response.
/// </para>
/// </remarks>
public sealed class PagrSignatureException : PagrApiException
{
    /// <summary>Initialises a new <see cref="PagrSignatureException"/>.</summary>
    /// <param name="message">The human-readable error message.</param>
    /// <param name="innerException">The underlying exception, if any.</param>
    public PagrSignatureException(string message, Exception? innerException = null)
        : base(message, statusCode: null, code: null, innerException) { }
}
