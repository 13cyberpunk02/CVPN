using System.Diagnostics;
using System.Security.Principal;

namespace CVPN.Core;

/// <summary>
/// Повышение прав в рантайме. Манифест намеренно оставлен asInvoker:
/// с requireAdministrator процесс не стартует под `dotnet run` и в невозвышенной IDE,
/// потому что CreateProcess не умеет показывать запрос UAC (ошибка 740).
/// </summary>
public static class Elevation
{
    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
 
    /// <summary>
    /// Перезапускает приложение с правами администратора.
    /// Возвращает false, если пользователь отменил запрос UAC - вызывающий код
    /// в этом случае должен остаться в текущем процессе и объяснить, что TUN недоступен.
    /// </summary>
    public static bool RelaunchElevated(params string[] args)
    {
        if (IsElevated) return true;
 
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return false;
 
        var info = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = AppContext.BaseDirectory
        };
 
        foreach (var a in args) info.ArgumentList.Add(a);
 
        try
        {
            Process.Start(info);
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}