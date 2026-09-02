using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Forms;
using System.Windows.Input;
using CVPN.Core;
using CVPN.Localization;
using CVPN.Shared;

namespace CVPN.ViewModels;

/// <summary>
/// Страница логов. Сама коллекция живёт в оболочке - в неё пишет ядро,
/// и она нужна независимо от того, открыта ли страница. Здесь остаются
/// только действия над ней.
/// </summary>
public sealed class LogsViewModel : PageViewModel
{
    public LogsViewModel(MainViewModel shell) : base(shell)
    {
        Clear = new RelayCommand(shell.Log.Clear, () => shell.Log.Count > 0);
        Copy = new RelayCommand(CopyToClipboard, () => shell.Log.Count > 0);
        OpenFolder = new RelayCommand(ShowFolder);
    }

    public ObservableCollection<string> Log => Shell.Log;

    public ICommand Clear { get; }
    public ICommand Copy { get; }
    public ICommand OpenFolder { get; }

    /// <summary>
    /// Буфер обмена - общесистемный ресурс: пока его держит другой процесс,
    /// запись падает с COMException. Перегрузки с повторами у WPF нет
    /// (она только в System.Windows.Forms.Clipboard), поэтому цикл здесь свой.
    /// </summary>
    private void CopyToClipboard()
    {
        const int attempts = 5;
        var text = string.Join(Environment.NewLine, Log);

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, true);
                Shell.Notify(Loc.T("Logs_CopiedLines", Log.Count));
                return;
            }
            catch (Exception) when (attempt < attempts)
            {
                Thread.Sleep(100);
            }
            catch (Exception ex)
            {
                Shell.Notify(Loc.T("Logs_CopyFailed", ex.Message));
            }
        }
    }

    /// <summary>Открывает папку с логами - то, что просят приложить к issue.</summary>
    private void ShowFolder()
    {
        try
        {
            FileLog.Current.Prepare();

            Process.Start(new ProcessStartInfo(FileLog.Current.Directory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Shell.Notify(Loc.T("Logs_FolderFailed", ex.Message));
        }
    }
}