using CVPN.Core;

namespace CVPN.Services;

public sealed class AppSettings : ObservableObject
{
    private string _corePath = AppPaths.DefaultCorePath;
    private bool _tunEnabled = true;
    private int _mixedPort = 2080;
    private string _remoteDns = "https://1.1.1.1/dns-query";
    private string _localDns = "8.8.8.8";
    private bool _proxyByDefault = true;
    private int _clashApiPort = 9191;
    private string _logLevel = "info";
    private bool _closeToTray = true;
    private bool _autoStart;
    private bool _autoConnect;
    private string _subscriptionUrl = "";
    private bool _autoSelectFastest;
    private bool _useService = true;
    private bool _killSwitch;
    private bool _checkUpdates = true;
    private bool _autoUpdateSubscription;
    private DateTimeOffset? _subscriptionUpdated;

    /// <summary>Полный путь к sing-box.exe.</summary>
    public string CorePath
    {
        get => _corePath;
        set => Set(ref _corePath, value);
    }

    /// <summary>TUN перехватывает весь системный трафик, но требует прав администратора.</summary>
    public bool TunEnabled
    {
        get => _tunEnabled;
        set => Set(ref _tunEnabled, value);
    }

    /// <summary>HTTP+SOCKS на одном порту - для приложений, которые настраивают прокси сами.</summary>
    public int MixedPort
    {
        get => _mixedPort;
        set => Set(ref _mixedPort, value);
    }

    public string RemoteDns
    {
        get => _remoteDns;
        set => Set(ref _remoteDns, value);
    }

    public string LocalDns
    {
        get => _localDns;
        set => Set(ref _localDns, value);
    }

    /// <summary>Что делать с трафиком вне правил: true - через прокси, false - напрямую.</summary>
    public bool ProxyByDefault
    {
        get => _proxyByDefault;
        set => Set(ref _proxyByDefault, value);
    }

    /// <summary>Порт Clash API - через него читаются счётчики трафика и задержки.</summary>
    public int ClashApiPort
    {
        get => _clashApiPort;
        set => Set(ref _clashApiPort, value);
    }

    public string LogLevel
    {
        get => _logLevel;
        set => Set(ref _logLevel, value);
    }

    /// <summary>
    /// Уровень debug заставляет ядро писать, какое правило сработало для каждого
    /// соединения. Единственный способ понять, почему сайт пошёл не туда,
    /// но лог становится очень многословным.
    /// </summary>
    public bool VerboseLog
    {
        get => _logLevel == "debug";
        set
        {
            LogLevel = value ? "debug" : "info";
            Raise();
        }
    }

    /// <summary>Крестик прячет окно в трей вместо выхода. Выйти можно из меню значка.</summary>
    public bool CloseToTray
    {
        get => _closeToTray;
        set => Set(ref _closeToTray, value);
    }

    /// <summary>Запись в разделе автозапуска Windows. Окно открывается сразу в трее.</summary>
    public bool AutoStart
    {
        get => _autoStart;
        set => Set(ref _autoStart, value);
    }

    /// <summary>Подключаться к последнему профилю сразу после запуска.</summary>
    public bool AutoConnect
    {
        get => _autoConnect;
        set => Set(ref _autoConnect, value);
    }

    /// <summary>
    /// Ссылка на подписку. Шифруется вместе с остальными секретами: в панелях
    /// её обычно выдают с токеном доступа прямо в адресе.
    /// </summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(Core.ProtectedStringConverter))]
    public string SubscriptionUrl
    {
        get => _subscriptionUrl;
        set => Set(ref _subscriptionUrl, value);
    }

    /// <summary>Ядро само держит соединение на быстрейшем сервере и перепроверяет раз в три минуты.</summary>
    public bool AutoSelectFastest
    {
        get => _autoSelectFastest;
        set => Set(ref _autoSelectFastest, value);
    }

    /// <summary>
    /// Поднимать туннель через службу, если она установлена. Так TUN работает
    /// без запроса прав администратора при каждом запуске.
    /// </summary>
    public bool UseService
    {
        get => _useService;
        set => Set(ref _useService, value);
    }

    /// <summary>Проверять новую версию на GitHub при запуске.</summary>
    public bool CheckUpdates
    {
        get => _checkUpdates;
        set => Set(ref _checkUpdates, value);
    }

    /// <summary>Обновлять подписку не чаще раза в сутки при запуске.</summary>
    public bool AutoUpdateSubscription
    {
        get => _autoUpdateSubscription;
        set => Set(ref _autoUpdateSubscription, value);
    }

    /// <summary>
    /// Когда подписка обновлялась в последний раз. Хранится, чтобы не дёргать
    /// сервер при каждом запуске приложения.
    /// </summary>
    public DateTimeOffset? SubscriptionUpdated
    {
        get => _subscriptionUpdated;
        set => Set(ref _subscriptionUpdated, value);
    }

    /// <summary>
    /// Запрещать трафик мимо туннеля. Защищает от утечки при обрыве, но при
    /// сбое приложения может оставить систему без интернета - снимается
    /// автоматически при следующем запуске.
    /// </summary>
    public bool KillSwitch
    {
        get => _killSwitch;
        set => Set(ref _killSwitch, value);
    }
}