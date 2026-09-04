using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using CVPN.Localization;

namespace CVPN.Services;

/// <summary>
/// Повышение прав через планировщик задач - упрощённая альтернатива службе.
///
/// Задача создаётся один раз с уровнем «наивысшие права». После этого запуск
/// через schtasks /run стартует приложение уже с правами администратора,
/// и окно UAC больше не появляется.
///
/// Что это НЕ решает, в отличие от службы:
///  • приложение целиком работает с правами администратора, а не только туннель;
///  • туннель живёт в пользовательском сеансе и падает при выходе из системы;
///  • задача срабатывает после входа пользователя, а не до него.
///
/// И одно предупреждение: exe должен лежать в каталоге, недоступном на запись
/// обычному пользователю. Иначе любой сможет подменить файл, который
/// планировщик запускает с повышенными правами.
/// </summary>
public static class ElevatedTask
{
    public const string TaskName = "CVPN Tunnel";

    public static bool Exists => Query();

    /// <summary>
    /// Задача описывается XML, а не ключами командной строки: у schtasks
    /// капризный разбор кавычек в /tr, и путь с пробелами ломает команду.
    /// </summary>
    public static bool Install(bool runAtLogon, out string error)
    {
        var exe = Environment.ProcessPath;

        if (string.IsNullOrEmpty(exe))
        {
            error = Loc.T("Error_NoAppPath");
            return false;
        }

        var xmlPath = Path.Combine(Path.GetTempPath(), "cvpn-task.xml");

        try
        {
            File.WriteAllText(xmlPath, BuildXml(exe, runAtLogon), new UnicodeEncoding(false, true));

            var ok = Run($"/create /tn \"{TaskName}\" /xml \"{xmlPath}\" /f", elevated: true, out error);

            return ok;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            try
            {
                File.Delete(xmlPath);
            }
            catch
            {
                /* временный файл, не критично */
            }
        }
    }

    public static bool Uninstall(out string error) =>
        Run($"/delete /tn \"{TaskName}\" /f", elevated: true, out error);

    /// <summary>
    /// Запускает приложение через задачу - уже с правами администратора и без UAC.
    /// Текущий экземпляр после этого следует закрыть.
    /// </summary>
    public static bool Launch(out string error) =>
        Run($"/run /tn \"{TaskName}\"", elevated: false, out error);

    /// <summary>
    /// Путь, который прописан в задаче. Задача хранит его с момента создания,
    /// поэтому после переустановки или переноса приложения она продолжит
    /// запускать старый файл - и правки в коде не будут видны.
    /// </summary>
    public static string? RegisteredPath()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/query /tn \"{TaskName}\" /xml",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.Unicode
            });

            if (process is null) return null;

            var xml = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            var open = xml.IndexOf("<Command>", StringComparison.OrdinalIgnoreCase);
            var close = xml.IndexOf("</Command>", StringComparison.OrdinalIgnoreCase);

            if (open < 0 || close <= open) return null;

            return xml[(open + "<Command>".Length)..close].Trim().Trim('"');
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Совпадает ли путь в задаче с текущим исполняемым файлом.</summary>
    public static bool PathMatchesCurrent()
    {
        var registered = RegisteredPath();
        var current = Environment.ProcessPath;

        if (registered is null || current is null) return true;

        return string.Equals(
            Path.GetFullPath(registered), Path.GetFullPath(current), StringComparison.OrdinalIgnoreCase);
    }

    private static bool Query()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/query /tn \"{TaskName}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (process is null) return false;

            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool Run(string arguments, bool elevated, out string error)
    {
        error = "";

        try
        {
            var info = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            if (elevated)
            {
                // Создание и удаление задачи требуют прав администратора,
                // а запрос UAC умеет показывать только ShellExecute
                info.UseShellExecute = true;
                info.Verb = "runas";
            }
            else
            {
                info.UseShellExecute = false;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
            }

            using var process = Process.Start(info);

            if (process is null)
            {
                error = Loc.T("Error_SchtasksStart");
                return false;
            }

            process.WaitForExit(15000);

            if (process.ExitCode == 0) return true;

            error = Loc.T("Error_SchtasksCode", process.ExitCode);
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            error = Loc.T("Error_UacCancelled");
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string BuildXml(string exe, bool runAtLogon)
    {
        var user = WindowsIdentity.GetCurrent().Name;

        var trigger = runAtLogon
            ? $"""
                 <LogonTrigger>
                   <Enabled>true</Enabled>
                   <UserId>{Escape(user)}</UserId>
                 </LogonTrigger>
               """
            : "";

        var arguments = runAtLogon ? $"<Arguments>{AutoStart.MinimizedArgument}</Arguments>" : "";

        return $"""
                <?xml version="1.0" encoding="UTF-16"?>
                <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
                  <RegistrationInfo>
                    <Description>Запуск CVPN с правами, необходимыми для режима TUN</Description>
                  </RegistrationInfo>
                  <Triggers>
                {trigger}
                  </Triggers>
                  <Principals>
                    <Principal id="Author">
                      <UserId>{Escape(user)}</UserId>
                      <LogonType>InteractiveToken</LogonType>
                      <RunLevel>HighestAvailable</RunLevel>
                    </Principal>
                  </Principals>
                  <Settings>
                    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                    <AllowHardTerminate>false</AllowHardTerminate>
                    <StartWhenAvailable>true</StartWhenAvailable>
                    <IdleSettings>
                      <StopOnIdleEnd>false</StopOnIdleEnd>
                      <RestartOnIdle>false</RestartOnIdle>
                    </IdleSettings>
                    <AllowStartOnDemand>true</AllowStartOnDemand>
                    <Enabled>true</Enabled>
                    <Hidden>false</Hidden>
                    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                    <Priority>7</Priority>
                  </Settings>
                  <Actions Context="Author">
                    <Exec>
                      <Command>{Escape(exe)}</Command>
                      {arguments}
                    </Exec>
                  </Actions>
                </Task>
                """;
    }

    private static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}