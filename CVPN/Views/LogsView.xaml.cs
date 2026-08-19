using System.Collections.Specialized;
using System.Windows.Controls;
using CVPN.ViewModels;

namespace CVPN.Views;

public partial class LogsView : UserControl
{
    private INotifyCollectionChanged? _log;
 
    public LogsView()
    {
        InitializeComponent();
 
        Loaded += (_, _) =>
        {
            if (DataContext is not MainViewModel vm) return;
 
            _log = vm.Log;
            _log.CollectionChanged += OnLogChanged;
            LogScroll.ScrollToEnd();
        };
 
        Unloaded += (_, _) =>
        {
            if (_log is null) return;
 
            _log.CollectionChanged -= OnLogChanged;
            _log = null;
        };
    }
 
    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add) LogScroll.ScrollToEnd();
    }

}