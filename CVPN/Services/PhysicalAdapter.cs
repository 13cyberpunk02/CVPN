using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace CVPN.Services;

/// <summary>
/// Ищет IPv4-адрес «настоящего» сетевого адаптера - того, через который компьютер
/// выходит в интернет в обход туннеля.
///
/// Нужен ByeDPI: чтобы его исходящие соединения не попадали в TUN (и приёмы вроде
/// --split и --disorder доходили до провайдера как есть), ciadpi привязывают
/// к адресу физического адаптера. У Windows для привязанного сокета маршрут берётся
/// с интерфейса, которому принадлежит адрес, а не из общей таблицы с маршрутами TUN.
/// </summary>
public static class PhysicalAdapter
{
    /// <summary>Что известно об адаптере. Отдельный тип - чтобы выбор проверялся тестами.</summary>
    public sealed record Candidate(
        string Name,
        string Description,
        NetworkInterfaceType Type,
        bool IsUp,
        bool HasGateway,
        IPAddress? Ipv4);

    /// <summary>Адрес из TUN-конфигурации CVPN: сам адаптер туннеля физическим не считается.</summary>
    private static readonly IPAddress TunAddress = IPAddress.Parse("172.19.0.1");

    private static readonly string[] VirtualMarkers =
    [
        "wintun", "sing-tun", "tap-windows", "tailscale", "wireguard", "zerotier",
        "vethernet", "hyper-v", "vmware", "virtualbox", "vpn", "loopback", "bluetooth"
    ];

    public static IPAddress? FindIPv4()
    {
        try
        {
            return Pick(NetworkInterface.GetAllNetworkInterfaces().Select(Describe));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Берёт первый работающий проводной или Wi-Fi адаптер со шлюзом.
    /// Шлюз важен: у адаптера без него нет собственного выхода в интернет.
    /// </summary>
    public static IPAddress? Pick(IEnumerable<Candidate> candidates) =>
        candidates
            .Where(c => c is { IsUp: true, HasGateway: true, Ipv4: not null })
            .Where(c => c.Type is NetworkInterfaceType.Ethernet
                            or NetworkInterfaceType.Wireless80211
                            or NetworkInterfaceType.GigabitEthernet)
            .Where(c => !c.Ipv4!.Equals(TunAddress))
            .Where(c => !IsVirtual(c))
            .Select(c => c.Ipv4)
            .FirstOrDefault();

    private static bool IsVirtual(Candidate c) =>
        VirtualMarkers.Any(m =>
            c.Name.Contains(m, StringComparison.OrdinalIgnoreCase) ||
            c.Description.Contains(m, StringComparison.OrdinalIgnoreCase));

    private static Candidate Describe(NetworkInterface nic)
    {
        var properties = nic.GetIPProperties();

        var ipv4 = properties.UnicastAddresses
            .Select(u => u.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);

        var hasGateway = properties.GatewayAddresses.Any(g =>
            g.Address.AddressFamily == AddressFamily.InterNetwork &&
            !g.Address.Equals(IPAddress.Any));

        return new Candidate(
            nic.Name,
            nic.Description,
            nic.NetworkInterfaceType,
            nic.OperationalStatus == OperationalStatus.Up,
            hasGateway,
            ipv4);
    }
}
