using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CVPN.Services;

/// <summary>
/// Системный прокси Windows (WinINET). Нужен, когда TUN выключен: без него
/// mixed-порт просто слушает, а трафик мимо него идёт напрямую.
/// Прав администратора не требует - настройки лежат в ветке текущего пользователя.
/// </summary>
public static class SystemProxy
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private const int InternetOptionSettingsChanged = 39;
    private const int InternetOptionRefresh = 37;
 
    /// <summary>Локальные адреса мимо прокси, иначе ломаются принтеры и внутренние сервисы.</summary>
    private const string Bypass = "localhost;127.*;10.*;172.16.*;172.17.*;172.18.*;172.19.*;192.168.*;<local>";
 
    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr handle, int option, IntPtr buffer, int bufferLength);
 
    private static bool _previousEnabled;
    private static string? _previousServer;
    private static bool _applied;
 
    public static void Enable(int port)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Key, writable: true);
        if (key is null) return;
 
        if (!_applied)
        {
            _previousEnabled = (key.GetValue("ProxyEnable") as int?) == 1;
            _previousServer = key.GetValue("ProxyServer") as string;
            _applied = true;
        }
 
        key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
        key.SetValue("ProxyServer", $"127.0.0.1:{port}", RegistryValueKind.String);
        key.SetValue("ProxyOverride", Bypass, RegistryValueKind.String);
 
        Notify();
    }
 
    /// <summary>Возвращает настройки, которые были до нас. Вызывать обязательно при выходе.</summary>
    public static void Restore()
    {
        if (!_applied) return;
 
        using var key = Registry.CurrentUser.OpenSubKey(Key, writable: true);
        if (key is null) return;
 
        key.SetValue("ProxyEnable", _previousEnabled ? 1 : 0, RegistryValueKind.DWord);
 
        if (_previousServer is null) key.DeleteValue("ProxyServer", throwOnMissingValue: false);
        else key.SetValue("ProxyServer", _previousServer, RegistryValueKind.String);
 
        _applied = false;
        Notify();
    }
 
    /// <summary>Без этого уже запущенные браузеры не заметят смену настроек.</summary>
    private static void Notify()
    {
        InternetSetOption(IntPtr.Zero, InternetOptionSettingsChanged, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, InternetOptionRefresh, IntPtr.Zero, 0);
    }
}
 
