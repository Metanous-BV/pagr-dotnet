using System.Text.Json.Serialization;
using Pagr.Sdk.Exceptions;

namespace Pagr.Sdk.Models;

/// <summary>
/// Metadata for a persisted rendered document, as returned by the document-browsing
/// endpoints (<c>GetDocumentsAsync</c> / <c>GetDocumentAsync</c>).
/// </summary>
/// <remarks>
/// <see cref="DocumentBase64"/> contains the PDF bytes only when the document was rendered
/// with <c>includeDocument: true</c>; otherwise call <c>DownloadDocumentAsync</c> to fetch
/// them separately.
/// </remarks>
public sealed class RenderDocument
{
    /// <summary>The unique identifier of the rendered document.</summary>
    [JsonPropertyName("id")]
    public required Guid Id { get; init; }

    /// <summary>The document's name (carries no file extension; rendered output is always a PDF).</summary>
    [JsonPropertyName("documentName")]
    public required string DocumentName { get; init; }

    /// <summary>The template the document was rendered from.</summary>
    [JsonPropertyName("templateId")]
    public required Guid TemplateId { get; init; }

    /// <summary>The template version the document was rendered from.</summary>
    [JsonPropertyName("versionNumber")]
    public required int VersionNumber { get; init; }

    /// <summary>The environment the document was rendered in: <c>"test"</c> or <c>"production"</c>.</summary>
    [JsonPropertyName("environment")]
    public required string Environment { get; init; }

    /// <summary>The size of the rendered file in bytes.</summary>
    [JsonPropertyName("fileSizeBytes")]
    public required long FileSizeBytes { get; init; }

    /// <summary>The number of pages in the rendered document.</summary>
    [JsonPropertyName("pageCount")]
    public required int PageCount { get; init; }

    /// <summary>When the document was rendered.</summary>
    [JsonPropertyName("renderedAt")]
    public required DateTimeOffset RenderedAt { get; init; }

    /// <summary>How long the render took, in milliseconds.</summary>
    [JsonPropertyName("renderDuration")]
    public required double RenderDuration { get; init; }

    /// <summary>A URL at which the rendered document can be viewed.</summary>
    [JsonPropertyName("viewUrl")]
    public required string ViewUrl { get; init; }

    /// <summary>The document type — <c>"Template"</c> or <c>"Invoice"</c>.</summary>
    [JsonPropertyName("documentType")]
    public required string DocumentType { get; init; }

    /// <summary><see langword="true"/> when the stored PDF has since been deleted server-side.</summary>
    [JsonPropertyName("isPdfDeleted")]
    public bool IsPdfDeleted { get; init; }

    /// <summary>The language variant the document was rendered in, if any.</summary>
    [JsonPropertyName("language")]
    public string? Language { get; init; }

    /// <summary>
    /// The inline document content as a Base64 string, or <see langword="null"/> when the
    /// document was rendered without <c>includeDocument: true</c>.
    /// </summary>
    [JsonPropertyName("documentBase64")]
    public string? DocumentBase64 { get; init; }

    /// <summary><see langword="true"/> if inline document bytes are available.</summary>
    [JsonIgnore]
    public bool HasContent => DocumentBase64 is not null;

    /// <summary>Returns the decoded document bytes.</summary>
    /// <remarks>Only available when the document was rendered with <c>includeDocument: true</c>.</remarks>
    /// <returns>The decoded document content.</returns>
    /// <exception cref="PagrApiException">Thrown when the document has no inline Base64 content.</exception>
    public byte[] ToBytes() => DocumentContent.ToBytes(DocumentBase64);

    /// <summary>Writes the document to disk.</summary>
    /// <param name="path">
    /// Destination path. If it is an existing directory, <see cref="DocumentName"/> — reduced
    /// to a safe, single-segment filename with <c>.pdf</c> appended unless it already ends in
    /// it — is used as the filename inside it. Anything else is written verbatim.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <returns>The path that was written.</returns>
    /// <exception cref="PagrApiException">Thrown when the document has no inline Base64 content.</exception>
    public Task<string> SaveAsync(string path, CancellationToken cancellationToken = default)
        => DocumentContent.SaveAsync(path, DocumentName, Id, ToBytes(), cancellationToken);

    /// <inheritdoc/>
    public override string ToString() => $"{DocumentName} ({Id})";
}
