namespace Pagr.Sdk.Exceptions;

/// <summary>403 — authenticated but not allowed to access this resource.</summary>
public sealed class PagrForbiddenException : PagrApiException
{
    /// <summary>Initialises a new <see cref="PagrForbiddenException"/>.</summary>
    public PagrForbiddenException(string message, int? statusCode = 403, string? code = null)
        : base(message, statusCode, code) { }
}
