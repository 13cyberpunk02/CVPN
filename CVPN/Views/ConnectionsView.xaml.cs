using System.Windows.Controls;
using CVPN.ViewModels;

namespace CVPN.Views;

public partial class ConnectionsView : UserControl
{
    public ConnectionsView()
    {
        InitializeComponent();
        Loaded += (_, _) => (DataContext as MainViewModel)?.StartWatchingConnections();
        Unloaded += (_, _) => (DataContext as MainViewModel)?.StopWatchingConnections();
    }

}