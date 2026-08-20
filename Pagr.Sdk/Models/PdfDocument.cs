using System.Globalization;

namespace Pagr.Sdk.Models;

/// <summary>
/// A single document returned as a raw PDF stream.
/// </summary>
/// <remarks>
/// Produced only by <c>PagrApiClient.RenderPdfAsync</c>, which opts into the
/// <c>Accept: application/pdf</c> response. Unlike <see cref="RenderedDocument"/> (built from
/// the JSON envelope) this carries only what the raw-PDF response actually provides — the
/// bytes plus the metadata the server puts in <c>X-Pagr-*</c> headers. Fields the headers do
/// not carry (template id, version, environment, timestamp, type, language) are deliberately
/// absent rather than fabricated.
/// <para>
/// <see cref="DocumentId"/> and <see cref="ViewUrl"/> are <see langword="null"/> when the
/// render was not persisted (<c>persist: false</c>).
/// </para>
/// </remarks>
public sealed class PdfDocument
{
    /// <summary>
    /// The document's name, read from <c>Content-Disposition</c> with any <c>.pdf</c>
    /// extension stripped, or <c>"document"</c> when the response carries no filename.
    /// </summary>
    public required string DocumentName { get; init; }

    /// <summary>The raw PDF bytes.</summary>
    public required byte[] Content { get; init; }

    /// <summary>
    /// The stored document's identifier, or <see langword="null"/> when the render was not
    /// persisted.
    /// </summary>
    public Guid? DocumentId { get; init; }

    /// <summary>The number of pages in the rendered document.</summary>
    public int PageCount { get; init; }

    /// <summary>How long the render took, in milliseconds.</summary>
    public double RenderDuration { get; init; }

    /// <summary>
    /// A URL at which the rendered document can be viewed, or <see langword="null"/> when the
    /// render was not persisted.
    /// </summary>
    public string? ViewUrl { get; init; }

    /// <summary>How many non-blocking issues the render reported.</summary>
    public int IssueCount { get; init; }

    /// <summary>Returns the PDF bytes.</summary>
    /// <returns>The raw PDF content.</returns>
    public byte[] ToBytes() => Content;

    /// <summary>Writes the PDF to disk.</summary>
    /// <param name="path">
    /// Destination path. If it is an existing directory, <see cref="DocumentName"/> — reduced
    /// to a safe, single-segment filename with <c>.pdf</c> appended — is used as the filename
    /// inside it. Anything else is written verbatim.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <returns>The path that was written.</returns>
    public Task<string> SaveAsync(string path, CancellationToken cancellationToken = default)
        => DocumentContent.SaveAsync(path, DocumentName, DocumentId ?? Guid.Empty, Content, cancellationToken);

    /// <summary>
    /// Builds a document from a raw <c>application/pdf</c> response: the metadata comes from
    /// the <c>X-Pagr-*</c> headers and the name from <c>Content-Disposition</c>.
    /// </summary>
    /// <param name="response">The response, whose headers must still be readable.</param>
    /// <param name="content">The already-read response body.</param>
    internal static PdfDocument FromResponse(HttpResponseMessage response, byte[] content) => new()
    {
        DocumentName = FilenameFromContentDisposition(Header(response, "Content-Disposition")),
        Content = content,
        // An absent or unparseable id means "not persisted" — never a zero-valued Guid.
        DocumentId = Guid.TryParse(Header(response, "X-Pagr-Document-Id"), out var id) ? id : null,
        PageCount = ParseInt(Header(response, "X-Pagr-Page-Count")),
        RenderDuration = ParseDouble(Header(response, "X-Pagr-Render-Duration-Ms")),
        ViewUrl = Header(response, "X-Pagr-View-Url"),
        IssueCount = ParseInt(Header(response, "X-Pagr-Issue-Count")),
    };

    /// <summary>
    /// Reads a single header value. <c>Content-Disposition</c> is a content header, so both
    /// collections are consulted rather than assuming which one the server's value landed in.
    /// </summary>
    private static string? Header(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out var values))
            return values.FirstOrDefault();
        if (response.Content.Headers.TryGetValues(name, out var contentValues))
            return contentValues.FirstOrDefault();
        return null;
    }

    private static int ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private static double ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    /// <summary>
    /// Extracts a bare document name from a <c>Content-Disposition</c> header.
    /// </summary>
    /// <remarks>
    /// The API sends <c>attachment; filename="&lt;documentName&gt;.pdf"</c>. Returns the name
    /// without its <c>.pdf</c> extension (matching
    /// <see cref="RenderedDocument.DocumentName"/>'s no-extension convention), or
    /// <c>"document"</c> when the header is missing or carries no filename. This is *not*
    /// filename sanitisation — that happens separately, in
    /// <see cref="SaveAsync"/>, only when the name is actually used as a path.
    /// </remarks>
    internal static string FilenameFromContentDisposition(string? header)
    {
        if (!string.IsNullOrEmpty(header))
        {
            const string marker = "filename=";
            var at = header.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (at >= 0)
            {
                var name = header[(at + marker.Length)..].Split(';', 2)[0].Trim().Trim('"');
                if (name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    name = name[..^4];
                if (name.Length > 0)
                    return name;
            }
        }
        return "document";
    }

    /// <inheritdoc/>
    public override string ToString() => $"{DocumentName} ({Content.Length} bytes, {PageCount} page(s))";
}
