using System.Diagnostics;
using System.Windows;
using System.IO;
using System.Windows.Input;
using Microsoft.Win32;
using CVPN.Core;
using CVPN.Ipc;
using CVPN.Localization;
using CVPN.Services;


namespace CVPN.ViewModels;

/// <summary>
/// Страница настроек: путь к ядру, служба, задача планировщика, kill switch
/// и обновления.
///
/// Сами настройки живут в оболочке - их читает сборка конфигурации. Здесь
/// действия над окружением: установить, удалить, проверить, восстановить.
/// </summary>
public sealed class SettingsViewModel : PageViewModel
{
    private string _serviceStatus = "проверка…";
    private string _taskStatus = "";
    private ReleaseInfo? _update;

    public SettingsViewModel(MainViewModel shell) : base(shell)
    {
        BrowseCore = new RelayCommand(PickCore);
        RestoreNetwork = new RelayCommand(async () => await RestoreNetworkAsync());

        CheckUpdate = new RelayCommand(async () => await CheckUpdateAsync(manual: true));
        OpenRelease = new RelayCommand(OpenReleasePage, () => Update is not null);

        InstallService = new RelayCommand(InstallTunnelService);
        UninstallService = new RelayCommand(UninstallTunnelService);
        InstallTask = new RelayCommand(InstallElevatedTask);
        UninstallTask = new RelayCommand(UninstallElevatedTask);

        // Настройки сохранялись только при штатном выходе: если приложение
        // снять из диспетчера, изменения пропадали
        Settings.PropertyChanged += OnSettingChanged;
    }

    private void OnSettingChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        Shell.Persist();

        // Язык читается при запуске, поэтому спрашиваем прямо
        if (e.PropertyName == nameof(AppSettings.Language)) OfferRestart();
    }

    public AppSettings Settings => Shell.Settings;

    /// <summary>Языки интерфейса для выпадающего списка.</summary>
    public IReadOnlyList<LanguageOption> Languages => Loc.Available;

    public ICommand BrowseCore { get; }
    public ICommand RestoreNetwork { get; }
    public ICommand CheckUpdate { get; }
    public ICommand OpenRelease { get; }
    public ICommand InstallService { get; }
    public ICommand UninstallService { get; }
    public ICommand InstallTask { get; }
    public ICommand UninstallTask { get; }

    /// <summary>Состояние службы для страницы настроек.</summary>
    public string ServiceStatus
    {
        get => _serviceStatus;
        private set => Set(ref _serviceStatus, value);
    }

    /// <summary>Состояние задачи планировщика.</summary>
    public string TaskStatus
    {
        get => _taskStatus;
        private set => Set(ref _taskStatus, value);
    }

    /// <summary>Найденное обновление; null - установлена последняя версия.</summary>
    public ReleaseInfo? Update
    {
        get => _update;
        private set
        {
            Set(ref _update, value);
            Raise(nameof(HasUpdate));
            Raise(nameof(UpdateLabel));
        }
    }

    public bool HasUpdate => Update is not null;

    public string UpdateLabel => Update is null
        ? $"установлена версия {UpdateChecker.CurrentVersion.ToString(3)}"
        : $"доступна версия {Update.Version}";

    /// <summary>Состояние окружения могло измениться, пока страница была закрыта.</summary>
    public override void Activate()
    {
        _ = RefreshServiceStatusAsync();
        RefreshTaskStatus();
    }

    /// <summary>
    /// Автоматическая проверка при запуске. Вызывается оболочкой с задержкой,
    /// чтобы не соперничать со стартом за сеть и внимание.
    /// </summary>
    public async Task StartupCheckAsync()
    {
        if (!Settings.CheckUpdates) return;

        await Task.Delay(TimeSpan.FromSeconds(5));
        await CheckUpdateAsync(manual: false);
    }

    /// <summary>
    /// Язык применяется при разборе разметки, то есть только при запуске.
    /// Молчаливое «ничего не изменилось» выглядит как поломка, поэтому
    /// предлагаем перезапуск сразу.
    /// </summary>
    private void OfferRestart()
    {
        Shell.Persist();

        var answer = MessageBox.Show(
            Loc.T("Settings_RestartQuestion"),
            "CVPN", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
        {
            Shell.Notify(Loc.T("Settings_LanguageHint"));
            return;
        }

        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;

        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            Application.Current?.Shutdown();
        }
        catch (Exception ex)
        {
            Shell.Notify(Loc.T("Settings_RestartFailed", ex.Message));
        }
    }

    // ===================== ядро =====================

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
        Shell.Persist();
        Shell.RefreshCoreVersion();
    }

    // ===================== сеть =====================

    /// <summary>
    /// Аварийная кнопка: снимает правила брандмауэра, если что-то пошло не так
    /// и интернет пропал вместе с туннелем.
    /// </summary>
    private async Task RestoreNetworkAsync()
    {
        var problem = await KillSwitch.DisableAsync();

        Shell.Notify(problem.Length > 0
            ? $"Не удалось снять правила: {problem}"
            : "Правила брандмауэра сняты, сеть восстановлена");
    }

    // ===================== обновления =====================

    private async Task CheckUpdateAsync(bool manual)
    {
        var release = await UpdateChecker.CheckAsync();

        MainViewModel.Dispatch(() =>
        {
            Update = release;

            if (release is not null)
                Shell.Notify($"Доступна версия {release.Version} - откройте страницу релиза");

            // При автоматической проверке молчим: сообщать «обновлений нет»
            // на каждом запуске - лишний шум
            else if (manual) Shell.Notify("Установлена последняя версия");
        });
    }

    private void OpenReleasePage()
    {
        if (Update is null) return;

        try
        {
            Process.Start(new ProcessStartInfo(Update.Url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Shell.Notify($"Не удалось открыть страницу: {ex.Message}");
        }
    }

    // ===================== служба =====================

    private async Task RefreshServiceStatusAsync()
    {
        ServiceStatus = await ServiceClient.IsAvailableAsync()
            ? "служба установлена и отвечает"
            : ServiceInstaller.IsInstalledOnDisk
                ? "служба не установлена - нажмите «Установить службу»"
                : $"файлы службы не найдены: {ServiceInstaller.ExecutablePath}";
    }

    private void InstallTunnelService()
    {
        Shell.Notify(ServiceInstaller.Install(out var error)
            ? "Служба установлена"
            : $"Не удалось установить службу: {error}");

        _ = RefreshServiceStatusAsync();
    }

    private void UninstallTunnelService()
    {
        Shell.Notify(ServiceInstaller.Uninstall(out var error)
            ? "Служба удалена"
            : $"Не удалось удалить службу: {error}");

        _ = RefreshServiceStatusAsync();
    }

    // ===================== задача планировщика =====================

    private void RefreshTaskStatus()
    {
        if (!ElevatedTask.Exists)
        {
            TaskStatus = "задача не создана - при включении TUN появится окно UAC";
            return;
        }

        TaskStatus = ElevatedTask.PathMatchesCurrent()
            ? "задача создана - права выдаются без запроса"
            : "задача указывает на другой файл - пересоздайте её";
    }

    private void InstallElevatedTask()
    {
        Shell.Notify(ElevatedTask.Install(Settings.AutoStart, out var error)
            ? "Задача планировщика создана"
            : $"Не удалось создать задачу: {error}");

        RefreshTaskStatus();
    }

    private void UninstallElevatedTask()
    {
        Shell.Notify(ElevatedTask.Uninstall(out var error)
            ? "Задача планировщика удалена"
            : $"Не удалось удалить задачу: {error}");

        RefreshTaskStatus();
    }
}