using System.Text.Json;
using System.Text.Json.Serialization;

namespace CVPN.Core;

/// <summary>
/// Шифрует значение при записи в JSON и расшифровывает при чтении.
///
/// Сделано конвертером, а не отдельными свойствами, чтобы модель в памяти
/// оставалась обычной: код работает с открытым паролем и не знает о шифровании,
/// а защита включается ровно на границе файла.
/// </summary>
public sealed class ProtectedStringConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
        Secret.Unprotect(reader.GetString());
 
    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(Secret.Protect(value));
}
