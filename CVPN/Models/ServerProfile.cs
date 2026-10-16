using System.Text.Json.Serialization;
using CVPN.Core;
using CVPN.Models.Enums;

namespace CVPN.Models;

public sealed class ServerProfile : ObservableObject
{
    public string Name
    {
        get;
        set
        {
            Set(ref field, value);
            Raise(nameof(CountryCode));
            Raise(nameof(FlagImage));
        }
    } = string.Empty;

    public string Host
    {
        get;
        set => Set(ref field, value);
    } = string.Empty;

    public int Port
    {
        get;
        set => Set(ref field, value);
    } = 443;

    public ProtocolKind Protocol
    {
        get;
        set
        {
            Set(ref field, value);
            Raise(nameof(ProtocolLabel));
        }
    } = ProtocolKind.VlessReality;

    /// <summary>-1 означает «ещё не измеряли».</summary>
    [JsonIgnore]
    public int LatencyMs
    {
        get;
        set
        {
            Set(ref field, value);
            Raise(nameof(LatencyLabel));
            Raise(nameof(LatencyGrade));
        }
    } = -1;

    /// <summary>Состояние интерфейса, в файл не пишется.</summary>
    [JsonIgnore]
    public bool IsActive
    {
        get;
        set => Set(ref field, value);
    }

    [JsonConverter(typeof(ProtectedStringConverter))]
    public string Uuid { get; set; } = string.Empty;

    [JsonConverter(typeof(ProtectedStringConverter))]
    public string Password { get; set; } = string.Empty;

    public string Sni { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string ShortId { get; set; } = string.Empty;
    public string Flow { get; set; } = "xtls-rprx-vision";
    public string Path { get; set; } = "/";

    /// <summary>
    /// Заголовок Host для XHTTP. Отдельно от адреса сервера и от SNI:
    /// у провайдеров там нередко длинная строка с параметрами.
    /// </summary>
    public string HttpHost { get; set; } = "";

    /// <summary>Режим XHTTP: auto, packet-up, stream-up, stream-one.</summary>
    public string Mode { get; set; } = "auto";

    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Ссылка подписки, из которой пришёл профиль. Пусто - создан вручную.
    /// По этому признаку обновление подписки заменяет только свои профили
    /// и не трогает добавленные руками.
    /// </summary>
    public string Subscription { get; set; } = string.Empty;

    [JsonIgnore] public string Endpoint => $"{Host}:{Port}";

    private string _countryCode = string.Empty;

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

        return string.Empty;
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
        ProtocolKind.VlessXhttp => "vless · xhttp",
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