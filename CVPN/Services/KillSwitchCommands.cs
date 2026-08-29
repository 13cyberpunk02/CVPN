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
    public static IReadOnlyList<string> Enable(string corePath, string appPath, bool allowLocalNetwork)
    {
        var commands = new List<string>
        {
            // Политика по умолчанию: наружу нельзя ничего
            "advfirewall set allprofiles firewallpolicy blockinbound,blockoutbound",
 
            // Ядру можно: именно оно держит соединение с сервером
            Allow("core", $"program=\"{corePath}\""),
 
            // Приложению можно: иначе не достучится до Clash API и не проверит обновления
            Allow("app", $"program=\"{appPath}\""),
 
            // Петля: на ней живут mixed-порт и Clash API
            Allow("loopback", "remoteip=127.0.0.1")
        };
 
        if (allowLocalNetwork)
        {
            // Локальная сеть остаётся доступной - принтеры, NAS, роутер.
            // Тот же принцип, что и правило ip_is_private в маршрутах.
            commands.Add(Allow("lan", "remoteip=LocalSubnet"));
 
            // Без DHCP адрес не обновится и сеть отвалится сама по себе
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
