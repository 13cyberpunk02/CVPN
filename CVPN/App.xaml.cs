using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using CVPN.Services;
using CVPN.Shared;

namespace CVPN;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Язык применяется до создания окна: разметка читает переводы при разборе
        Localization.Loc.Apply(ProfileStore.Load().Settings.Language);

        FileLog.Initialize(System.IO.Path.Combine(AppPaths.DataDir, "logs"));

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        FileLog.Current.Write($"=== CVPN {version} started · {Environment.ProcessPath}");

        // Если прошлый запуск завершился аварийно с включённым kill switch,
        // система осталась без интернета. Чиним до всего остального.
        _ = RestoreNetworkAsync();

        // Исключение в UI-потоке: приложение можно оставить живым
        DispatcherUnhandledException += OnDispatcherException;

        // Исключение в фоновом потоке: процесс уже не спасти, но записать успеем
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Record("Unhandled exception", args.ExceptionObject as Exception);

        // Задача упала, и её результат никто не проверил
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Record("Exception in a background task", args.Exception);
            args.SetObserved();
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        FileLog.Current.Write($"=== exit, code {e.ApplicationExitCode}");
        base.OnExit(e);
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Record("UI exception", e.Exception);

        var answer = MessageBox.Show(
            Localization.Loc.T("Crash_Message", e.Exception.Message, FileLog.Current.CurrentFile),
            "CVPN", MessageBoxButton.YesNo, MessageBoxImage.Error);

        // Продолжать после ошибки рискованно, но принудительное закрытие
        // с открытым туннелем хуже: пользователь останется без интернета
        e.Handled = answer == MessageBoxResult.Yes;

        if (!e.Handled) Shutdown(1);
    }

    private static async Task RestoreNetworkAsync()
    {
        if (!KillSwitch.IsActive) return;

        FileLog.Current.Write("[cvpn] kill switch rules left from a previous run, removing them");

        var problem = await KillSwitch.DisableAsync();

        FileLog.Current.Write(problem.Length > 0
            ? $"[cvpn] could not remove the rules: {problem}"
            : "[cvpn] rules removed, network restored");
    }

    private static void Record(string title, Exception? exception)
    {
        if (exception is null) return;

        FileLog.Current.WriteBlock(title, exception.ToString());
    }
}