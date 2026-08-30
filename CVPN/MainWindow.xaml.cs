using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
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
        RefreshTray();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.State) or nameof(MainViewModel.Active)) RefreshTray();
    }

    private void RefreshTray()
    {
        if (Vm is not null) _tray.Update(Vm.State, Vm.Active?.Name);
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
                _tray.Notify("CVPN работает", "Приложение свёрнуто в трей. Выход - через меню значка.");

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
            "profiles" => new ProfilesView(),
            "routing" => new RoutingView(),
            "connections" => Vm?.ConnectionsPage,
            "logs" => new LogsView(),
            "settings" => new SettingsView(),
            _ => new ConnectionView()
        };
    }
}