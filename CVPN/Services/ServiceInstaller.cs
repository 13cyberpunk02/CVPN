using System.Diagnostics;
using System.IO;
using CVPN.Ipc;
using CVPN.Localization;
using CVPN.Shared;

namespace CVPN.Services;

/// <summary>
/// Установка и удаление службы туннеля. Обе операции требуют прав администратора,
/// поэтому sc.exe запускается через ShellExecute с запросом UAC - один раз,
/// а не при каждом старте приложения.
/// </summary>
public static class ServiceInstaller
{
    public static string ExecutablePath =>
        Path.Combine(AppContext.BaseDirectory, "service", "CVPN.Service.exe");

    public static bool IsInstalledOnDisk => File.Exists(ExecutablePath);

    public static bool Install(out string error)
    {
        if (!IsInstalledOnDisk)
        {
            error = Loc.T("Service_FilesMissing", ExecutablePath);
            return false;
        }

        // start=auto - служба поднимется до входа пользователя в систему
        var ok = RunSc($"create {IpcContract.ServiceName} binPath= \"{ExecutablePath}\" " +
                       $"start= auto DisplayName= \"CVPN Tunnel\"", out error);

        if (ok) RunSc($"start {IpcContract.ServiceName}", out _);

        return ok;
    }

    public static bool Uninstall(out string error)
    {
        RunSc($"stop {IpcContract.ServiceName}", out _);
        return RunSc($"delete {IpcContract.ServiceName}", out error);
    }

    private static bool RunSc(string arguments, out string error)
    {
        error = "";

        try
        {
            var info = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = arguments,
                // ShellExecute обязателен: только он показывает запрос UAC
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(info);
            if (process is null)
            {
                error = Loc.T("Error_ScStart");
                return false;
            }

            process.WaitForExit(15000);

            if (process.ExitCode == 0) return true;

            error = Loc.T("Error_ScCode", process.ExitCode);
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
}