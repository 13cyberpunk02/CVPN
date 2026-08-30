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

        FileLog.Initialize(System.IO.Path.Combine(AppPaths.DataDir, "logs"));

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        FileLog.Current.Write($"=== запуск CVPN {version} · {Environment.ProcessPath}");

        // Если прошлый запуск завершился аварийно с включённым kill switch,
        // система осталась без интернета. Чиним до всего остального.
        _ = RestoreNetworkAsync();

        // Исключение в UI-потоке: приложение можно оставить живым
        DispatcherUnhandledException += OnDispatcherException;

        // Исключение в фоновом потоке: процесс уже не спасти, но записать успеем
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Record("Необработанное исключение", args.ExceptionObject as Exception);

        // Задача упала, и её результат никто не проверил
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Record("Исключение в фоновой задаче", args.Exception);
            args.SetObserved();
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        FileLog.Current.Write($"=== выход, код {e.ApplicationExitCode}");
        base.OnExit(e);
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Record("Ошибка в интерфейсе", e.Exception);

        var answer = MessageBox.Show(
            $"Произошла ошибка:\n\n{e.Exception.Message}\n\n" +
            $"Подробности записаны в {FileLog.Current.CurrentFile}\n\n" +
            "Продолжить работу? При отказе приложение закроется.",
            "CVPN", MessageBoxButton.YesNo, MessageBoxImage.Error);

        // Продолжать после ошибки рискованно, но принудительное закрытие
        // с открытым туннелем хуже: пользователь останется без интернета
        e.Handled = answer == MessageBoxResult.Yes;

        if (!e.Handled) Shutdown(1);
    }

    private static async Task RestoreNetworkAsync()
    {
        if (!KillSwitch.IsActive) return;

        FileLog.Current.Write("[cvpn] обнаружены правила kill switch от прошлого запуска, снимаем");

        var problem = await KillSwitch.DisableAsync();

        FileLog.Current.Write(problem.Length > 0
            ? $"[cvpn] снять правила не удалось: {problem}"
            : "[cvpn] правила сняты, сеть восстановлена");
    }

    private static void Record(string title, Exception? exception)
    {
        if (exception is null) return;

        FileLog.Current.WriteBlock(title, exception.ToString());
    }
}