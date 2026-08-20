namespace Pagr.Sdk.Exceptions;

/// <summary>413 — a submitted document exceeds the maximum payload size.</summary>
public sealed class PagrPayloadTooLargeException : PagrApiException
{
    /// <summary>Initialises a new <see cref="PagrPayloadTooLargeException"/>.</summary>
    public PagrPayloadTooLargeException(string message, int? statusCode = 413, string? code = null)
        : base(message, statusCode, code) { }
}