namespace Pagr.Sdk.Exceptions;

/// <summary>404 — template or resource not found.</summary>
public sealed class PagrNotFoundException : PagrApiException
{
    /// <summary>Initialises a new <see cref="PagrNotFoundException"/>.</summary>
    public PagrNotFoundException(string message, int? statusCode = 404, string? code = null)
        : base(message, statusCode, code) { }
}