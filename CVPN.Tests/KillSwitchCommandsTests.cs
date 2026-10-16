using CVPN.Services;

namespace CVPN.Tests;

/// <summary>
/// Ошибка в этих правилах оставляет человека без интернета, поэтому они
/// проверяются здесь, а не на живой машине.
/// </summary>
public class KillSwitchCommandsTests
{
    private const string Core = @"C:\Program Files\CVPN\core\sing-box.exe";
    private const string App = @"C:\Program Files\CVPN\CVPN.exe";
    private const string ByeDpi = @"C:\tools\byedpi\ciadpi.exe";

    [Fact]
    public void Первой_идёт_смена_политики_по_умолчанию()
    {
        var commands = KillSwitchCommands.Enable(Core, App, allowLocalNetwork: false);

        Assert.StartsWith("advfirewall set allprofiles firewallpolicy blockinbound,blockoutbound",
            commands[0]);
    }

    [Fact]
    public void Ядру_и_приложению_разрешён_выход()
    {
        var commands = KillSwitchCommands.Enable(Core, App, allowLocalNetwork: false);

        Assert.Contains(commands, c => c.Contains(Core) && c.Contains("action=allow"));
        Assert.Contains(commands, c => c.Contains(App) && c.Contains("action=allow"));
    }

    /// <summary>Пути содержат пробелы - без кавычек netsh разберёт их как разные аргументы.</summary>
    [Fact]
    public void Пути_заключены_в_кавычки()
    {
        var commands = KillSwitchCommands.Enable(Core, App, allowLocalNetwork: false);

        Assert.Contains(commands, c => c.Contains($"program=\"{Core}\""));
    }

    [Fact]
    public void Петля_разрешена_всегда()
    {
        var commands = KillSwitchCommands.Enable(Core, App, allowLocalNetwork: false);

        Assert.Contains(commands, c => c.Contains("remoteip=127.0.0.1"));
    }

    [Fact]
    public void Локальная_сеть_разрешается_по_запросу()
    {
        var without = KillSwitchCommands.Enable(Core, App, allowLocalNetwork: false);
        var with = KillSwitchCommands.Enable(Core, App, allowLocalNetwork: true);

        Assert.DoesNotContain(without, c => c.Contains("LocalSubnet"));
        Assert.Contains(with, c => c.Contains("LocalSubnet"));
    }

    /// <summary>Без DHCP адрес не продлится, и сеть отвалится сама.</summary>
    [Fact]
    public void При_разрешённой_локальной_сети_открыт_dhcp()
    {
        var commands = KillSwitchCommands.Enable(Core, App, allowLocalNetwork: true);

        Assert.Contains(commands, c => c.Contains("localport=68") && c.Contains("remoteport=67"));
    }

    [Fact]
    public void Отключение_возвращает_политику_и_удаляет_все_правила()
    {
        // с ByeDPI правил на одно больше; Disable обязан удалять максимальный набор
        var enable = KillSwitchCommands.Enable(Core, App, allowLocalNetwork: true, ByeDpi);
        var disable = KillSwitchCommands.Disable();

        Assert.StartsWith("advfirewall set allprofiles firewallpolicy blockinbound,allowoutbound",
            disable[0]);

        // сколько правил создали, столько и удаляем
        var created = enable.Count(c => c.Contains("add rule"));
        var deleted = disable.Count(c => c.Contains("delete rule"));

        Assert.Equal(created, deleted);
    }

    [Fact]
    public void Имена_правил_общие_для_создания_и_удаления()
    {
        var enable = KillSwitchCommands.Enable(Core, App, allowLocalNetwork: true);
        var disable = KillSwitchCommands.Disable();

        Assert.All(enable.Where(c => c.Contains("add rule")),
            c => Assert.Contains(KillSwitchCommands.RulePrefix, c));
        Assert.All(disable.Where(c => c.Contains("delete rule")),
            c => Assert.Contains(KillSwitchCommands.RulePrefix, c));
    }

    /// <summary>ciadpi ходит в сеть сам: без правила kill switch его соединения заблокирует.</summary>
    [Fact]
    public void Byedpi_получает_разрешение_только_если_указан()
    {
        var without = KillSwitchCommands.Enable(Core, App, allowLocalNetwork: false);
        var with = KillSwitchCommands.Enable(Core, App, allowLocalNetwork: false, ByeDpi);

        Assert.DoesNotContain(without, c => c.Contains("byedpi", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(with, c => c.Contains(ByeDpi) && c.Contains("action=allow"));
        Assert.Equal(without.Count + 1, with.Count);
    }

    [Fact]
    public void Пустой_путь_byedpi_правила_не_создаёт()
    {
        var commands = KillSwitchCommands.Enable(Core, App, allowLocalNetwork: false, "  ");

        Assert.DoesNotContain(commands, c => c.Contains("byedpi", StringComparison.OrdinalIgnoreCase));
    }
}