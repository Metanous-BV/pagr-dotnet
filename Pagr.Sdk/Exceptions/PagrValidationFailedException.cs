namespace Pagr.Sdk.Exceptions;

/// <summary>422 — the request body could not be bound/validated.</summary>
public sealed class PagrValidationFailedException : PagrApiException
{
    /// <summary>Initialises a new <see cref="PagrValidationFailedException"/>.</summary>
    public PagrValidationFailedException(string message, int? statusCode = 422, string? code = null)
        : base(message, statusCode, code) { }
}