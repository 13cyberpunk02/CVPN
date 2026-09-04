using System.Windows;
using System.Windows.Controls;
using CVPN.Localization;
using CVPN.Models.Enums;
using CVPN.ViewModels;
using Microsoft.Win32;

namespace CVPN.Views;

public partial class RoutingView : UserControl
{
    /// <summary>
    /// Подсказка объясняет, что вообще ждут в этом поле: для geoip нужен код страны,
    /// для domain_suffix - домен без точки, для rule_set - ссылка или путь.
    /// </summary>
    private static readonly Dictionary<MatchKind, string> Hints = new()
    {
        [MatchKind.Geosite] = "youtube · twitch · category-ads-all",
        [MatchKind.Geoip] = Loc.T("Hint_Geoip"),
        [MatchKind.Domain] = Loc.T("Hint_Domain"),
        [MatchKind.DomainSuffix] = Loc.T("Hint_Suffix"),
        [MatchKind.DomainKeyword] = Loc.T("Hint_Keyword"),
        [MatchKind.Process] = Loc.T("Hint_Process"),
        [MatchKind.RuleSetRemote] = "https://…/geosite-youtube.srs",
        [MatchKind.RuleSetLocal] = "C:\\rules\\twitch.srs - файл на диске"
    };

    private bool _loaded;

    public RoutingView()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            Vm?.Activate();

            ApplyHint();
            SyncFallback();
            _loaded = true;

            // Смена набора меняет и значение «всё остальное»
            if (Vm is not null) Vm.PropertyChanged += OnVmChanged;
        };

        Unloaded += (_, _) =>
        {
            Vm?.Deactivate();

            if (Vm is not null) Vm.PropertyChanged -= OnVmChanged;
        };
    }

    private RoutingViewModel? Vm => DataContext as RoutingViewModel;

    private MatchKind SelectedMatch => (MatchKind)Math.Max(0, MatchBox.SelectedIndex);

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RoutingViewModel.ActiveRouting)) SyncFallback();
    }

    /// <summary>Список «всё остальное» принадлежит набору, а не приложению.</summary>
    private void SyncFallback()
    {
        if (Vm is null || FinalBox is null) return;

        FinalBox.SelectedIndex = Vm.ActiveRouting.ProxyByDefault ? 0 : 1;
    }

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

        // Копируем набор к себе: иначе правило сломается, стоит файлу переехать
        try
        {
            Services.AppPaths.EnsureCreated();

            var target = System.IO.Path.Combine(
                Services.AppPaths.RulesDir, System.IO.Path.GetFileName(dialog.FileName));

            if (!string.Equals(target, dialog.FileName, StringComparison.OrdinalIgnoreCase))
                System.IO.File.Copy(dialog.FileName, target, overwrite: true);

            ValueBox.Text = target;
        }
        catch (Exception)
        {
            // Не смогли скопировать - работаем по исходному пути
            ValueBox.Text = dialog.FileName;
        }
    }

    private void OnAddRule(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;

        Vm.AddRule(SelectedMatch, ValueBox.Text, (RouteAction)Math.Max(0, ActionBox.SelectedIndex));
        ValueBox.Clear();
    }

    private void OnFinalChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Vm is null || !_loaded) return;

        Vm.SetFallback(FinalBox.SelectedIndex == 0);
    }
}