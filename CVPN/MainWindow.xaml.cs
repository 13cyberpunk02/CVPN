using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using CVPN.ViewModels;
using CVPN.Views;

namespace CVPN;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private bool _shuttingDown;
 
    public MainWindow() => InitializeComponent();
 
    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
 
    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
 
    private void OnClose(object sender, RoutedEventArgs e) => Close();
 
    /// <summary>
    /// Закрытие откладывается до остановки ядра и возврата системного прокси:
    /// иначе процесс sing-box переживёт приложение, а в настройках Windows
    /// останется мёртвый прокси на 127.0.0.1.
    /// </summary>
    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_shuttingDown || DataContext is not MainViewModel vm)
        {
            base.OnClosing(e);
            return;
        }
 
        e.Cancel = true;
        _shuttingDown = true;
 
        await vm.ShutdownAsync();
 
        base.OnClosing(e);
        Close();
    }
 
    private void OnNavChanged(object sender, RoutedEventArgs e)
    {
        if (PageHost is null || sender is not RadioButton { Tag: string tag }) return;
 
        PageHost.Content = tag switch
        {
            "profiles" => new ProfilesView(),
            "routing" => new RoutingView(),
            "logs" => new LogsView(),
            "settings" => new SettingsView(),
            _ => new ConnectionView()
        };
    }
}