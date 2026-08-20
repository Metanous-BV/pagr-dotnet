namespace Pagr.Sdk.Exceptions;

/// <summary>
/// Base type for every error thrown by the Pagr SDK.
/// </summary>
/// <remarks>
/// All API errors derive from this class, so a single <c>catch (PagrApiException)</c>
/// handles every failure mode. Where the failure originated from an HTTP response,
/// <see cref="StatusCode"/> and <see cref="Code"/> carry the HTTP status and the API
/// error code (read from the <c>{"error":{"code","message"}}</c> envelope) respectively.
/// <para>
/// The type is <see langword="abstract"/> on purpose: it is the catch-all supertype and is
/// never itself thrown, so <c>catch (PagrApiException)</c> can never be the only way to
/// tell two different failures apart. Failures with no dedicated subclass surface as
/// <see cref="PagrGenericApiException"/>.
/// </para>
/// </remarks>
public abstract class PagrApiException : Exception
{
    /// <summary>HTTP status code that produced this error, or <see langword="null"/> if not HTTP-related.</summary>
    public int? StatusCode { get; }

    /// <summary>API error code from the response envelope, or <see langword="null"/> if absent.</summary>
    public string? Code { get; }

    /// <summary>Initialises a new <see cref="PagrApiException"/>.</summary>
    /// <param name="message">The human-readable error message.</param>
    /// <param name="statusCode">The originating HTTP status code, if any.</param>
    /// <param name="code">The API error code from the response envelope, if any.</param>
    /// <param name="innerException">The underlying exception, if any.</param>
    protected PagrApiException(
        string message,
        int? statusCode = null,
        string? code = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Code = code;
    }
}
