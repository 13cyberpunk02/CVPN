using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CVPN.Models.Enums;

namespace CVPN.Controls;

public partial class ConnectDial : UserControl
{
    public ConnectDial() => InitializeComponent();
 
    /// <summary>Сработавший клик виден даже когда команда ничего не делает.</summary>
    public event EventHandler? Activated;
 
    private void OnCoreClick(object sender, RoutedEventArgs e)
    {
        Activated?.Invoke(this, EventArgs.Empty);
 
        var command = Command;
        if (command is null) return;
        if (!command.CanExecute(null)) return;
 
        command.Execute(null);
    }
 
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(TunnelState), typeof(ConnectDial),
        new PropertyMetadata(TunnelState.Disconnected));
 
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(ConnectDial), new PropertyMetadata("Не выбран"));
 
    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(ConnectDial), new PropertyMetadata(""));
 
    public static readonly DependencyProperty MetricProperty = DependencyProperty.Register(
        nameof(Metric), typeof(string), typeof(ConnectDial), new PropertyMetadata(""));
 
    public static readonly DependencyProperty ActionLabelProperty = DependencyProperty.Register(
        nameof(ActionLabel), typeof(string), typeof(ConnectDial), new PropertyMetadata("Подключить"));
 
    /// <summary>Двухбуквенный код страны. Пустая строка прячет значок.</summary>
    public static readonly DependencyProperty CountryProperty = DependencyProperty.Register(
        nameof(Country), typeof(string), typeof(ConnectDial), new PropertyMetadata(""));
 
    public static readonly DependencyProperty EndpointProperty = DependencyProperty.Register(
        nameof(Endpoint), typeof(string), typeof(ConnectDial), new PropertyMetadata(""));
 
    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command), typeof(ICommand), typeof(ConnectDial), new PropertyMetadata(null));
 
    public TunnelState State
    {
        get => (TunnelState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }
 
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }
 
    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }
 
    public string Metric
    {
        get => (string)GetValue(MetricProperty);
        set => SetValue(MetricProperty, value);
    }
 
    public string ActionLabel
    {
        get => (string)GetValue(ActionLabelProperty);
        set => SetValue(ActionLabelProperty, value);
    }
 
    public string Country
    {
        get => (string)GetValue(CountryProperty);
        set => SetValue(CountryProperty, value);
    }
 
    public string Endpoint
    {
        get => (string)GetValue(EndpointProperty);
        set => SetValue(EndpointProperty, value);
    }
 
    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }
}