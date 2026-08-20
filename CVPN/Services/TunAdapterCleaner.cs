using System.Diagnostics;

namespace CVPN.Services;

/// <summary>
/// Удаление «осиротевших» TUN-адаптеров.
///
/// Если процесс sing-box завершился аварийно или был снят принудительно,
/// виртуальный адаптер остаётся зарегистрированным в системе. Следующий запуск
/// падает с «create adapter: Cannot create a file when that file already exists»,
/// потому что создать новый нельзя, а открыть существующий не выходит -
/// его запись повреждена.
///
/// Требует прав администратора. Без них вызов просто ничего не делает:
/// TUN в таком режиме всё равно не поднимется.
/// </summary>
public static class TunAdapterCleaner
{
    /// <summary>
    /// Ищет адаптеры на драйвере Wintun и удаляет их. Возвращает текст для лога
    /// либо пустую строку, если удалять было нечего.
    /// </summary>
    public static async Task<string> RemoveStaleAsync(CancellationToken ct = default)
    {
        if (!Core.Elevation.IsElevated) return "";
 
        // Get-NetAdapter показывает и отключённые адаптеры, поэтому -IncludeHidden
        const string script =
            "$found = Get-NetAdapter -IncludeHidden -ErrorAction SilentlyContinue | " +
            "Where-Object { $_.InterfaceDescription -like '*Wintun*' -or $_.Name -like '*sing-box*' }; " +
            "if ($found) { $found | Remove-NetAdapter -Confirm:$false -ErrorAction SilentlyContinue; " +
            "$found.Count } else { 0 }";
 
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
 
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-NonInteractive");
            info.ArgumentList.Add("-Command");
            info.ArgumentList.Add(script);
 
            using var process = Process.Start(info);
            if (process is null) return "";
 
            var output = await process.StandardOutput.ReadToEndAsync(ct);
 
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(cts.Token);
 
            return int.TryParse(output.Trim(), out var count) && count > 0
                ? $"удалено зависших адаптеров: {count}"
                : "";
        }
        catch (Exception)
        {
            // Очистка - вспомогательный шаг: не вышло, значит пробуем запускаться как есть
            return "";
        }
    }
}