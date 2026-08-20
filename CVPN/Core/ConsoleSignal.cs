using System.Runtime.InteropServices;

namespace CVPN.Core;

/// <summary>
/// Отправка Ctrl+Break дочернему процессу.
///
/// Нужно потому, что sing-box снимает TUN-интерфейс только при штатном
/// завершении. Если процесс убить, адаптер остаётся в системе, и следующий
/// запуск падает с «create adapter: file already exists».
/// </summary>
public static class ConsoleSignal
{
    private const uint CtrlBreakEvent = 1;
 
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);
 
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();
 
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);
 
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GenerateConsoleCtrlEvent(uint ctrlEvent, uint processGroupId);
 
    /// <summary>
    /// Присоединяется к консоли процесса и шлёт Ctrl+Break. Собственный обработчик
    /// на это время отключается — иначе сигнал прилетит и нам самим.
    /// </summary>
    public static bool TryBreak(int processId)
    {
        if (!AttachConsole((uint)processId)) return false;
 
        try
        {
            SetConsoleCtrlHandler(IntPtr.Zero, true);
            return GenerateConsoleCtrlEvent(CtrlBreakEvent, 0);
        }
        finally
        {
            FreeConsole();
            SetConsoleCtrlHandler(IntPtr.Zero, false);
        }
    }
}