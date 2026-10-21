using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gannet.Core.Domain;

/// <summary>
/// A source's ID within one run: S1, S2, ... Tools assign it the moment a result is fetched; the model
/// sees and cites it; code turns it into a reference number. Attribution never passes through the model.
/// </summary>
[JsonConverter(typeof(SourceIdJsonConverter))]
public readonly record struct SourceId(int Number)
{
    private const int MaxDigits = 6;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"S{Number}");

    /// <summary>Accepts exactly an upper-case S followed by 1 to 6 digits with no leading zero.</summary>
    public static bool TryParse([NotNullWhen(true)] string? text, out SourceId id)
    {
        id = default;
        if (text is null || text.Length < 2 || text.Length > 1 + MaxDigits || text[0] != 'S' || text[1] == '0')
        {
            return false;
        }

        for (var i = 1; i < text.Length; i++)
        {
            if (!char.IsAsciiDigit(text[i]))
            {
                return false;
            }
        }

        id = new SourceId(int.Parse(text.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture));
        return true;
    }
}

internal sealed class SourceIdJsonConverter : JsonConverter<SourceId>
{
    public override SourceId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        SourceId.TryParse(reader.GetString(), out var id) ? id : throw new JsonException("Expected a source ID like \"S3\".");

    public override void Write(Utf8JsonWriter writer, SourceId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
