using System.Windows.Controls;
using CVPN.ViewModels;

namespace CVPN.Views;

public partial class ProfilesView : UserControl
{
    public ProfilesView()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is MainViewModel vm) await vm.EnsureLatencyAsync();
        };
    }
}