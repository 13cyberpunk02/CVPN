using System.Windows;
using System.Windows.Controls;
using CVPN.Models.Enums;
using CVPN.ViewModels;
using Microsoft.Win32;

namespace CVPN.Views;

public partial class RoutingView : UserControl
{
      /// <summary>
    /// Подсказка объясняет, что вообще ждут в этом поле: для geoip нужен код страны,
    /// для domain_suffix — домен без точки, для rule_set — ссылка или путь.
    /// </summary>
    private static readonly Dictionary<MatchKind, string> Hints = new()
    {
        [MatchKind.Geosite] = "youtube · twitch · category-ads-all",
        [MatchKind.Geoip] = "ru · de · us — код страны",
        [MatchKind.Domain] = "example.com — точное совпадение",
        [MatchKind.DomainSuffix] = "openai.com — домен и все поддомены",
        [MatchKind.DomainKeyword] = "google — подстрока в имени домена",
        [MatchKind.Process] = "Telegram.exe — имя процесса",
        [MatchKind.RuleSetRemote] = "https://…/geosite-youtube.srs",
        [MatchKind.RuleSetLocal] = "C:\\rules\\twitch.srs — файл на диске"
    };
 
    public RoutingView()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyHint();
    }
 
    private MainViewModel? Vm => DataContext as MainViewModel;
 
    private MatchKind SelectedMatch => (MatchKind)Math.Max(0, MatchBox.SelectedIndex);
 
    private void OnMatchChanged(object sender, SelectionChangedEventArgs e) => ApplyHint();
 
    private void ApplyHint()
    {
        if (ValueBox is null || PickSrsButton is null) return;
 
        ValueBox.Tag = Hints.TryGetValue(SelectedMatch, out var hint) ? hint : "";
 
        // Кнопка обзора нужна только для локального .srs
        PickSrsButton.Visibility = SelectedMatch == MatchKind.RuleSetLocal
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
 
    private void OnPickSrs(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите файл набора правил",
            Filter = "Наборы правил sing-box (*.srs)|*.srs|Все файлы (*.*)|*.*",
            CheckFileExists = true
        };
 
        if (dialog.ShowDialog() != true) return;
 
        ValueBox.Text = dialog.FileName;
    }
 
    private void OnAddRule(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
 
        Vm.AddRule(SelectedMatch, ValueBox.Text, (RouteAction)Math.Max(0, ActionBox.SelectedIndex));
        ValueBox.Clear();
    }
 
    private void OnFinalChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Vm is null) return;
 
        Vm.Settings.ProxyByDefault = FinalBox.SelectedIndex == 0;
        Vm.Persist();
    }
}