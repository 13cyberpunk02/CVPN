using System.IO;
using System.Text.Json;
using CVPN.Ipc;

namespace CVPN.Services;

/// <summary>
/// Собирает локальные наборы правил для передачи службе.
///
/// Служба работает под SYSTEM и не имеет доступа к каталогу пользователя,
/// где приложение хранит .srs. Поэтому файлы едут вместе с конфигом, а служба
/// раскладывает их у себя - путь от клиента при этом не принимается.
/// </summary>
public static class RuleSetPayload
{
    private const long MaxFileBytes = 8 * 1024 * 1024;
 
    public static List<RuleSetFile> Collect(string configJson)
    {
        var files = new List<RuleSetFile>();
 
        try
        {
            using var doc = JsonDocument.Parse(configJson);
 
            if (!doc.RootElement.TryGetProperty("route", out var route) || 
                !route.TryGetProperty("rule_set", out var sets)) return files;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
 
            foreach (var set in sets.EnumerateArray())
            {
                if (set.TryGetProperty("type", out var type) && type.GetString() != "local") continue;
                if (!set.TryGetProperty("path", out var pathValue)) continue;
 
                var path = pathValue.GetString() ?? "";
                if (path.Length == 0 || !File.Exists(path)) continue;
 
                var name = Path.GetFileName(path);
                if (!seen.Add(name)) continue;
 
                var info = new FileInfo(path);
                if (info.Length > MaxFileBytes) continue;
 
                files.Add(new RuleSetFile
                {
                    Name = name,
                    Content = Convert.ToBase64String(File.ReadAllBytes(path))
                });
            }
        }
        catch (Exception)
        {
            // Не смогли прочитать - служба отбросит набор, о чём сообщит в логе
        }
 
        return files;
    }
}

