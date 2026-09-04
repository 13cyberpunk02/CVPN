using System.Diagnostics;
using System.IO;

namespace CVPN.Services;

/// <summary>
/// Kill switch: пока туннель работает, весь исходящий трафик мимо него запрещён.
/// Без этого обрыв соединения означает, что трафик молча пошёл напрямую,
/// а человек об этом не узнал.
///
/// Опасная функция: неснятые правила оставляют систему без интернета. Поэтому
/// факт включения записывается в файл-отметку, и приложение снимает правила
/// при старте, если в прошлый раз завершилось аварийно.
/// </summary>
public static class KillSwitch
{
    private static string MarkerFile => Path.Combine(AppPaths.DataDir, "killswitch.active");

    /// <summary>Правила сейчас применены - по крайней мере, так считает приложение.</summary>
    public static bool IsActive => File.Exists(MarkerFile);

    /// <summary>
    /// Снимает правила, если они остались от прошлого запуска. Вызывается при
    /// старте приложения до всего остального.
    /// </summary>
    public static async Task RestoreAfterCrashAsync()
    {
        if (!IsActive) return;

        await DisableAsync();
    }

    public static async Task<string> EnableAsync(string corePath)
    {
        if (!Core.Elevation.IsElevated)
            return "Kill switch требует прав администратора";

        var app = Environment.ProcessPath;
        if (string.IsNullOrEmpty(app)) return "Не удалось определить путь к приложению";

        // Отметку ставим ДО применения правил: если процесс упадёт посередине,
        // следующий запуск всё равно узнает, что систему надо чинить
        MarkActive();

        var failed = await RunAsync(KillSwitchCommands.Enable(corePath, app, allowLocalNetwork: true));

        if (failed.Length > 0)
        {
            await DisableAsync();
            return $"Не удалось включить kill switch: {failed}";
        }

        return "";
    }

    public static async Task<string> DisableAsync()
    {
        var failed = await RunAsync(KillSwitchCommands.Disable(), ignoreErrors: true);

        // Отметку снимаем только после успешного возврата политики
        if (failed.Length == 0) ClearMarker();

        return failed;
    }

    private static void MarkActive()
    {
        try
        {
            AppPaths.EnsureCreated();
            File.WriteAllText(MarkerFile, DateTime.Now.ToString("O"));
        }
        catch (Exception)
        {
            // Не смогли записать отметку - работаем без страховки
        }
    }

    private static void ClearMarker()
    {
        try
        {
            if (File.Exists(MarkerFile)) File.Delete(MarkerFile);
        }
        catch (Exception)
        {
            // Останется до следующего запуска, тогда правила снимутся повторно
        }
    }

    /// <summary>
    /// Первая команда - смена политики по умолчанию, и её провал критичен.
    /// Удаление несуществующих правил ошибкой не считается.
    /// </summary>
    private static async Task<string> RunAsync(IReadOnlyList<string> commands, bool ignoreErrors = false)
    {
        for (var i = 0; i < commands.Count; i++)
        {
            var (code, output) = await NetshAsync(commands[i]);

            if (code == 0) continue;
            if (ignoreErrors && i > 0) continue;

            return output.Length > 0 ? output : $"netsh вернул код {code}";
        }

        return "";
    }

    private static async Task<(int Code, string Output)> NetshAsync(string arguments)
    {
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(info);
            if (process is null) return (-1, "не удалось запустить netsh");

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await process.WaitForExitAsync(cts.Token);

            return (process.ExitCode, (error + output).Trim());
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}