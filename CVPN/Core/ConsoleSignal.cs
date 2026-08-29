using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CVPN.Core;

/// <summary>
/// Штатная остановка дочернего консольного процесса.
///
/// Нужна потому, что sing-box снимает TUN-интерфейс только при корректном
/// завершении. Если процесс убить, адаптер остаётся в системе, и следующий
/// запуск падает с «create adapter: file already exists».
/// </summary>
public static class ConsoleSignal
{
    // Именно CTRL_C, а не CTRL_BREAK. GenerateConsoleCtrlEvent с нулевой группой
    // шлёт сигнал всем процессам консоли, включая отправителя, а игнорировать
    // через SetConsoleCtrlHandler(NULL, TRUE) можно только Ctrl+C.
    // С CTRL_BREAK отправитель завершался вместе с ядром.
    private const uint CtrlCEvent = 0;
 
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);
 
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();
 
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);
 
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GenerateConsoleCtrlEvent(uint ctrlEvent, uint processGroupId);
 
    /// <summary>
    /// Просит процесс завершиться и ждёт указанное время. Возвращает false,
    /// если он не отреагировал - тогда вызывающему коду остаётся Kill.
    /// </summary>
    public static bool TryGracefulStop(Process process, TimeSpan timeout)
    {
        if (!AttachConsole((uint)process.Id)) return false;
 
        var handlerDisabled = false;
 
        try
        {
            // Отключаем собственную реакцию на Ctrl+C до отправки сигнала
            handlerDisabled = SetConsoleCtrlHandler(IntPtr.Zero, true);
            if (!handlerDisabled) return false;
 
            if (!GenerateConsoleCtrlEvent(CtrlCEvent, 0)) return false;
 
            // Ждём здесь, а не снаружи: обработчик должен оставаться отключённым,
            // пока сигнал доставляется, иначе отправитель поймает его сам
            return process.WaitForExit((int)timeout.TotalMilliseconds);
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            if (handlerDisabled) SetConsoleCtrlHandler(IntPtr.Zero, false);
            FreeConsole();
        }
    }
}
