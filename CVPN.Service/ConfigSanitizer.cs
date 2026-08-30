using System.Text.Json;
using System.Text.Json.Nodes;

namespace CVPN.Service;

/// <summary>
/// Приводит присланный конфиг к безопасному виду.
///
/// Служба работает под LocalSystem, а конфиг приходит от обычного пользователя.
/// Без этой обработки любой локальный пользователь смог бы заставить SYSTEM
/// писать файлы куда угодно - например, через experimental.cache_file.path
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

    /// <summary>Вывод только в stdout: файл лога - это запись по произвольному пути.</summary>
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

    /// <summary>
    /// Локальные наборы читаются только из каталога службы. Путь от клиента
    /// не принимается вовсе: берётся одно имя файла, и набор остаётся, только
    /// если такой файл действительно лежит у нас.
    ///
    /// Если набор отброшен, ссылки на него убираются и из правил - иначе ядро
    /// падает с «rule-set not found» при инициализации.
    /// </summary>
    private static void RestrictRuleSets(JsonObject root, string dataDir)
    {
        if (root["route"] is not JsonObject route) return;
        if (route["rule_set"] is not JsonArray sets) return;

        var rulesDir = Path.Combine(dataDir, "rules");
        var kept = new JsonArray();
        var dropped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in sets.ToList())
        {
            if (node is not JsonObject set) continue;

            sets.Remove(node);

            var tag = set["tag"]?.GetValue<string>() ?? "";

            if (set["type"]?.GetValue<string>() == "local")
            {
                // Только имя файла: так «..\..\windows\evil.srs» превращается
                // в «evil.srs» и никуда за пределы каталога не уводит
                var name = Path.GetFileName(set["path"]?.GetValue<string>() ?? "");
                var full = Path.Combine(rulesDir, name);

                if (name.Length == 0 || !File.Exists(full))
                {
                    if (tag.Length > 0) dropped.Add(tag);
                    continue;
                }

                set["path"] = full;
            }

            kept.Add(set);
        }

        route["rule_set"] = kept;

        if (dropped.Count > 0) RemoveReferences(root, dropped);
    }

    /// <summary>Убирает из правил ссылки на наборы, которых нет.</summary>
    private static void RemoveReferences(JsonObject root, HashSet<string> dropped)
    {
        foreach (var section in new[] { "route", "dns" })
        {
            if (root[section] is not JsonObject node) continue;
            if (node["rules"] is not JsonArray rules) continue;

            var kept = new JsonArray();

            foreach (var item in rules.ToList())
            {
                rules.Remove(item);

                if (item is not JsonObject rule) continue;

                if (rule["rule_set"] is JsonArray tags)
                {
                    var remaining = new JsonArray();

                    foreach (var tag in tags.ToList())
                    {
                        tags.Remove(tag);

                        if (!dropped.Contains(tag?.GetValue<string>() ?? "")) remaining.Add(tag);
                    }

                    // У правила не осталось условий - оно совпало бы со всем подряд
                    if (remaining.Count == 0) continue;

                    rule["rule_set"] = remaining;
                }

                kept.Add(rule);
            }

            node["rules"] = kept;
        }
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