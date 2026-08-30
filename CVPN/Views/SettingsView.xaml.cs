using System.Windows.Controls;
using CVPN.ViewModels;

namespace CVPN.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        
        Loaded += (_, _) => (DataContext as PageViewModel)?.Activate();
        Unloaded += (_, _) => (DataContext as PageViewModel)?.Deactivate();
    }
}