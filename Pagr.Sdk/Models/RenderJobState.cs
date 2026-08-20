using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pagr.Sdk.Models;

/// <summary>
/// Lifecycle state of an async render job.
/// </summary>
/// <remarks>
/// <see cref="Queued"/> (just enqueued) and <see cref="Pending"/> (queued or rendering) are
/// non-terminal; <see cref="Completed"/> (documents produced, including partial/credit-stopped
/// runs) and <see cref="Failed"/> (nothing produced) are terminal.
///
/// <see cref="Unknown"/> is a client-side fail-open fallback, not a server value: an
/// unrecognised state parses to it rather than throwing, and is treated as
/// <see cref="RenderJobStateExtensions.IsTerminal"/> so a new server state can never trap a
/// polling loop in an infinite wait.
/// </remarks>
[JsonConverter(typeof(RenderJobStateConverter))]
public enum RenderJobState
{
    /// <summary>The job was just enqueued.</summary>
    Queued,

    /// <summary>The job is queued or rendering.</summary>
    Pending,

    /// <summary>The job finished; documents were produced, including partial/credit-stopped runs.</summary>
    Completed,

    /// <summary>The job finished without producing any document.</summary>
    Failed,

    /// <summary>The state was missing or not recognised by this SDK version.</summary>
    Unknown,
}

/// <summary>Extension methods for <see cref="RenderJobState"/>.</summary>
public static class RenderJobStateExtensions
{
    /// <summary>
    /// <see langword="true"/> once the job has stopped advancing. <see cref="RenderJobState.Unknown"/>
    /// counts as terminal (fail-open) so an unrecognised server state ends a polling loop
    /// rather than spinning forever.
    /// </summary>
    public static bool IsTerminal(this RenderJobState state) =>
        state is not (RenderJobState.Queued or RenderJobState.Pending);
}

/// <summary>
/// Parses the API's string value case-insensitively; unknown or <see langword="null"/>
/// values fail open to <see cref="RenderJobState.Unknown"/>.
/// </summary>
internal sealed class RenderJobStateConverter : JsonConverter<RenderJobState>
{
    public override bool HandleNull => true;

    public override RenderJobState Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String &&
            Enum.TryParse<RenderJobState>(reader.GetString(), ignoreCase: true, out var value) &&
            Enum.IsDefined(value))
        {
            return value;
        }
        return RenderJobState.Unknown;
    }

    public override void Write(Utf8JsonWriter writer, RenderJobState value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
