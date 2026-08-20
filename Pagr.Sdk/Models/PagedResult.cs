using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pagr.Sdk.Models;

/// <summary>
/// A page of results returned by a list endpoint. Enumerable and indexable over
/// <see cref="Items"/>.
/// </summary>
/// <remarks>
/// <see cref="Total"/> is the total number of matching records across all pages, not just
/// this page; use <see cref="HasMore"/> to drive paging.
/// </remarks>
/// <typeparam name="T">The element type.</typeparam>
[JsonConverter(typeof(PagedResultConverterFactory))]
public sealed class PagedResult<T> : IReadOnlyList<T>
{
    /// <summary>The items on this page.</summary>
    public IReadOnlyList<T> Items { get; init; } = [];

    /// <summary>The total number of items across all pages.</summary>
    public int Total { get; init; }

    /// <summary>The paging offset this page was requested with.</summary>
    public int Skip { get; init; }

    /// <summary>The page size this page was requested with.</summary>
    public int Take { get; init; }

    /// <summary><see langword="true"/> when more pages follow this one.</summary>
    public bool HasMore => Skip + Items.Count < Total;

    /// <inheritdoc/>
    public int Count => Items.Count;

    /// <inheritdoc/>
    public T this[int index] => Items[index];

    /// <inheritdoc/>
    public IEnumerator<T> GetEnumerator() => Items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Reads/writes <see cref="PagedResult{T}"/> as the API's <c>{items,total,skip,take}</c>
/// object. Needed because the class implements <see cref="IEnumerable{T}"/>, which the
/// serialiser would otherwise treat as a JSON array.
/// </summary>
internal sealed class PagedResultConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(PagedResult<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var itemType = typeToConvert.GetGenericArguments()[0];
        return (JsonConverter)Activator.CreateInstance(
            typeof(PagedResultConverter<>).MakeGenericType(itemType))!;
    }
}

internal sealed class PagedResultConverter<T> : JsonConverter<PagedResult<T>>
{
    private sealed class Dto
    {
        [JsonPropertyName("items")]
        public List<T>? Items { get; set; }

        [JsonPropertyName("total")]
        public int Total { get; set; }

        [JsonPropertyName("skip")]
        public int Skip { get; set; }

        [JsonPropertyName("take")]
        public int Take { get; set; }
    }

    public override PagedResult<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dto = JsonSerializer.Deserialize<Dto>(ref reader, options)
            ?? throw new JsonException("Expected a paged-result object.");
        return new PagedResult<T>
        {
            Items = dto.Items ?? [],
            Total = dto.Total,
            Skip = dto.Skip,
            Take = dto.Take,
        };
    }

    public override void Write(Utf8JsonWriter writer, PagedResult<T> value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, new Dto
        {
            Items = value.Items.ToList(),
            Total = value.Total,
            Skip = value.Skip,
            Take = value.Take,
        }, options);
}
