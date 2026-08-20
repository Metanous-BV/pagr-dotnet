namespace Pagr.Sdk.Exceptions;

/// <summary>401 — invalid or missing API key.</summary>
public sealed class PagrAuthenticationException : PagrApiException
{
    /// <summary>Initialises a new <see cref="PagrAuthenticationException"/>.</summary>
    public PagrAuthenticationException(string message, int? statusCode = 401, string? code = null)
        : base(message, statusCode, code) { }
}
