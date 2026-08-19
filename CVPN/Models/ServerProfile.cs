using CVPN.Core;
using CVPN.Models.Enums;

namespace CVPN.Models;

public sealed class ServerProfile : ObservableObject
{
    private string _name = "";
    private string _host = "";
    private int _port = 443;
    private ProtocolKind _protocol = ProtocolKind.VlessReality;
    private int _latencyMs = -1;
    private bool _isActive;
 
    public string Name { get => _name; set { Set(ref _name, value); Raise(nameof(CountryCode)); } }
    public string Host { get => _host; set => Set(ref _host, value); }
    public int Port { get => _port; set => Set(ref _port, value); }
    public ProtocolKind Protocol { get => _protocol; set { Set(ref _protocol, value); Raise(nameof(ProtocolLabel)); } }
 
    /// <summary>-1 означает «ещё не измеряли».</summary>
    public int LatencyMs { get => _latencyMs; set { Set(ref _latencyMs, value); Raise(nameof(LatencyLabel)); } }
 
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }
 
    // Учётные данные протокола
    public string Uuid { get; set; } = "";
    public string Password { get; set; } = "";
    public string Sni { get; set; } = "";
    public string PublicKey { get; set; } = "";
    public string ShortId { get; set; } = "";
    public string Flow { get; set; } = "xtls-rprx-vision";
    public string Path { get; set; } = "/";
    public string Username { get; set; } = "";
 
    public string Endpoint => $"{Host}:{Port}";
 
    private string _countryCode = "";
 
    /// <summary>
    /// Двухбуквенный код страны для значка. Windows не рисует флаги из regional
    /// indicator — эмодзи там показывается как две буквы, поэтому значок текстовый.
    /// </summary>
    public string CountryCode
    {
        get => _countryCode.Length > 0 ? _countryCode : GuessCountry();
        set => Set(ref _countryCode, value.Trim().ToUpperInvariant());
    }
 
    /// <summary>
    /// Догадка по имени профиля и по хосту вида nl-01.example.net.
    /// Именно догадка: всегда можно задать код вручную.
    /// </summary>
    private string GuessCountry()
    {
        foreach (var (needle, code) in Countries)
            if (Name.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return code;
 
        var head = Host.Split('.', '-').FirstOrDefault() ?? "";
        if (head.Length == 2 && head.All(char.IsLetter)) return head.ToUpperInvariant();
 
        return "";
    }
 
    private static readonly (string Name, string Code)[] Countries =
    [
        ("netherlands", "NL"), ("нидерланд", "NL"), ("amsterdam", "NL"),
        ("germany", "DE"), ("герман", "DE"), ("frankfurt", "DE"),
        ("finland", "FI"), ("финлянд", "FI"), ("helsinki", "FI"),
        ("sweden", "SE"), ("швец", "SE"), ("stockholm", "SE"),
        ("france", "FR"), ("франц", "FR"), ("paris", "FR"),
        ("britain", "GB"), ("london", "GB"), ("англ", "GB"),
        ("turkey", "TR"), ("турц", "TR"), ("istanbul", "TR"),
        ("japan", "JP"), ("япон", "JP"), ("tokyo", "JP"),
        ("singapore", "SG"), ("сингапур", "SG"),
        ("poland", "PL"), ("польш", "PL"), ("warsaw", "PL"),
        ("latvia", "LV"), ("латв", "LV"), ("riga", "LV"),
        ("estonia", "EE"), ("эстон", "EE"),
        ("usa", "US"), ("united states", "US"), ("сша", "US"),
        ("canada", "CA"), ("канад", "CA"),
        ("russia", "RU"), ("росси", "RU"), ("moscow", "RU")
    ];
 
    public string ProtocolLabel => Protocol switch
    {
        ProtocolKind.VlessReality => "vless · reality",
        ProtocolKind.VlessWs => "vless · ws + tls",
        ProtocolKind.AnyTls => "anytls",
        ProtocolKind.Naive => "naive",
        _ => "—"
    };
 
    public string LatencyLabel => LatencyMs < 0 ? "—" : $"{LatencyMs} ms";
}

