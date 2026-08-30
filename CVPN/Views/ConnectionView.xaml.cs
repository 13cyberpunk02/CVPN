using System.Windows.Controls;
using CVPN.ViewModels;

namespace CVPN.Views;

public partial class ConnectionView : UserControl
{
    public ConnectionView() => InitializeComponent();
 
    /// <summary>
    /// Клик по кругу фиксируется в логе до всякой логики: если запись есть,
    /// а состояние не меняется - дело в команде, а не в привязке.
    /// </summary>
    private void OnDialActivated(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.NoteDialClick();
    }

}