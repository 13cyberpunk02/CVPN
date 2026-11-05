using CVPN.Core;

namespace CVPN.Services;

public sealed class AppSettings : ObservableObject
{
    private string _logLevel = "info";
    
    /// <summary>Полный путь к sing-box.exe.</summary>
    public string CorePath
    {
        get;
        set => Set(ref field, value);
    } = AppPaths.DefaultCorePath;

    /// <summary>TUN перехватывает весь системный трафик, но требует прав администратора.</summary>
    public bool TunEnabled
    {
        get;
        set => Set(ref field, value);
    } = true;

    /// <summary>HTTP+SOCKS на одном порту - для приложений, которые настраивают прокси сами.</summary>
    public int MixedPort
    {
        get;
        set => Set(ref field, value);
    } = 2080;

    public string RemoteDns
    {
        get;
        set => Set(ref field, value);
    } = "https://1.1.1.1/dns-query";

    public string LocalDns
    {
        get;
        set => Set(ref field, value);
    } = "8.8.8.8";

    /// <summary>Что делать с трафиком вне правил: true - через прокси, false - напрямую.</summary>
    public bool ProxyByDefault
    {
        get;
        set => Set(ref field, value);
    } = true;

    /// <summary>Порт Clash API - через него читаются счётчики трафика и задержки.</summary>
    public int ClashApiPort
    {
        get;
        set => Set(ref field, value);
    } = 9191;

    public string LogLevel { get => _logLevel; set => Set(ref _logLevel, value); }

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
        get;
        set => Set(ref field, value);
    } = true;

    /// <summary>Запись в разделе автозапуска Windows. Окно открывается сразу в трее.</summary>
    public bool AutoStart
    {
        get;
        set => Set(ref field, value);
    }

    /// <summary>Подключаться к последнему профилю сразу после запуска.</summary>
    public bool AutoConnect
    {
        get;
        set => Set(ref field, value);
    }

    /// <summary>
    /// Ссылка на подписку. Шифруется вместе с остальными секретами: в панелях
    /// её обычно выдают с токеном доступа прямо в адресе.
    /// </summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(Core.ProtectedStringConverter))]
    public string SubscriptionUrl
    {
        get;
        set => Set(ref field, value);
    } = string.Empty;

    /// <summary>Ядро само держит соединение на быстрейшем сервере и перепроверяет раз в три минуты.</summary>
    public bool AutoSelectFastest
    {
        get;
        set => Set(ref field, value);
    }

    /// <summary>
    /// Поднимать туннель через службу, если она установлена. Так TUN работает
    /// без запроса прав администратора при каждом запуске.
    /// </summary>
    public bool UseService
    {
        get;
        set => Set(ref field, value);
    } = true;

    /// <summary>
    /// Код языка интерфейса: «en», «ru» или пустая строка - язык системы.
    /// Применяется при запуске.
    /// </summary>
    public string Language
    {
        get;
        set => Set(ref field, value);
    } = string.Empty;

    /// <summary>Проверять новую версию на GitHub при запуске.</summary>
    public bool CheckUpdates
    {
        get;
        set => Set(ref field, value);
    } = true;

    /// <summary>Обновлять подписку не чаще раза в сутки при запуске.</summary>
    public bool AutoUpdateSubscription
    {
        get;
        set => Set(ref field, value);
    }

    /// <summary>
    /// Когда подписка обновлялась в последний раз. Хранится, чтобы не дёргать
    /// сервер при каждом запуске приложения.
    /// </summary>
    public DateTimeOffset? SubscriptionUpdated
    {
        get;
        set => Set(ref field, value);
    }

    /// <summary>
    /// Запрещать трафик мимо туннеля. Защищает от утечки при обрыве, но при
    /// сбое приложения может оставить систему без интернета - снимается
    /// автоматически при следующем запуске.
    /// </summary>
    public bool KillSwitch
    {
        get;
        set => Set(ref field, value);
    }

    /// <summary>
    /// strict_route у TUN. Защищает от утечек мимо туннеля, но на Windows ставит
    /// фильтры на уровне системы, и под ними могут перестать открываться
    /// локальные сервисы. Выключатель - для диагностики и обхода.
    /// </summary>
    public bool StrictRoute
    {
        get;
        set => Set(ref field, value);
    } = true;

    /// <summary>
    /// Дать TUN-адаптеру IPv6-адрес. Без него строгая маршрутизация считает IPv6
    /// неподдерживаемым и глушит его целиком, вместе с ::1 - а на ::1 по умолчанию
    /// слушают dev-серверы Node (Angular, Vite и т. п.). Сам IPv6-интернет при этом
    /// отклоняется правилом маршрута, так что трафик идёт по IPv4, как и раньше.
    /// </summary>
    public bool TunIpv6
    {
        get;
        set => Set(ref field, value);
    } = true;

    /// <summary>
    /// Запускать ByeDPI вместе с туннелем. Правила «Через ByeDPI» без этого
    /// работают как обычный прокси - страница не должна ломать маршруты.
    /// </summary>
    public bool ByeDpiEnabled
    {
        get;
        set => Set(ref field, value);
    }

    /// <summary>Путь к ciadpi.exe. ByeDPI в приложение не вшит: его ставят отдельно.</summary>
    public string ByeDpiPath
    {
        // Пустое поле - значит «тот, что поставлен с приложением». Так путь
        // подставляется независимо от того, что лежит в старом profiles.json
        get => string.IsNullOrWhiteSpace(field) ? AppPaths.DetectByeDpiPath() : field;
        set => Set(ref field, value);
    } = AppPaths.DetectByeDpiPath();

    /// <summary>
    /// Аргументы ciadpi как их ввёл человек. Адрес и порт приложение
    /// подставляет само, см. <see cref="ByeDpiArguments"/>.
    /// </summary>
    public string ByeDpiArgs
    {
        get;
        set => Set(ref field, value);
    } = "-m1 -d3+s -s5+s -d9+s -r-6+se -r16+s -Qr -o1 -q1 -u2";

    /// <summary>Локальный SOCKS-порт ciadpi.</summary>
    public int ByeDpiPort
    {
        get;
        set => Set(ref field, value);
    } = 1081;

    /// <summary>
    /// В режиме TUN привязывать исходящие соединения ciadpi к адресу физического
    /// адаптера, чтобы они шли мимо туннеля и приёмы обхода DPI работали как у
    /// самостоятельного ByeDPI.
    /// </summary>
    public bool ByeDpiBypassTun
    {
        get;
        set => Set(ref field, value);
    } = true;

    /// <summary>
    /// Для сайтов ByeDPI отбрасывать QUIC (UDP 443): браузер тогда переходит на
    /// TCP, где приёмы ByeDPI работают. Через UDP они почти бесполезны.
    /// </summary>
    public bool ByeDpiBlockQuic
    {
        get;
        set => Set(ref field, value);
    } = true;
}