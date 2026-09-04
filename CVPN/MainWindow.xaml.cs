using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using CVPN.Localization;
using CVPN.Services;
using CVPN.ViewModels;
using CVPN.Views;

namespace CVPN;

public partial class MainWindow : Window
{
    private readonly TrayIcon _tray = new();
    private bool _exiting;

    public MainWindow()
    {
        InitializeComponent();

        _tray.ShowRequested += RestoreFromTray;
        _tray.ToggleRequested += () => Vm?.ToggleConnection.Execute(null);
        _tray.ProfileRequested += SwitchProfile;
        _tray.ExitRequested += ExitApplication;

        DataContextChanged += (_, _) => Subscribe();

        Loaded += async (_, _) =>
        {
            Subscribe();

            // Запуск из автозагрузки: окно не показываем, только значок в трее
            if (Environment.GetCommandLineArgs().Contains(AutoStart.MinimizedArgument)) Hide();

            if (Vm is not null) await Vm.StartupAsync();
        };
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void Subscribe()
    {
        if (Vm is null) return;

        Vm.PropertyChanged -= OnVmChanged;
        Vm.PropertyChanged += OnVmChanged;

        // Профиль могли добавить, удалить или переименовать - меню должно поспевать
        Vm.Profiles.CollectionChanged -= OnProfilesChanged;
        Vm.Profiles.CollectionChanged += OnProfilesChanged;

        RefreshTray();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.State) or nameof(MainViewModel.Active)) RefreshTray();
    }

    private void OnProfilesChanged(object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => RefreshTray();

    private void RefreshTray()
    {
        if (Vm is null) return;

        _tray.Update(Vm.State, Vm.Active?.Name);
        _tray.SetProfiles(Vm.Profiles.Select(p => (p.Name, ReferenceEquals(p, Vm.Active))));
    }

    /// <summary>
    /// Выбор сервера из меню трея. Ищем по названию: пункт меню хранит строку,
    /// а не ссылку на профиль - список пересоздаётся при каждом изменении.
    /// </summary>
    private void SwitchProfile(string name)
    {
        var profile = Vm?.Profiles.FirstOrDefault(p => p.Name == name);

        if (profile is not null) _ = Vm!.SelectServerAsync(profile);
    }

    // ===================== заголовок окна =====================

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    // ===================== трей =====================

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _exiting = true;
        Close();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        // Свёрнутое окно уходит с панели задач: значок в трее его заменяет
        if (WindowState == WindowState.Minimized) Hide();

        base.OnStateChanged(e);
    }

    /// <summary>
    /// Крестик прячет окно, если так настроено. Настоящее закрытие ждёт
    /// остановки ядра и возврата системного прокси.
    /// </summary>
    protected override async void OnClosing(CancelEventArgs e)
    {
        if (!_exiting && Vm?.Settings.CloseToTray == true)
        {
            e.Cancel = true;
            Hide();

            if (Vm.IsConnected)
                _tray.Notify(Loc.T("Tray_StillRunning"), Loc.T("Tray_StillRunningHint"));

            return;
        }

        if (_exiting && Vm is not null)
        {
            e.Cancel = true;
            _exiting = false;

            await Vm.ShutdownAsync();
            _tray.Dispose();

            Application.Current.Shutdown();
            return;
        }

        _tray.Dispose();
        base.OnClosing(e);
    }

    private void OnNavChanged(object sender, RoutedEventArgs e)
    {
        if (PageHost is null || sender is not RadioButton { Tag: string tag }) return;

        // Страницы, переехавшие на вьюмодели, подставляются как объекты -
        // разметку для них WPF найдёт по DataTemplate. Остальные пока
        // создаются напрямую и своё состояние при переходе теряют.
        PageHost.Content = tag switch
        {
            "profiles" => Vm?.ProfilesPage,
            "routing" => Vm?.RoutingPage,
            "connections" => Vm?.ConnectionsPage,
            "logs" => Vm?.LogsPage,
            "settings" => Vm?.SettingsPage,
            _ => new ConnectionView()
        };
    }
}