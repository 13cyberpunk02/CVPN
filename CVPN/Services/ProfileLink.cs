using System.Text;
using CVPN.Models;
using CVPN.Models.Enums;

namespace CVPN.Services;

/// <summary>
/// Обратная операция к LinkParser: собирает ссылку из профиля.
/// Формат тот же, что понимают другие клиенты, - ссылку можно передать как есть.
/// </summary>
public static class ProfileLink
{
    public static string Build(ServerProfile p) => p.Protocol switch
    {
        ProtocolKind.VlessReality => Vless(p, reality: true),
        ProtocolKind.VlessWs => Vless(p, reality: false),
        ProtocolKind.AnyTls => AnyTls(p),
        ProtocolKind.Naive => Naive(p),
        _ => ""
    };

    private static string Vless(ServerProfile p, bool reality)
    {
        var query = new List<string>();

        if (reality)
        {
            query.Add("type=tcp");
            query.Add("security=reality");
            Add(query, "pbk", p.PublicKey);
            Add(query, "sid", p.ShortId);
            Add(query, "flow", p.Flow);
            query.Add("fp=chrome");
        }
        else
        {
            query.Add("type=ws");
            query.Add("security=tls");
            Add(query, "path", p.Path);
            Add(query, "host", p.Sni);
        }

        Add(query, "sni", p.Sni);

        return $"vless://{p.Uuid}@{p.Host}:{p.Port}?{string.Join('&', query)}{Fragment(p.Name)}";
    }

    private static string AnyTls(ServerProfile p)
    {
        var query = new List<string>();
        Add(query, "sni", p.Sni);

        var suffix = query.Count > 0 ? $"?{string.Join('&', query)}" : "";

        return $"anytls://{Uri.EscapeDataString(p.Password)}@{p.Host}:{p.Port}{suffix}{Fragment(p.Name)}";
    }

    private static string Naive(ServerProfile p) =>
        $"naive+https://{Uri.EscapeDataString(p.Username)}:{Uri.EscapeDataString(p.Password)}" +
        $"@{p.Host}:{p.Port}{Fragment(p.Name)}";

    private static void Add(List<string> query, string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) query.Add($"{key}={Uri.EscapeDataString(value)}");
    }

    private static string Fragment(string name) =>
        string.IsNullOrWhiteSpace(name) ? "" : $"#{Uri.EscapeDataString(name)}";

    /// <summary>
    /// Экспорт всего списка в формате подписки: ссылки построчно в base64.
    /// Именно так их ждут другие клиенты.
    /// </summary>
    public static string BuildSubscription(IEnumerable<ServerProfile> profiles)
    {
        var links = profiles
            .Select(Build)
            .Where(link => link.Length > 0);

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join('\n', links)));
    }
}