namespace CVPN.Services;

/// <summary>
/// Аргументы netsh для kill switch. Вынесены отдельно от выполнения, чтобы
/// правила можно было проверить тестами: ошибка здесь оставляет человека
/// без интернета, и «посмотрим на живой машине» - плохая стратегия.
/// </summary>
public static class KillSwitchCommands
{
    /// <summary>Общая часть имени: по ней правила потом находятся и удаляются.</summary>
    public const string RulePrefix = "CVPN Kill Switch";

    /// <summary>
    /// Запрещает весь исходящий трафик и разрешает ровно то, без чего
    /// туннель не поднимется.
    /// </summary>
    public static IReadOnlyList<string> Enable(
        string corePath, string appPath, bool allowLocalNetwork, string? byeDpiPath = null)
    {
        var commands = new List<string>
        {
            "advfirewall set allprofiles firewallpolicy blockinbound,blockoutbound",
            Allow("core", $"program=\"{corePath}\""),
            Allow("app", $"program=\"{appPath}\""),
            Allow("loopback", "remoteip=127.0.0.1")
        };

        if (!string.IsNullOrWhiteSpace(byeDpiPath))
            commands.Add(Allow("byedpi", $"program=\"{byeDpiPath}\""));

        if (allowLocalNetwork)
        {
            commands.Add(Allow("lan", "remoteip=LocalSubnet"));
            commands.Add(Allow("dhcp", "protocol=UDP localport=68 remoteport=67"));
        }

        return commands;
    }

    /// <summary>Возвращает систему в обычное состояние. Порядок важен: сначала политика.</summary>
    public static IReadOnlyList<string> Disable() =>
    [
        "advfirewall set allprofiles firewallpolicy blockinbound,allowoutbound",
        Delete("core"),
        Delete("app"),
        Delete("byedpi"),
        Delete("loopback"),
        Delete("lan"),
        Delete("dhcp")
    ];

    private static string Allow(string suffix, string filter) =>
        $"advfirewall firewall add rule name=\"{RulePrefix} - {suffix}\" " +
        $"dir=out action=allow enable=yes {filter}";

    private static string Delete(string suffix) =>
        $"advfirewall firewall delete rule name=\"{RulePrefix} - {suffix}\"";
}