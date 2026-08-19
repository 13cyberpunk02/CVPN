using Microsoft.Win32;

namespace CVPN.Services;

/// <summary>
/// Автозапуск через раздел Run текущего пользователя. Планировщик задач умел бы
/// стартовать сразу с правами администратора, но требует их для самой установки
/// задания - для обычного пользователя это тупик, поэтому ветка реестра.
/// </summary>
public static class AutoStart
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CVPN";

    /// <summary>С этим аргументом окно не показывается: приложение сразу уходит в трей.</summary>
    public const string MinimizedArgument = "--minimized";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key);
            return key?.GetValue(ValueName) is not null;
        }
    }

    public static void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Key, writable: true);
        if (key is null) return;

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;

        // Кавычки обязательны: путь почти наверняка содержит пробелы
        key.SetValue(ValueName, $"\"{exe}\" {MinimizedArgument}", RegistryValueKind.String);
    }

    /// <summary>Синхронизирует реестр с настройкой - на случай переноса приложения в другую папку.</summary>
    public static void Sync(bool enabled)
    {
        if (enabled || IsEnabled) Apply(enabled);
    }
}