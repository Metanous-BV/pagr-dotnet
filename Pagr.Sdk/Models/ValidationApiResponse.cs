using System.Text.Json.Serialization;

namespace Pagr.Sdk.Models;

/// <summary>
/// Raw shape of the validate endpoint's JSON response (<c>ValidateResultDto</c>): a single
/// flat list of issues. Deserialised internally and then projected into
/// <see cref="ValidationResponse"/>.
/// </summary>
internal sealed class ValidationApiResponse
{
    [JsonPropertyName("issues")]
    public List<RenderIssue> Issues { get; set; } = [];
}
