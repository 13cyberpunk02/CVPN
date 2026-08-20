using System.Text.Json;
using System.Text.Json.Nodes;

namespace CVPN.Service;

/// <summary>
/// Приводит присланный конфиг к безопасному виду.
///
/// Служба работает под LocalSystem, а конфиг приходит от обычного пользователя.
/// Без этой обработки любой локальный пользователь смог бы заставить SYSTEM
/// писать файлы куда угодно — например, через experimental.cache_file.path
/// или log.output. Поэтому все пути в конфиге переписываются на служебные,
/// а ссылки на локальные файлы разрешены только внутри каталога службы.
/// </summary>
public static class ConfigSanitizer
{
    public static string Sanitize(string incoming, string dataDir)
    {
        var root = JsonNode.Parse(incoming) as JsonObject
                   ?? throw new InvalidDataException("Конфигурация не является объектом JSON");

        ForceLog(root);
        ForceCacheFile(root, dataDir);
        RestrictRuleSets(root, dataDir);
        RestrictClashApi(root);

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Вывод только в stdout: файл лога — это запись по произвольному пути.</summary>
    private static void ForceLog(JsonObject root)
    {
        if (root["log"] is not JsonObject log)
        {
            root["log"] = new JsonObject { ["level"] = "info", ["timestamp"] = true };
            return;
        }

        log.Remove("output");
    }

    private static void ForceCacheFile(JsonObject root, string dataDir)
    {
        if (root["experimental"] is not JsonObject experimental) return;

        if (experimental["cache_file"] is JsonObject cache)
            cache["path"] = Path.Combine(dataDir, "cache.db");
    }

    /// <summary>Локальные наборы правил читаются только из каталога службы.</summary>
    private static void RestrictRuleSets(JsonObject root, string dataDir)
    {
        if (root["route"] is not JsonObject route) return;
        if (route["rule_set"] is not JsonArray sets) return;

        var rulesDir = Path.Combine(dataDir, "rules");
        var kept = new JsonArray();

        foreach (var node in sets.ToList())
        {
            if (node is not JsonObject set) continue;

            sets.Remove(node);

            if (set["type"]?.GetValue<string>() == "local")
            {
                var path = set["path"]?.GetValue<string>() ?? "";
                var full = Path.GetFullPath(path);

                // Выход за пределы каталога — набор отбрасывается целиком
                if (!full.StartsWith(rulesDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    continue;

                set["path"] = full;
            }

            kept.Add(set);
        }

        route["rule_set"] = kept;
    }

    /// <summary>API остаётся на петле: наружу его выставлять незачем.</summary>
    private static void RestrictClashApi(JsonObject root)
    {
        if (root["experimental"] is not JsonObject experimental) return;
        if (experimental["clash_api"] is not JsonObject api) return;

        var listen = api["external_controller"]?.GetValue<string>() ?? "";

        if (!listen.StartsWith("127.0.0.1:", StringComparison.Ordinal))
            api["external_controller"] = "127.0.0.1:9191";
    }
}