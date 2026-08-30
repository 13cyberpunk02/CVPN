using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using CVPN.Core;
using CVPN.Ipc;
using CVPN.Models;
using CVPN.Models.Enums;
using CVPN.Services;
using CVPN.Shared;

namespace CVPN.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const int MaxLogLines = 500;

    private readonly StoredState _state;
    private readonly DispatcherTimer _uptimeTimer;
    private SingBoxService? _core;
    private bool? _sessionTun;
    private bool _viaService;
    private bool _adapterErrorSeen;
    private bool _adapterRetryUsed;
    private DispatcherTimer? _serviceLogTimer;
    private RoutingProfile _routing;
    private ClashApiClient? _stats;
    private DateTime _connectedAt;

    private TunnelState _tunnel = TunnelState.Disconnected;
    private ServerProfile? _active;
    private string _uptime = "00:00:00";
    private string _download = "0.0";
    private string _upload = "0.0";
    private string _downloadUnit = "КБ/с";
    private string _uploadUnit = "КБ/с";
    private string _singBoxVersion = "ядро не найдено";
    private string _status = "";

    public MainViewModel()
    {
        _state = ProfileStore.Load();

        Profiles = new ObservableCollection<ServerProfile>(_state.Profiles);
        RoutingProfiles = new ObservableCollection<RoutingProfile>(_state.RoutingProfiles);
        Settings = _state.Settings;

        _routing = RoutingProfiles.FirstOrDefault(r => r.Name == _state.ActiveRoutingProfile)
                   ?? RoutingProfiles[0];

        Active = Profiles.FirstOrDefault(p => p.Name == _state.ActiveProfileName)
                 ?? Profiles.FirstOrDefault();

        // Импорт добавляет профили в уже открытое приложение: если активного нет, берём первый
        Profiles.CollectionChanged += (_, _) => Active ??= Profiles.FirstOrDefault();

        Settings.PropertyChanged += (_, _) => RaiseMode();

        ConnectionsPage = new ConnectionsViewModel(this);
        LogsPage = new LogsViewModel(this);
        RoutingPage = new RoutingViewModel(this);
        ProfilesPage = new ProfilesViewModel(this);
        SettingsPage = new SettingsViewModel(this);

        SubscribeRules();

        ToggleConnection = new RelayCommand(async () => await ToggleAsync());


        OpenConfig = new RelayCommand(ShowGeneratedConfig);
        MeasureDelay = new RelayCommand(async () => await MeasureAsync(), () => IsConnected);
        CheckConfig = new RelayCommand(async () => await CheckAsync());

        _uptimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _uptimeTimer.Tick += (_, _) => Uptime = (DateTime.Now - _connectedAt).ToString(@"hh\:mm\:ss");

        // Реестр мог разойтись с настройкой, если приложение перенесли
        AutoStart.Sync(Settings.AutoStart);
        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.AutoStart)) AutoStart.Apply(Settings.AutoStart);
        };

        StampBuild();

        if (ProfileStore.LastLoadWarning.Length > 0)
        {
            Status = ProfileStore.LastLoadWarning;
            Append($"[cvpn] {Status.ToLowerInvariant()}");
        }

        _ = DetectCoreAsync();
        ReportMissingFlags();

        _ = SettingsPage.StartupCheckAsync();
    }

    public ObservableCollection<ServerProfile> Profiles { get; }

    /// <summary>
    /// Страница соединений. Экземпляр один: навигация пересоздаёт разметку,
    /// но состояние страницы должно переживать переходы.
    /// </summary>
    public ConnectionsViewModel ConnectionsPage { get; }

    public RoutingViewModel RoutingPage { get; }

    public LogsViewModel LogsPage { get; }
    public ProfilesViewModel ProfilesPage { get; }
    public SettingsViewModel SettingsPage { get; }

    public ObservableCollection<RoutingProfile> RoutingProfiles { get; }

    /// <summary>Правила активного набора. При смене набора коллекция подменяется целиком.</summary>
    public ObservableCollection<RouteRule> Rules => ActiveRouting.Rules;

    /// <summary>
    /// Активный набор правил. Смена на лету требует перезапуска ядра:
    /// маршруты живут в конфиге, а не в Clash API, в отличие от выбора сервера.
    /// </summary>
    public RoutingProfile ActiveRouting
    {
        get => _routing;
        set
        {
            if (value is null || ReferenceEquals(value, _routing)) return;

            UnsubscribeRules();
            Set(ref _routing, value);
            SubscribeRules();

            _state.ActiveRoutingProfile = value.Name;

            Raise(nameof(Rules));
            Raise(nameof(ActiveRuleCount));
            Persist();

            if (IsConnected) Status = "Набор правил применится после переподключения";
        }
    }

    public ObservableCollection<string> Log { get; } = [];

    /// <summary>Живые соединения. Обновляются, пока открыта страница «Соединения».</summary>
    public AppSettings Settings { get; }

    public ICommand ToggleConnection { get; }
    public ICommand OpenConfig { get; }
    public ICommand MeasureDelay { get; }
    public ICommand CheckConfig { get; }

    // ===================== состояние =====================

    public TunnelState State
    {
        get => _tunnel;
        private set
        {
            if (!Set(ref _tunnel, value)) return;
            Raise(nameof(StateLabel));
            Raise(nameof(IsConnected));
            Raise(nameof(PrimaryActionLabel));
        }
    }

    public bool IsConnected => State == TunnelState.Connected;

    /// <summary>
    /// Доступ к Clash API для страниц. Временный мостик: состояние туннеля
    /// просится в отдельный сервис, но пока живёт здесь.
    /// </summary>
    public ClashApiClient? Api => _stats;

    /// <summary>Сообщение в строке состояния и в логе - общая точка для страниц.</summary>
    public void Notify(string message)
    {
        Status = message;
        Append($"[cvpn] {message.ToLowerInvariant()}");
    }

    // ===================== режим сессии =====================

    /// <summary>Режим запущенной сессии; если ядро не работает - то, что выбрано в настройках.</summary>
    private bool EffectiveTun => _sessionTun ?? Settings.TunEnabled;

    public string ModeTitle => EffectiveTun
        ? _viaService ? "TUN · служба" : "TUN"
        : "Системный прокси";

    public string ModeDetail => EffectiveTun
        ? "весь трафик системы"
        : $"127.0.0.1:{Settings.MixedPort}";

    /// <summary>Режим активен только при живом туннеле - иначе это просто настройка.</summary>
    public bool ModeActive => _sessionTun is not null;

    /// <summary>Настройку поменяли на ходу: к текущей сессии она не относится.</summary>
    public bool ModePending => _sessionTun is not null && _sessionTun != Settings.TunEnabled;

    public string ModePendingHint => Settings.TunEnabled
        ? "после переподключения: TUN"
        : "после переподключения: прокси";

    private void RaiseMode()
    {
        Raise(nameof(ModeTitle));
        Raise(nameof(ModeDetail));
        Raise(nameof(ModeActive));
        Raise(nameof(ModePending));
        Raise(nameof(ModePendingHint));
    }

    public string StateLabel => State switch
    {
        TunnelState.Connected => "ПОДКЛЮЧЕНО",
        TunnelState.Connecting => "ПОДКЛЮЧЕНИЕ",
        TunnelState.Failing => "ОШИБКА",
        _ => "ОТКЛЮЧЕНО"
    };

    public string PrimaryActionLabel => State is TunnelState.Connected or TunnelState.Connecting
        ? "Отключить"
        : "Подключить";

    public ServerProfile? Active
    {
        get => _active;
        set
        {
            if (_active is not null) _active.IsActive = false;
            Set(ref _active, value);
            if (_active is not null) _active.IsActive = true;
            _state.ActiveProfileName = _active?.Name;
        }
    }

    public string Uptime
    {
        get => _uptime;
        private set => Set(ref _uptime, value);
    }

    public string Download
    {
        get => _download;
        private set => Set(ref _download, value);
    }

    public string Upload
    {
        get => _upload;
        private set => Set(ref _upload, value);
    }

    public string DownloadUnit
    {
        get => _downloadUnit;
        private set => Set(ref _downloadUnit, value);
    }

    public string UploadUnit
    {
        get => _uploadUnit;
        private set => Set(ref _uploadUnit, value);
    }

    public string SingBoxVersion
    {
        get => _singBoxVersion;
        private set => Set(ref _singBoxVersion, value);
    }


    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public int ActiveRuleCount => Rules.Count(r => r.Enabled);

    // ===================== подключение =====================

    private async Task ToggleAsync()
    {
        // Ручное нажатие - новая попытка: прошлый неудачный запуск не должен
        // блокировать автоповтор навсегда
        _adapterRetryUsed = false;

        if (State is TunnelState.Connected or TunnelState.Connecting)
        {
            await DisconnectAsync();
            return;
        }

        await ConnectAsync();
    }

    private async Task ConnectAsync()
    {
        if (Active is null)
        {
            // Раньше кнопка в этом случае просто не нажималась, и причина была не видна
            Status = Profiles.Count == 0
                ? "Нет ни одного профиля. Добавьте его на странице «Профили»."
                : "Профиль не выбран. Откройте «Профили» и нажмите «Выбрать».";
            Append($"[cvpn] {Status}");
            return;
        }

        if (!File.Exists(Settings.CorePath))
        {
            Fail($"sing-box.exe не найден: {Settings.CorePath}");
            return;
        }

        // TUN не поднимется без прав администратора. Раньше здесь была просто ошибка -
        // теперь предлагаем повышение, потому что иначе перехватывать трафик нечем.
        _viaService = Settings.UseService && await ServiceClient.IsAvailableAsync();

        // Служба сама работает под LocalSystem - прав у приложения не требуется
        if (Settings.TunEnabled && !_viaService && !Elevation.IsElevated && !RequestElevation()) return;

        State = TunnelState.Connecting;
        Append("[cvpn] сборка конфигурации");

        string configPath;
        try
        {
            configPath = ConfigBuilder.Write([.. Profiles], Active, ActiveRouting, Settings);
        }
        catch (Exception ex)
        {
            Fail($"не удалось собрать конфигурацию: {ex.Message}");
            return;
        }

        ExplainRouting();

        // Зависший адаптер от прошлого сеанса - самая частая причина отказа TUN
        if (Settings.TunEnabled && !_viaService)
        {
            var cleaned = await TunAdapterCleaner.RemoveStaleAsync();
            if (cleaned.Length > 0) Append($"[cvpn] {cleaned}");
        }

        if (_viaService)
        {
            Append("[cvpn] запуск через службу CVPN Tunnel");

            var configText = File.ReadAllText(configPath);
            var ruleSets = RuleSetPayload.Collect(configText);

            if (ruleSets.Count > 0)
                Append($"[cvpn] службе передано наборов правил: {ruleSets.Count}");

            var response = await ServiceClient.StartAsync(configText, ruleSets);

            if (response?.Ok != true)
            {
                Fail(response?.Message ?? "служба не ответила");
                return;
            }

            StartServiceLogPump();
        }
        else
        {
            _core = new SingBoxService(Settings.CorePath);
            _core.LineReceived += OnCoreLine;
            _core.Exited += OnCoreExited;

            var (ok, message) = await _core.CheckConfigAsync(configPath);
            if (!ok)
            {
                Fail($"конфигурация отклонена ядром: {message}");
                await TeardownAsync();
                return;
            }

            _core.Start(configPath);
        }

        // Без TUN трафик надо кому-то отдать: прописываем mixed-порт системным прокси
        if (!Settings.TunEnabled)
        {
            try
            {
                SystemProxy.Enable(Settings.MixedPort);
                Append($"[cvpn] системный прокси: 127.0.0.1:{Settings.MixedPort}");
            }
            catch (Exception ex)
            {
                Append($"[cvpn] не удалось прописать системный прокси: {ex.Message}");
            }
        }

        _sessionTun = Settings.TunEnabled;
        RaiseMode();

        _stats = new ClashApiClient(Settings.ClashApiPort);
        _stats.TrafficReceived += OnTraffic;
        _stats.Start();

        if (Settings.KillSwitch)
        {
            var problem = await KillSwitch.EnableAsync(Settings.CorePath);

            if (problem.Length > 0)
            {
                Append($"[cvpn] {problem}");
                Status = problem;
            }
            else
            {
                Append("[cvpn] kill switch включён: трафик мимо туннеля запрещён");
            }
        }

        _adapterErrorSeen = false;
        _connectedAt = DateTime.Now;
        _uptimeTimer.Start();
        State = TunnelState.Connected;
        Status = "";
        Append(Settings.AutoSelectFastest && Profiles.Count > 1
            ? $"[cvpn] автовыбор быстрейшего из {Profiles.Count} серверов"
            : $"[cvpn] подключение к {Active.Name} ({Active.ProtocolLabel})");

        // Первый замер с задержкой: ядру нужно поднять Clash API и сам туннель
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            await MeasureAsync();
        });
    }

    private async Task DisconnectAsync()
    {
        _adapterRetryUsed = false;
        _adapterErrorSeen = false;

        SystemProxy.Restore();
        _uptimeTimer.Stop();
        if (Active is not null) Active.LatencyMs = -1;
        Uptime = "00:00:00";
        Download = "0.0";
        Upload = "0.0";
        DownloadUnit = "КБ/с";
        UploadUnit = "КБ/с";

        await TeardownAsync();

        State = TunnelState.Disconnected;
        Append("[cvpn] отключено");
    }

    private async Task TeardownAsync()
    {
        if (KillSwitch.IsActive)
        {
            var problem = await KillSwitch.DisableAsync();
            Append(problem.Length > 0
                ? $"[cvpn] не удалось снять kill switch: {problem}"
                : "[cvpn] kill switch снят");
        }

        _sessionTun = null;
        RaiseMode();

        if (_viaService)
        {
            _serviceLogTimer?.Stop();
            _serviceLogTimer = null;
            await ServiceClient.StopAsync();
            _viaService = false;
        }

        if (_stats is not null)
        {
            _stats.TrafficReceived -= OnTraffic;
            await _stats.StopAsync();
            _stats = null;
        }

        if (_core is null) return;

        _core.LineReceived -= OnCoreLine;
        _core.Exited -= OnCoreExited;
        await _core.StopAsync();
        _core = null;
    }

    private async Task CheckAsync()
    {
        if (Active is null) return;

        try
        {
            var path = ConfigBuilder.Write([.. Profiles], Active, ActiveRouting, Settings);
            var service = new SingBoxService(Settings.CorePath);
            var (ok, message) = await service.CheckConfigAsync(path);
            Status = ok ? "Конфигурация корректна" : message;
            Append($"[cvpn] проверка: {Status}");
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }

    /// <summary>Перечитать версию ядра - например, после смены пути.</summary>
    public void RefreshCoreVersion() => _ = DetectCoreAsync();

    private async Task DetectCoreAsync()
    {
        if (!File.Exists(Settings.CorePath))
        {
            SingBoxVersion = "ядро не найдено";
            return;
        }

        try
        {
            var service = new SingBoxService(Settings.CorePath);
            SingBoxVersion = await service.GetVersionAsync();
        }
        catch
        {
            SingBoxVersion = "ядро не отвечает";
        }
    }

    /// <summary>
    /// Повышение прав возможно только перезапуском процесса: UAC нельзя запросить
    /// для уже работающего приложения.
    /// </summary>
    private bool RequestElevation()
    {
        // Задача планировщика уже несёт нужные права - окно UAC не понадобится
        if (ElevatedTask.Exists)
        {
            if (ElevatedTask.Launch(out var launchError))
            {
                Persist();
                Application.Current?.Shutdown();
                return false;
            }

            Append($"[cvpn] задача планировщика не запустилась: {launchError}");
        }

        var answer = MessageBox.Show(
            "Режим TUN перехватывает весь системный трафик и требует прав администратора.\n\n" +
            "Перезапустить CVPN с повышением прав?\n\n" +
            "Если отказаться, можно выключить TUN в настройках - тогда трафик пойдёт " +
            "через системный прокси, но только для приложений, которые его учитывают.",
            "CVPN", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
        {
            Status = "Подключение отменено: для TUN нужны права администратора";
            State = TunnelState.Disconnected;
            return false;
        }

        if (!Elevation.RelaunchElevated())
        {
            Fail("Windows отклонила запрос на повышение прав");
            return false;
        }

        Persist();
        Application.Current?.Shutdown();
        return false;
    }

    /// <summary>Вызывается при закрытии окна: нельзя оставить систему с чужим прокси.</summary>
    public async Task ShutdownAsync()
    {
        SystemProxy.Restore();
        Persist();
        await TeardownAsync();
    }

    // ===================== события ядра =====================

    private void OnCoreLine(string line) => Dispatch(() => Append(line));

    private void OnCoreExited(int exitCode, bool graceful) => Dispatch(async () =>
    {
        if (graceful) return;

        SystemProxy.Restore();
        _uptimeTimer.Stop();

        // Одна автоматическая попытка: Windows освобождает адаптер с задержкой,
        // и повторный запуск через пару секунд обычно проходит
        if (_adapterErrorSeen && !_adapterRetryUsed)
        {
            _adapterRetryUsed = true;
            _adapterErrorSeen = false;

            Append("[cvpn] остался TUN-адаптер от прошлого запуска, повтор через 3 с");
            Status = "Освобождение сетевого интерфейса…";
            State = TunnelState.Connecting;

            await TeardownAsync();
            await Task.Delay(TimeSpan.FromSeconds(3));
            await ConnectAsync();

            return;
        }

        State = TunnelState.Failing;
        Status = $"Ядро завершилось с кодом {exitCode}. Подробности в логах.";
        Append($"[cvpn] ядро остановлено, код {exitCode}");
    });

    /// <summary>Задержку меряет само ядро через Clash API - свой пинг мимо туннеля бессмыслен.</summary>
    private async Task MeasureAsync()
    {
        if (_stats is null || Active is null) return;

        var ms = await _stats.MeasureDelayAsync();

        Dispatch(() =>
        {
            if (Active is null) return;

            Active.LatencyMs = ms;
            if (ms < 0) Append("[cvpn] проверка задержки не прошла");
        });
    }

    /// <summary>Счётчик активных правил зависит и от состава списка, и от галочки в каждом правиле.</summary>
    private void SubscribeRules()
    {
        Rules.CollectionChanged += OnRulesChanged;
        foreach (var rule in Rules) rule.PropertyChanged += OnRulePropertyChanged;
    }

    private void UnsubscribeRules()
    {
        Rules.CollectionChanged -= OnRulesChanged;
        foreach (var rule in Rules) rule.PropertyChanged -= OnRulePropertyChanged;
    }

    private void OnRulesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        foreach (var rule in e.NewItems?.OfType<RouteRule>() ?? [])
            rule.PropertyChanged += OnRulePropertyChanged;
        foreach (var rule in e.OldItems?.OfType<RouteRule>() ?? [])
            rule.PropertyChanged -= OnRulePropertyChanged;

        Raise(nameof(ActiveRuleCount));
    }

    private void OnRulePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RouteRule.Enabled)) Dispatch(() => Raise(nameof(ActiveRuleCount)));
    }

    private void OnTraffic(long up, long down) => Dispatch(() =>
    {
        (Upload, UploadUnit) = FormatRate(up);
        (Download, DownloadUnit) = FormatRate(down);
    });

    /// <summary>Килобайты до мегабайта, дальше мегабайты - иначе на медленном канале одни нули.</summary>
    private static (string Value, string Unit) FormatRate(long bytesPerSecond)
    {
        const double kb = 1024;
        const double mb = kb * 1024;

        if (bytesPerSecond >= mb)
            return ((bytesPerSecond / mb).ToString("0.0"), "МБ/с");

        return ((bytesPerSecond / kb).ToString("0.0"), "КБ/с");
    }

    public void AddRule(MatchKind match, string value, RouteAction action)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        Rules.Add(new RouteRule { Match = match, Value = value.Trim(), Action = action });
        Raise(nameof(ActiveRuleCount));
        Persist();
    }

    public void Persist()
    {
        _state.Profiles = [.. Profiles];
        _state.RoutingProfiles = [.. RoutingProfiles];
        _state.ActiveRoutingProfile = ActiveRouting.Name;
        _state.Settings = Settings;
        ProfileStore.Save(_state);
    }


    /// <summary>Открывает сгенерированный config.json - удобно сверить с рабочим вручную.</summary>
    private void ShowGeneratedConfig()
    {
        try
        {
            if (Active is not null) ConfigBuilder.Write([.. Profiles], Active, ActiveRouting, Settings);

            if (!File.Exists(AppPaths.GeneratedConfig))
            {
                Status = "Конфигурация ещё не собрана";
                return;
            }

            Process.Start(new ProcessStartInfo(AppPaths.GeneratedConfig) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Status = $"Не удалось открыть конфигурацию: {ex.Message}";
        }
    }

    /// <summary>Автоподключение при старте - вызывается окном после загрузки.</summary>
    public async Task StartupAsync()
    {
        if (!Settings.AutoConnect || Active is null) return;

        Append("[cvpn] автоподключение");
        await ConnectAsync();
    }

    /// <summary>
    /// Служба не может писать в наш лог напрямую, поэтому раз в секунду
    /// забираем накопленные строки. Очередь на стороне службы ограничена,
    /// так что при простое ничего не растёт.
    /// </summary>
    private void StartServiceLogPump()
    {
        _serviceLogTimer?.Stop();

        _serviceLogTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _serviceLogTimer.Tick += async (_, _) =>
        {
            var status = await ServiceClient.StatusAsync();
            if (status is null) return;

            foreach (var line in status.Log) Append(line);

            if (!status.Running && State == TunnelState.Connected)
            {
                State = TunnelState.Failing;
                Status = "Служба сообщает, что ядро остановлено";
            }
        };

        _serviceLogTimer.Start();
    }

    /// <summary>
    /// Пишет в лог, как разошлись правила. Помогает понять, почему сайт всё ещё
    /// идёт через прокси: чаще всего домена просто нет в наборе.
    /// </summary>
    private void ExplainRouting()
    {
        var active = Rules.Where(r => r.Enabled).ToList();
        var direct = active.Count(r => r.Action == RouteAction.Direct);
        var blocked = active.Count(r => r.Action == RouteAction.Block);

        Append($"[cvpn] набор «{ActiveRouting.Name}»: правил {active.Count} " +
               $"(напрямую {direct}, блок {blocked}), " +
               $"остальное {(ActiveRouting.ProxyByDefault ? "через прокси" : "напрямую")}");

        // В DNS переносятся только доменные условия и локальные наборы:
        // остальное на момент инициализации ядру недоступно
        var dnsUnfriendly = active
            .Where(r => r.Action == RouteAction.Direct)
            .Where(r => r.Match is MatchKind.Geoip or MatchKind.Geosite
                or MatchKind.RuleSetRemote or MatchKind.Process)
            .Select(r => $"{r.MatchLabel} {r.DisplayValue}")
            .ToList();

        if (dnsUnfriendly.Count > 0)
        {
            Append($"[cvpn] правила «напрямую» ({string.Join(", ", dnsUnfriendly)}) действуют " +
                   "только для соединений, но не для DNS: домены резолвятся через туннель. " +
                   "Для сайтов с геобалансировкой добавьте правило по домену.");
        }
    }

    /// <summary>
    /// Смена сервера. При живом туннеле идёт через селектор Clash API -
    /// ядро не перезапускается, существующие соединения не рвутся.
    /// Перегенерировать конфиг нужно только чтобы выбор пережил перезапуск.
    /// </summary>
    public async Task SelectServerAsync(ServerProfile profile)
    {
        Active = profile;
        Persist();

        if (!IsConnected || _stats is null) return;

        var tag = ConfigBuilder.BuildTags([.. Profiles]).GetValueOrDefault(profile);
        if (tag is null) return;

        if (await _stats.SelectAsync(tag))
        {
            Append($"[cvpn] переключение на {profile.Name} без перезапуска ядра");
            ConfigBuilder.Write([.. Profiles], profile, ActiveRouting, Settings);
            await MeasureAsync();
        }
        else
        {
            Status = "Не удалось переключить сервер. Переподключитесь вручную.";
            Append("[cvpn] селектор не ответил, нужно переподключение");
        }
    }

    /// <summary>
    /// Если флаг не загрузился, об этом надо сказать: молча пустое место
    /// выглядит как ошибка вёрстки, хотя дело в отсутствующем файле ресурса.
    /// </summary>
    private void ReportMissingFlags()
    {
        foreach (var profile in Profiles)
        {
            _ = profile.FlagImage;
        }

        if (FlagCatalog.Missing.Count == 0) return;

        Append($"[cvpn] флаги не загружены: {string.Join(", ", FlagCatalog.Missing)}");
        Append("[cvpn] проверьте, что файлы Assets/Flags/*.png добавлены в проект с действием Resource");
    }

    /// <summary>
    /// Версия и путь к запущенному файлу - первое, что нужно знать при разборе
    /// «правка не подействовала»: часто работает не та сборка, которую собрали.
    /// </summary>
    private void StampBuild()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;

        Append($"[cvpn] сборка {version} · {Environment.ProcessPath}");

        if (!ElevatedTask.Exists || ElevatedTask.PathMatchesCurrent()) return;
        Append($"[cvpn] внимание: задача планировщика запускает другой файл - {ElevatedTask.RegisteredPath()}");
        Append("[cvpn] пересоздайте задачу в настройках, иначе изменения не применятся");
    }

    /// <summary>Отметка о нажатии на круг - для диагностики привязок.</summary>
    public void NoteDialClick() =>
        Append($"[cvpn] нажатие: состояние {StateLabel}, профиль {Active?.Name ?? "не выбран"}");

    // ===================== служебное =====================

    private void Fail(string message)
    {
        State = TunnelState.Failing;
        Status = message;
        Append($"[cvpn] {message}");
    }

    /// <summary>
    /// Признак «осиротевшего» TUN-адаптера: прошлый процесс sing-box был снят
    /// принудительно и не успел удалить интерфейс. Создать новый нельзя -
    /// он уже есть, открыть существующий тоже нельзя - запись повреждена.
    /// </summary>
    private static bool IsStaleAdapterError(string line) =>
        line.Contains("configure tun interface", StringComparison.OrdinalIgnoreCase)
        || line.Contains("create adapter", StringComparison.OrdinalIgnoreCase);

    private void Append(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        if (IsStaleAdapterError(line)) _adapterErrorSeen = true;

        // В файл пишем всё; на экране остаются последние строки
        FileLog.Current.Write(line);

        Log.Add(line);
        while (Log.Count > MaxLogLines) Log.RemoveAt(0);
    }

    /// <summary>Вывод ядра приходит из фонового потока, коллекции WPF этого не прощают.</summary>
    public static void Dispatch(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.Invoke(action);
    }
}