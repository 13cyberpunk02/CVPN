using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using CVPN.Core;
using CVPN.Models;
using CVPN.Models.Enums;
using CVPN.Services;

namespace CVPN.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const int MaxLogLines = 500;
 
    private readonly StoredState _state;
    private readonly DispatcherTimer _uptimeTimer;
    private SingBoxService? _core;
    private bool? _sessionTun;
    private bool _busy;
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
    private string _importLink = "";
    private string _status = "";
 
    public MainViewModel()
    {
        _state = ProfileStore.Load();
 
        Profiles = new ObservableCollection<ServerProfile>(_state.Profiles);
        Rules = new ObservableCollection<RouteRule>(_state.Rules);
        Settings = _state.Settings;
 
        Active = Profiles.FirstOrDefault(p => p.Name == _state.ActiveProfileName)
                 ?? Profiles.FirstOrDefault();
 
        // Импорт добавляет профили в уже открытое приложение: если активного нет, берём первый
        Profiles.CollectionChanged += (_, _) => Active ??= Profiles.FirstOrDefault();
 
        Settings.PropertyChanged += (_, _) => RaiseMode();
 
        // Счётчик активных правил зависит и от состава списка, и от галочки в каждом правиле
        Rules.CollectionChanged += (_, e) =>
        {
            foreach (var rule in e.NewItems?.OfType<RouteRule>() ?? [])
                rule.PropertyChanged += OnRulePropertyChanged;
            foreach (var rule in e.OldItems?.OfType<RouteRule>() ?? [])
                rule.PropertyChanged -= OnRulePropertyChanged;
 
            Raise(nameof(ActiveRuleCount));
        };
 
        foreach (var rule in Rules) rule.PropertyChanged += OnRulePropertyChanged;
 
        ToggleConnection = new RelayCommand(async () => await ToggleAsync());
        ImportLink = new RelayCommand(Import);
        RemoveProfile = new RelayCommand(p => { if (p is ServerProfile sp) DeleteProfile(sp); });
        RemoveRule = new RelayCommand(p => { if (p is RouteRule rr) { Rules.Remove(rr); Persist(); } });
        SelectProfile = new RelayCommand(p => { if (p is ServerProfile sp) { Active = sp; Persist(); } });
        ImportFile = new RelayCommand(ImportFromFile);
        CreateProfile = new RelayCommand(() => EditProfile(null));
        EditProfileCommand = new RelayCommand(p => { if (p is ServerProfile sp) EditProfile(sp); });
        BrowseCore = new RelayCommand(PickCore);
        OpenConfig = new RelayCommand(ShowGeneratedConfig);
        MeasureDelay = new RelayCommand(async () => await MeasureAsync(), () => IsConnected);
        PingAll = new RelayCommand(async () => await PingAllAsync(), () => !IsBusy);
        UpdateSubscription = new RelayCommand(async () => await UpdateSubscriptionAsync(), () => !IsBusy);
        ClearLog = new RelayCommand(Log.Clear, () => Log.Count > 0);
        CopyLog = new RelayCommand(CopyLogToClipboard, () => Log.Count > 0);
        CheckConfig = new RelayCommand(async () => await CheckAsync());
 
        _uptimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _uptimeTimer.Tick += (_, _) => Uptime = (DateTime.Now - _connectedAt).ToString(@"hh\:mm\:ss");
 
        // Реестр мог разойтись с настройкой, если приложение перенесли
        AutoStart.Sync(Settings.AutoStart);
        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.AutoStart)) AutoStart.Apply(Settings.AutoStart);
        };
 
        _ = DetectCoreAsync();
        ReportMissingFlags();
    }
 
    public ObservableCollection<ServerProfile> Profiles { get; }
    public ObservableCollection<RouteRule> Rules { get; }
    public ObservableCollection<string> Log { get; } = [];
    public AppSettings Settings { get; }
 
    public ICommand ToggleConnection { get; }
    public ICommand ImportLink { get; }
    public ICommand RemoveProfile { get; }
    public ICommand RemoveRule { get; }
    public ICommand ImportFile { get; }
    public ICommand CreateProfile { get; }
    public ICommand EditProfileCommand { get; }
    public ICommand BrowseCore { get; }
    public ICommand OpenConfig { get; }
    public ICommand MeasureDelay { get; }
    public ICommand PingAll { get; }
    public ICommand UpdateSubscription { get; }
    public ICommand SelectProfile { get; }
    public ICommand ClearLog { get; }
    public ICommand CopyLog { get; }
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
 
    /// <summary>Идёт долгая операция: пинг всех серверов или загрузка подписки.</summary>
    public bool IsBusy
    {
        get => _busy;
        private set => Set(ref _busy, value);
    }
 
    // ===================== режим сессии =====================
 
    /// <summary>Режим запущенной сессии; если ядро не работает — то, что выбрано в настройках.</summary>
    private bool EffectiveTun => _sessionTun ?? Settings.TunEnabled;
 
    public string ModeTitle => EffectiveTun ? "TUN" : "Системный прокси";
 
    public string ModeDetail => EffectiveTun
        ? "весь трафик системы"
        : $"127.0.0.1:{Settings.MixedPort}";
 
    /// <summary>Режим активен только при живом туннеле — иначе это просто настройка.</summary>
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
 
    public string Uptime { get => _uptime; private set => Set(ref _uptime, value); }
    public string Download { get => _download; private set => Set(ref _download, value); }
    public string Upload { get => _upload; private set => Set(ref _upload, value); }
    public string DownloadUnit { get => _downloadUnit; private set => Set(ref _downloadUnit, value); }
    public string UploadUnit { get => _uploadUnit; private set => Set(ref _uploadUnit, value); }
    public string SingBoxVersion { get => _singBoxVersion; private set => Set(ref _singBoxVersion, value); }
    public string LinkText { get => _importLink; set => Set(ref _importLink, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
 
    public int ActiveRuleCount => Rules.Count(r => r.Enabled);
 
    // ===================== подключение =====================
 
    private async Task ToggleAsync()
    {
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
 
        // TUN не поднимется без прав администратора. Раньше здесь была просто ошибка —
        // теперь предлагаем повышение, потому что иначе перехватывать трафик нечем.
        if (Settings.TunEnabled && !Elevation.IsElevated && !RequestElevation()) return;
 
        State = TunnelState.Connecting;
        Append("[cvpn] сборка конфигурации");
 
        string configPath;
        try
        {
            configPath = ConfigBuilder.Write(Active, Rules, Settings);
        }
        catch (Exception ex)
        {
            Fail($"не удалось собрать конфигурацию: {ex.Message}");
            return;
        }
 
        ExplainRouting();
 
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
 
        _connectedAt = DateTime.Now;
        _uptimeTimer.Start();
        State = TunnelState.Connected;
        Status = "";
        Append($"[cvpn] подключение к {Active.Name} ({Active.ProtocolLabel})");
 
        // Первый замер с задержкой: ядру нужно поднять Clash API и сам туннель
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            await MeasureAsync();
        });
    }
 
    private async Task DisconnectAsync()
    {
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
        _sessionTun = null;
        RaiseMode();
 
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
            var path = ConfigBuilder.Write(Active, Rules, Settings);
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
        var answer = MessageBox.Show(
            "Режим TUN перехватывает весь системный трафик и требует прав администратора.\n\n" +
            "Перезапустить CVPN с повышением прав?\n\n" +
            "Если отказаться, можно выключить TUN в настройках — тогда трафик пойдёт " +
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
 
    private void OnCoreExited(int exitCode, bool graceful) => Dispatch(() =>
    {
        if (graceful) return;
 
        SystemProxy.Restore();
        _uptimeTimer.Stop();
        State = TunnelState.Failing;
        Status = $"Ядро завершилось с кодом {exitCode}. Подробности в логах.";
        Append($"[cvpn] ядро остановлено, код {exitCode}");
    });
 
    /// <summary>Задержку меряет само ядро через Clash API — свой пинг мимо туннеля бессмыслен.</summary>
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
 
    private void OnRulePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RouteRule.Enabled)) Dispatch(() => Raise(nameof(ActiveRuleCount)));
    }
 
    private void OnTraffic(long up, long down) => Dispatch(() =>
    {
        (Upload, UploadUnit) = FormatRate(up);
        (Download, DownloadUnit) = FormatRate(down);
    });
 
    /// <summary>Килобайты до мегабайта, дальше мегабайты — иначе на медленном канале одни нули.</summary>
    private static (string Value, string Unit) FormatRate(long bytesPerSecond)
    {
        const double kb = 1024;
        const double mb = kb * 1024;
 
        if (bytesPerSecond >= mb)
            return ((bytesPerSecond / mb).ToString("0.0"), "МБ/с");
 
        return ((bytesPerSecond / kb).ToString("0.0"), "КБ/с");
    }
 
    // ===================== профили и правила =====================
 
    private void Import()
    {
        if (!LinkParser.TryParse(LinkText, out var profile, out var error))
        {
            Status = error;
            return;
        }
 
        Profiles.Add(profile);
        Active ??= profile;
        LinkText = "";
        Status = $"Профиль «{profile.Name}» добавлен";
        Persist();
    }
 
    private void DeleteProfile(ServerProfile profile)
    {
        Profiles.Remove(profile);
        if (ReferenceEquals(Active, profile)) Active = Profiles.FirstOrDefault();
        Persist();
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
        _state.Rules = [.. Rules];
        _state.Settings = Settings;
        ProfileStore.Save(_state);
    }
 
    /// <summary>Импорт из файла: конфиг sing-box, массив outbound'ов или один outbound.</summary>
    private void ImportFromFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите конфигурацию sing-box",
            Filter = "Конфигурации (*.json)|*.json|Все файлы (*.*)|*.*",
            CheckFileExists = true
        };
 
        if (dialog.ShowDialog() != true) return;
 
        if (!ConfigImporter.TryImportFile(dialog.FileName, out var imported, out var error))
        {
            Status = error;
            return;
        }
 
        foreach (var profile in imported) Profiles.Add(profile);
 
        Active ??= Profiles.FirstOrDefault();
        Status = imported.Count == 1
            ? $"Профиль «{imported[0].Name}» добавлен"
            : $"Добавлено профилей: {imported.Count}";
        Persist();
    }
 
    /// <summary>Открывает сгенерированный config.json — удобно сверить с рабочим вручную.</summary>
    private void ShowGeneratedConfig()
    {
        try
        {
            if (Active is not null) ConfigBuilder.Write(Active, Rules, Settings);
 
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
 
    /// <summary>Ручное создание и правка профиля через отдельное окно.</summary>
    private void EditProfile(ServerProfile? existing)
    {
        var editor = new Views.ProfileEditorWindow(existing)
        {
            Owner = Application.Current?.MainWindow
        };
 
        if (editor.ShowDialog() != true) return;
 
        var result = editor.Result;
 
        if (existing is null)
        {
            Profiles.Add(result);
            Active ??= result;
            Status = $"Профиль «{result.Name}» создан";
        }
        else
        {
            // Заменяем на месте, чтобы не терять позицию в списке
            var index = Profiles.IndexOf(existing);
            if (index >= 0) Profiles[index] = result;
 
            if (ReferenceEquals(Active, existing)) Active = result;
            Status = $"Профиль «{result.Name}» обновлён";
        }
 
        Persist();
    }
 
    private void PickCore()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Укажите sing-box.exe",
            Filter = "sing-box (sing-box.exe)|sing-box.exe|Исполняемые файлы (*.exe)|*.exe",
            CheckFileExists = true,
            InitialDirectory = Directory.Exists(Path.GetDirectoryName(Settings.CorePath) ?? "")
                ? Path.GetDirectoryName(Settings.CorePath)!
                : AppContext.BaseDirectory
        };
 
        if (dialog.ShowDialog() != true) return;
 
        Settings.CorePath = dialog.FileName;
        Persist();
        // Реестр мог разойтись с настройкой, если приложение перенесли
        AutoStart.Sync(Settings.AutoStart);
        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.AutoStart)) AutoStart.Apply(Settings.AutoStart);
        };
 
        _ = DetectCoreAsync();
        ReportMissingFlags();
        Status = "";
    }
 
    /// <summary>
    /// Буфер обмена — общесистемный ресурс: пока его держит другой процесс,
    /// запись падает с COMException. Перегрузки с повторами у WPF нет
    /// (она только в System.Windows.Forms.Clipboard), поэтому цикл здесь свой.
    /// </summary>
    private void CopyLogToClipboard()
    {
        const int attempts = 5;
        var text = string.Join(Environment.NewLine, Log);
 
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, true);
                Status = $"Скопировано строк: {Log.Count}";
                return;
            }
            catch (Exception ex) when (attempt < attempts)
            {
                _ = ex;
                Thread.Sleep(100);
            }
            catch (Exception ex)
            {
                Status = $"Не удалось скопировать: {ex.Message}";
            }
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
    /// Пишет в лог, как разошлись правила. Помогает понять, почему сайт всё ещё
    /// идёт через прокси: чаще всего домена просто нет в наборе.
    /// </summary>
    private void ExplainRouting()
    {
        var active = Rules.Where(r => r.Enabled).ToList();
        var direct = active.Count(r => r.Action == RouteAction.Direct);
        var blocked = active.Count(r => r.Action == RouteAction.Block);
 
        Append($"[cvpn] правил: {active.Count} (напрямую {direct}, блок {blocked}), " +
               $"остальное {(Settings.ProxyByDefault ? "через прокси" : "напрямую")}");
 
        // geoip сопоставляется по адресу, а на момент DNS-запроса его ещё нет
        var geoipDirect = active
            .Where(r => r.Action == RouteAction.Direct && r.Match == MatchKind.Geoip)
            .Select(r => r.Value)
            .ToList();
 
        if (geoipDirect.Count > 0)
        {
            Append($"[cvpn] geoip ({string.Join(", ", geoipDirect)}) работает только для соединений, " +
                   "но не для DNS: домен резолвится через туннель. Для сайтов с геобалансировкой " +
                   "добавьте правило по домену или geosite.");
        }
    }
 
    /// <summary>
    /// Проверяет все серверы разом. Замер идёт напрямую по TCP, а не через ядро:
    /// так это работает и без подключения, и сразу для всего списка.
    /// </summary>
    private async Task PingAllAsync()
    {
        if (Profiles.Count == 0) return;
 
        IsBusy = true;
        Status = "Проверка серверов…";
 
        try
        {
            var probes = Profiles.Select(async profile =>
            {
                var ms = await LatencyProbe.MeasureAsync(profile.Host, profile.Port);
                Dispatch(() => profile.LatencyMs = ms);
            });
 
            await Task.WhenAll(probes);
 
            var alive = Profiles.Count(p => p.LatencyMs >= 0);
            Status = $"Ответили {alive} из {Profiles.Count}";
            Append($"[cvpn] проверка серверов: {Status.ToLowerInvariant()}");
        }
        finally
        {
            IsBusy = false;
        }
    }
 
    /// <summary>
    /// Обновляет профили из подписки. Заменяются только пришедшие из неё:
    /// созданные вручную остаются на месте.
    /// </summary>
    private async Task UpdateSubscriptionAsync()
    {
        IsBusy = true;
        Status = "Загрузка подписки…";
 
        try
        {
            var (fetched, error) = await SubscriptionService.FetchAsync(Settings.SubscriptionUrl);
 
            if (error.Length > 0)
            {
                Status = error;
                Append($"[cvpn] подписка: {error}");
                return;
            }
 
            var activeName = Active?.Name;
 
            foreach (var stale in Profiles.Where(p => p.Subscription == Settings.SubscriptionUrl).ToList())
                Profiles.Remove(stale);
 
            foreach (var profile in fetched) Profiles.Add(profile);
 
            // Возвращаем выбор на сервер с тем же именем, если он ещё есть
            Active = Profiles.FirstOrDefault(p => p.Name == activeName) ?? Profiles.FirstOrDefault();
 
            Status = $"Из подписки загружено серверов: {fetched.Count}";
            Append($"[cvpn] {Status.ToLowerInvariant()}");
            Persist();
        }
        finally
        {
            IsBusy = false;
        }
    }
 
    /// <summary>Автоподключение при старте — вызывается окном после загрузки.</summary>
    public async Task StartupAsync()
    {
        if (!Settings.AutoConnect || Active is null) return;
 
        Append("[cvpn] автоподключение");
        await ConnectAsync();
    }
 
    /// <summary>Отметка о нажатии на круг — для диагностики привязок.</summary>
    public void NoteDialClick() => Append($"[cvpn] нажатие: состояние {StateLabel}, профиль {Active?.Name ?? "не выбран"}");
 
    // ===================== служебное =====================
 
    private void Fail(string message)
    {
        State = TunnelState.Failing;
        Status = message;
        Append($"[cvpn] {message}");
    }
 
    private void Append(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
 
        Log.Add(line);
        while (Log.Count > MaxLogLines) Log.RemoveAt(0);
    }
 
    /// <summary>Вывод ядра приходит из фонового потока, коллекции WPF этого не прощают.</summary>
    private static void Dispatch(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.Invoke(action);
    }
}
