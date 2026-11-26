using System.Text.Json;
using System.Text.Json.Serialization;

namespace Widgets.Core.Settings;

/// <summary>
/// Reads an enum by name, and gives <c>fallback</c> for anything it does not know (a removed value, a misspelling, a number
/// out of range, null, or any other kind of value) instead of failing. A settings file that names a layout this version no
/// longer has then keeps every other setting. It writes the name, like <see cref="JsonStringEnumConverter"/>.
/// </summary>
public abstract class LenientEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    private readonly T _fallback;

    protected LenientEnumConverter(T fallback) => _fallback = fallback;

    // A JSON null reaches Read too, so that it becomes the fallback instead of an error.
    public override bool HandleNull => true;

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String when Enum.TryParse<T>(reader.GetString(), ignoreCase: true, out var named) && Enum.IsDefined(named):
                return named;
            case JsonTokenType.Number when reader.TryGetInt32(out var number) && Enum.IsDefined(typeof(T), number):
                return (T)Enum.ToObject(typeof(T), number);
            case JsonTokenType.StartObject or JsonTokenType.StartArray:
                reader.Skip(); // the reader must end on the last token of the value it was given
                return _fallback;
            default:
                return _fallback;
        }
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

/// <summary>Reads <see cref="PreviewLayout"/>, falling back to <see cref="PreviewLayout.VerticalMeters"/>.</summary>
public sealed class PreviewLayoutConverter : LenientEnumConverter<PreviewLayout>
{
    public PreviewLayoutConverter() : base(PreviewLayout.VerticalMeters) { }
}
