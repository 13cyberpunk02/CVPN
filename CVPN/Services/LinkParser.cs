using System.Web;
using CVPN.Localization;
using CVPN.Models;
using CVPN.Models.Enums;

namespace CVPN.Services;

/// <summary>Разбор ссылок подписки в профиль.</summary>
public static class LinkParser
{
    public static bool TryParse(string link, out ServerProfile profile, out string error)
    {
        profile = new ServerProfile();
        error = "";

        link = link.Trim();
        if (link.Length == 0)
        {
            error = Loc.T("Link_Empty");
            return false;
        }

        try
        {
            if (link.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
                profile = ParseVless(link);
            else if (link.StartsWith("anytls://", StringComparison.OrdinalIgnoreCase))
                profile = ParseAnyTls(link);
            else if (link.StartsWith("naive+https://", StringComparison.OrdinalIgnoreCase))
                profile = ParseNaive(link);
            else
            {
                error = Loc.T("Link_UnsupportedScheme");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = Loc.T("Link_ParseFailed", ex.Message);
            return false;
        }
    }
    
    private static ServerProfile ParseVless(string link)
    {
        var uri = new Uri(link);
        var q = HttpUtility.ParseQueryString(uri.Query);

        var security = q["security"] ?? "";
        var transport = q["type"] ?? "tcp";
        var isReality = security.Equals("reality", StringComparison.OrdinalIgnoreCase);

        var profile = new ServerProfile
        {
            Name = Label(uri, uri.Host),
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 443,
            Uuid = Uri.UnescapeDataString(uri.UserInfo),
            Sni = q["sni"] ?? q["host"] ?? uri.Host,
            PublicKey = q["pbk"] ?? "",
            ShortId = q["sid"] ?? "",
            Flow = q["flow"] ?? "",
            Path = q["path"] ?? "/",
            HttpHost = q["host"] ?? "",
            Mode = q["mode"] ?? "auto",
            Fingerprint = TlsFingerprint.Normalize(q["fp"]),
            
            Protocol = transport.Equals("xhttp", StringComparison.OrdinalIgnoreCase)
                       || transport.Equals("splithttp", StringComparison.OrdinalIgnoreCase)
                ? ProtocolKind.VlessXhttp
                : transport.Equals("ws", StringComparison.OrdinalIgnoreCase)
                    ? ProtocolKind.VlessWs
                    : ProtocolKind.VlessReality
        };

        if (profile.Protocol == ProtocolKind.VlessReality && string.IsNullOrWhiteSpace(profile.PublicKey))
            throw new FormatException(Loc.T("Link_RealityNeedsPbk"));

        return profile;
    }
    
    private static ServerProfile ParseAnyTls(string link)
    {
        var uri = new Uri(link);
        var q = HttpUtility.ParseQueryString(uri.Query);

        return new ServerProfile
        {
            Name = Label(uri, uri.Host),
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 443,
            Password = Uri.UnescapeDataString(uri.UserInfo),
            Sni = q["sni"] ?? uri.Host,
            Fingerprint = TlsFingerprint.Normalize(q["fp"]),
            Protocol = ProtocolKind.AnyTls
        };
    }

    private static ServerProfile ParseNaive(string link)
    {
        var uri = new Uri(link["naive+".Length..]);
        var credentials = uri.UserInfo.Split(':', 2);

        return new ServerProfile
        {
            Name = Label(uri, uri.Host),
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 443,
            Username = Uri.UnescapeDataString(credentials[0]),
            Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : "",
            Sni = uri.Host,
            Protocol = ProtocolKind.Naive
        };
    }

    private static string Label(Uri uri, string fallback)
    {
        var fragment = uri.Fragment.TrimStart('#');
        return fragment.Length > 0 ? Uri.UnescapeDataString(fragment) : fallback;
    }
}
