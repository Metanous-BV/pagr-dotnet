using Pagr.Sdk.Exceptions;

namespace Pagr.Sdk.Models;

/// <summary>
/// Shared decode/save behaviour for documents that carry PDF bytes
/// (<see cref="RenderedDocument"/>, <see cref="RenderDocument"/> and <see cref="PdfDocument"/>).
/// </summary>
internal static class DocumentContent
{
    public static byte[] ToBytes(string? documentBase64)
    {
        if (documentBase64 is null)
            throw new PagrGenericApiException(
                "This document has no inline content. Render with includeDocument: true " +
                "to receive the document bytes.");
        return Convert.FromBase64String(documentBase64);
    }

    /// <summary>
    /// Reduces a server-supplied document name to a bare, safe filename.
    /// </summary>
    /// <remarks>
    /// A document name is data — it can embed values bound from the render payload — never a
    /// trusted path segment, so it must not be able to steer a save outside the target
    /// directory. Directory components (both separators), a Windows drive prefix and leading
    /// separators are stripped; an empty, <c>"."</c> or <c>".."</c> result falls back to the
    /// literal <c>"document"</c>. Without this, <see cref="Path.Combine(string, string)"/>
    /// silently discards the caller's directory for a rooted-looking name.
    /// </remarks>
    public static string SafeFilename(string? name)
    {
        // Drop directory components: normalise `\` to `/`, keep only the last segment.
        var result = (name ?? string.Empty).Replace('\\', '/');
        var lastSlash = result.LastIndexOf('/');
        if (lastSlash >= 0)
            result = result[(lastSlash + 1)..];

        // Drop a Windows drive prefix (e.g. a drive-relative `C:name`).
        if (result.Length >= 2 && char.IsAsciiLetter(result[0]) && result[1] == ':')
            result = result[2..];

        result = result.TrimStart('/', '\\').Trim();
        return result is "" or "." or ".." ? "document" : result;
    }

    /// <summary>
    /// Appends <c>.pdf</c> unless <paramref name="filename"/> already ends in it,
    /// case-insensitively.
    /// </summary>
    /// <remarks>
    /// A suffix test, never a "has no extension" test: a document name is generated from the
    /// version's name template and routinely contains a literal <c>.</c> that is not an
    /// extension, so <c>"Invoice 2024.10"</c> must become <c>"Invoice 2024.10.pdf"</c>.
    /// </remarks>
    public static string WithPdfSuffix(string filename) =>
        filename.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? filename : filename + ".pdf";

    public static async Task<string> SaveAsync(
        string path, string documentName, Guid id, byte[] bytes, CancellationToken cancellationToken)
    {
        var target = path;
        if (Directory.Exists(path))
        {
            var name = string.IsNullOrWhiteSpace(documentName)
                ? (id != Guid.Empty ? id.ToString() : "document")
                : documentName;
            target = Path.Combine(path, WithPdfSuffix(SafeFilename(name)));
        }

        await File.WriteAllBytesAsync(target, bytes, cancellationToken).ConfigureAwait(false);
        return target;
    }
}
