using System.Text.Json.Serialization;
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

    public string Name
    {
        get => _name;
        set
        {
            Set(ref _name, value);
            Raise(nameof(CountryCode));
            Raise(nameof(FlagImage));
        }
    }

    public string Host
    {
        get => _host;
        set => Set(ref _host, value);
    }

    public int Port
    {
        get => _port;
        set => Set(ref _port, value);
    }

    public ProtocolKind Protocol
    {
        get => _protocol;
        set
        {
            Set(ref _protocol, value);
            Raise(nameof(ProtocolLabel));
        }
    }

    /// <summary>-1 означает «ещё не измеряли».</summary>
    [JsonIgnore]
    public int LatencyMs
    {
        get => _latencyMs;
        set
        {
            Set(ref _latencyMs, value);
            Raise(nameof(LatencyLabel));
            Raise(nameof(LatencyGrade));
        }
    }

    /// <summary>Состояние интерфейса, в файл не пишется.</summary>
    [JsonIgnore]
    public bool IsActive
    {
        get => _isActive;
        set => Set(ref _isActive, value);
    }

    // Учётные данные протокола. UUID и пароль - секреты, они шифруются
    // при записи в файл; остальное восстанавливается из ссылки и тайной не является.
    [JsonConverter(typeof(ProtectedStringConverter))]
    public string Uuid { get; set; } = "";

    [JsonConverter(typeof(ProtectedStringConverter))]
    public string Password { get; set; } = "";

    public string Sni { get; set; } = "";
    public string PublicKey { get; set; } = "";
    public string ShortId { get; set; } = "";
    public string Flow { get; set; } = "xtls-rprx-vision";
    public string Path { get; set; } = "/";
    public string Username { get; set; } = "";

    /// <summary>
    /// Ссылка подписки, из которой пришёл профиль. Пусто - создан вручную.
    /// По этому признаку обновление подписки заменяет только свои профили
    /// и не трогает добавленные руками.
    /// </summary>
    public string Subscription { get; set; } = "";

    [JsonIgnore] public string Endpoint => $"{Host}:{Port}";

    private string _countryCode = "";

    /// <summary>
    /// Двухбуквенный код страны: заданный вручную либо определённый по названию.
    /// Windows не рисует флаги-эмодзи из regional indicator, поэтому флаги
    /// лежат картинками в Assets/Flags.
    /// </summary>
    [JsonIgnore]
    public string CountryCode
    {
        get => _countryCode.Length > 0 ? _countryCode : GuessCountry();
        set
        {
            Set(ref _countryCode, value.Trim().ToUpperInvariant());
            Raise(nameof(FlagImage));
        }
    }

    /// <summary>
    /// В файл пишется только явно заданный код. Пустая строка означает
    /// «определять автоматически» - иначе догадка застыла бы навсегда
    /// и переименование профиля перестало бы на неё влиять.
    /// </summary>
    [JsonPropertyName("CountryCode")]
    public string StoredCountryCode
    {
        get => _countryCode;
        set
        {
            _countryCode = value.Trim().ToUpperInvariant();
            Raise(nameof(CountryCode));
            Raise(nameof(FlagImage));
        }
    }

    /// <summary>Картинка флага или null, если для страны её нет.</summary>
    [JsonIgnore]
    public System.Windows.Media.ImageSource? FlagImage => Services.FlagCatalog.Get(CountryCode);

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

    [JsonIgnore]
    public string ProtocolLabel => Protocol switch
    {
        ProtocolKind.VlessReality => "vless · reality",
        ProtocolKind.VlessWs => "vless · ws + tls",
        ProtocolKind.AnyTls => "anytls",
        ProtocolKind.Naive => "naive",
        _ => "-"
    };

    [JsonIgnore] public string LatencyLabel => LatencyMs < 0 ? "-" : $"{LatencyMs} ms";

    /// <summary>Качество канала для подсветки: unknown · good · fair · poor · dead.</summary>
    [JsonIgnore]
    public string LatencyGrade => LatencyMs switch
    {
        < 0 => "unknown",
        0 => "dead",
        < 80 => "good",
        < 200 => "fair",
        _ => "poor"
    };
}