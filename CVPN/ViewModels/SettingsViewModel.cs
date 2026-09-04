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
        get;
        private set => Set(ref field, value);
    } = Loc.T("Settings_Checking");

    /// <summary>Состояние задачи планировщика.</summary>
    public string TaskStatus
    {
        get;
        private set => Set(ref field, value);
    } = "";

    /// <summary>Найденное обновление; null - установлена последняя версия.</summary>
    public ReleaseInfo? Update
    {
        get;
        private set
        {
            Set(ref field, value);
            Raise(nameof(HasUpdate));
            Raise(nameof(UpdateLabel));
        }
    }

    public bool HasUpdate => Update is not null;

    public string UpdateLabel => Update is null
        ? Loc.T("Settings_VersionInstalled", UpdateChecker.CurrentVersion.ToString(3))
        : Loc.T("Settings_VersionAvailable", Update.Version);

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
            Title = Loc.T("Settings_PickCore"),
            Filter = Loc.T("Settings_CoreFilter"),
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
            ? Loc.T("Settings_RestoreFailed", problem)
            : Loc.T("Settings_RestoreDone"));
    }

    // ===================== обновления =====================

    private async Task CheckUpdateAsync(bool manual)
    {
        var release = await UpdateChecker.CheckAsync();

        MainViewModel.Dispatch(() =>
        {
            Update = release;

            if (release is not null)
                Shell.Notify(Loc.T("Settings_UpdateFound", release.Version));

            // При автоматической проверке молчим: сообщать «обновлений нет»
            // на каждом запуске - лишний шум
            else if (manual) Shell.Notify(Loc.T("Settings_UpToDate"));
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
            Shell.Notify(Loc.T("Settings_OpenPageFailed", ex.Message));
        }
    }

    // ===================== служба =====================

    private async Task RefreshServiceStatusAsync()
    {
        ServiceStatus = await ServiceClient.IsAvailableAsync()
            ? Loc.T("Settings_ServiceOk")
            : ServiceInstaller.IsInstalledOnDisk
                ? Loc.T("Settings_ServiceMissing")
                : Loc.T("Settings_ServiceFilesMissing", ServiceInstaller.ExecutablePath);
    }

    private void InstallTunnelService()
    {
        Shell.Notify(ServiceInstaller.Install(out var error)
            ? Loc.T("Settings_ServiceInstalled")
            : Loc.T("Settings_ServiceInstallFailed", error));

        _ = RefreshServiceStatusAsync();
    }

    private void UninstallTunnelService()
    {
        Shell.Notify(ServiceInstaller.Uninstall(out var error)
            ? Loc.T("Settings_ServiceRemoved")
            : Loc.T("Settings_ServiceRemoveFailed", error));

        _ = RefreshServiceStatusAsync();
    }

    // ===================== задача планировщика =====================

    private void RefreshTaskStatus()
    {
        if (!ElevatedTask.Exists)
        {
            TaskStatus = Loc.T("Settings_TaskMissing");
            return;
        }

        TaskStatus = ElevatedTask.PathMatchesCurrent()
            ? Loc.T("Settings_TaskOk")
            : Loc.T("Settings_TaskStale");
    }

    private void InstallElevatedTask()
    {
        Shell.Notify(ElevatedTask.Install(Settings.AutoStart, out var error)
            ? Loc.T("Settings_TaskCreated")
            : Loc.T("Settings_TaskCreateFailed", error));

        RefreshTaskStatus();
    }

    private void UninstallElevatedTask()
    {
        Shell.Notify(ElevatedTask.Uninstall(out var error)
            ? Loc.T("Settings_TaskDeleted")
            : Loc.T("Settings_TaskDeleteFailed", error));

        RefreshTaskStatus();
    }
}