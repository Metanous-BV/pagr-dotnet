using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pagr.Sdk.Exceptions;

namespace Pagr.Sdk;

/// <summary>Shared JSON serialiser configuration for API responses.</summary>
internal static class PagrJson
{
    /// <summary>
    /// Web defaults (camelCase, case-insensitive) plus lenient timestamp parsing: the API
    /// emits offset-less timestamps (e.g. <c>2026-07-16T10:00:00</c>) that are UTC by
    /// contract, which the default <see cref="DateTimeOffset"/> handling would interpret
    /// in the machine's local time zone.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new UtcAssumingDateTimeOffsetConverter() },
    };

    /// <summary>
    /// Deserialises an already-decoded JSON element, translating any decode failure
    /// (malformed shape, a missing <see langword="required"/> member) into a
    /// <see cref="PagrDecodeException"/> rather than letting a raw <see cref="JsonException"/>
    /// escape.
    /// </summary>
    public static T Deserialize<T>(JsonElement element)
    {
        try
        {
            return element.Deserialize<T>(Options)
                ?? throw new PagrDecodeException($"The Pagr API response has no data for {typeof(T).Name}.");
        }
        catch (JsonException ex)
        {
            throw new PagrDecodeException(
                $"The Pagr API returned a response that could not be decoded as {typeof(T).Name}.",
                innerException: ex);
        }
    }

    /// <summary>An empty JSON object, used as the lenient fallback for free-form JSON fields.</summary>
    public static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();

    /// <summary>
    /// Reads an HTTP response body as a raw <see cref="JsonElement"/>, ignoring the declared
    /// media type (the raw-PDF render path receives its 422 error envelope on a request that
    /// asked for <c>application/pdf</c>). Decode failures become
    /// <see cref="PagrDecodeException"/>.
    /// </summary>
    public static async Task<JsonElement> ReadElementAsync(
        HttpContent content, int statusCode, CancellationToken cancellationToken)
    {
        var body = await content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new PagrDecodeException(
                "The Pagr API returned a response that could not be decoded as JSON.", statusCode, ex);
        }
    }

    /// <summary>
    /// Reads and deserialises an HTTP response body, translating any decode failure (a
    /// non-JSON/empty body, a missing <see langword="required"/> member) into a
    /// <see cref="PagrDecodeException"/> carrying the response's status code.
    /// </summary>
    public static async Task<T> ReadAsync<T>(
        HttpContent content, int statusCode, CancellationToken cancellationToken)
    {
        try
        {
            return await content.ReadFromJsonAsync<T>(Options, cancellationToken).ConfigureAwait(false)
                ?? throw new PagrDecodeException(
                    $"The Pagr API response has no data for {typeof(T).Name}.", statusCode);
        }
        catch (JsonException ex)
        {
            throw new PagrDecodeException(
                $"The Pagr API returned a response that could not be decoded as {typeof(T).Name}.",
                statusCode, ex);
        }
    }
}

/// <summary>
/// Parses ISO-8601 timestamps, treating values without an explicit offset as UTC —
/// per the API contract — rather than as local time.
/// </summary>
internal sealed class UtcAssumingDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // An explicit offset (or Z) in the text always wins; AssumeUniversal only kicks in
        // when the timestamp carries none.
        var text = reader.GetString();
        return DateTimeOffset.Parse(
            text!, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}

/// <summary>
/// Reads a field the API transports as a JSON <em>string containing JSON</em> (e.g.
/// <c>"sampleData": "{\"a\":1}"</c>) into a usable <see cref="JsonElement"/>.
/// </summary>
/// <remarks>
/// Deliberately lenient, matching the other SDKs: an empty, malformed or non-object value all
/// decode to an empty JSON object rather than throwing. This field is free-form caller data
/// the API round-trips verbatim, so a template author's malformed sample data must not make an
/// otherwise fine <c>GetTemplateVersionAsync</c> call fail.
/// </remarks>
internal sealed class EmbeddedJsonConverter : JsonConverter<JsonElement>
{
    public override JsonElement Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                var raw = reader.GetString();
                if (string.IsNullOrWhiteSpace(raw))
                    return PagrJson.EmptyObject;
                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    return doc.RootElement.ValueKind == JsonValueKind.Object
                        ? doc.RootElement.Clone()
                        : PagrJson.EmptyObject;
                }
                catch (JsonException)
                {
                    return PagrJson.EmptyObject;
                }

            // Tolerate an API that ever starts sending the field as a real JSON object.
            case JsonTokenType.StartObject:
                using (var doc = JsonDocument.ParseValue(ref reader))
                {
                    return doc.RootElement.Clone();
                }

            default:
                reader.Skip();
                return PagrJson.EmptyObject;
        }
    }

    public override void Write(Utf8JsonWriter writer, JsonElement value, JsonSerializerOptions options)
        => value.WriteTo(writer);
}
