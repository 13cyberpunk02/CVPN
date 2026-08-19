using System.IO;
using System.Text.Json;
using CVPN.Models;
using CVPN.Models.Enums;


namespace CVPN.Services;

/// <summary>
/// Импорт профилей из JSON. Принимает три формы:
///  1.полный конфиг sing-box - берутся все прокси-outbound'ы;
///  2.массив outbound-объектов;
///  3.один outbound-объект.
/// </summary>
public static class ConfigImporter
{
    public static bool TryImportFile(string path, out List<ServerProfile> profiles, out string error)
    {
        profiles = [];
        error = "";
 
        try
        {
            return TryImport(File.ReadAllText(path), out profiles, out error);
        }
        catch (Exception ex)
        {
            error = $"Не удалось прочитать файл: {ex.Message}";
            return false;
        }
    }
 
    public static bool TryImport(string json, out List<ServerProfile> profiles, out string error)
    {
        profiles = [];
        error = "";
 
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
        }
        catch (JsonException ex)
        {
            error = $"Это не похоже на JSON: {ex.Message}";
            return false;
        }
 
        using (doc)
        {
            var root = doc.RootElement;
 
            IEnumerable<JsonElement> candidates = root.ValueKind switch
            {
                JsonValueKind.Array => root.EnumerateArray(),
                JsonValueKind.Object when root.TryGetProperty("outbounds", out var outbounds)
                                          && outbounds.ValueKind == JsonValueKind.Array
                    => outbounds.EnumerateArray(),
                JsonValueKind.Object => [root],
                _ => []
            };
 
            foreach (var element in candidates)
            {
                var profile = FromOutbound(element);
                if (profile is not null) profiles.Add(profile);
            }
        }
 
        if (profiles.Count == 0)
        {
            error = "В файле не нашлось outbound'ов vless, anytls или naive";
            return false;
        }
 
        return true;
    }
 
    private static ServerProfile? FromOutbound(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        if (!e.TryGetProperty("type", out var typeProp)) return null;
 
        var type = typeProp.GetString();
        var tls = e.TryGetProperty("tls", out var tlsEl) ? tlsEl : default;
 
        var profile = new ServerProfile
        {
            Name = Str(e, "tag") is { Length: > 0 } tag ? tag : Str(e, "server") ?? "профиль",
            Host = Str(e, "server") ?? "",
            Port = Int(e, "server_port") ?? 443,
            Sni = Str(tls, "server_name") ?? Str(e, "server") ?? ""
        };
 
        if (profile.Host.Length == 0) return null;
 
        switch (type)
        {
            case "vless":
                profile.Uuid = Str(e, "uuid") ?? "";
                profile.Flow = Str(e, "flow") ?? "";
 
                var hasReality = tls.ValueKind == JsonValueKind.Object
                                 && tls.TryGetProperty("reality", out var reality)
                                 && reality.ValueKind == JsonValueKind.Object;
 
                if (hasReality)
                {
                    var r = tls.GetProperty("reality");
                    profile.Protocol = ProtocolKind.VlessReality;
                    profile.PublicKey = Str(r, "public_key") ?? "";
                    profile.ShortId = Str(r, "short_id") ?? "";
                }
                else
                {
                    profile.Protocol = ProtocolKind.VlessWs;
                    if (e.TryGetProperty("transport", out var tr) && tr.ValueKind == JsonValueKind.Object)
                        profile.Path = Str(tr, "path") ?? "/";
                }
                break;
 
            case "anytls":
                profile.Protocol = ProtocolKind.AnyTls;
                profile.Password = Str(e, "password") ?? "";
                break;
 
            case "naive":
                profile.Protocol = ProtocolKind.Naive;
                profile.Username = Str(e, "username") ?? "";
                profile.Password = Str(e, "password") ?? "";
                break;
 
            default:
                // direct, block, selector, urltest и прочее — не профили
                return null;
        }
 
        return profile;
    }
 
    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
 
    private static int? Int(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)
            ? i
            : null;
}
