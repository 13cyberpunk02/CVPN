using System.Net;
using System.Net.NetworkInformation;
using CVPN.Services;

namespace CVPN.Tests;

/// <summary>
/// От выбора адаптера зависит, куда уйдёт трафик ByeDPI: привязка к виртуальному
/// адаптеру вернула бы его обратно в туннель, поэтому выбор проверяется тестами.
/// </summary>
public class PhysicalAdapterTests
{
    private static PhysicalAdapter.Candidate Nic(
        string name = "Ethernet",
        string description = "Realtek PCIe GbE Family Controller",
        NetworkInterfaceType type = NetworkInterfaceType.Ethernet,
        bool up = true,
        bool gateway = true,
        string? ip = "192.168.1.10") =>
        new(name, description, type, up, gateway, ip is null ? null : IPAddress.Parse(ip));

    [Fact]
    public void Берётся_рабочий_проводной_адаптер_со_шлюзом()
    {
        Assert.Equal(IPAddress.Parse("192.168.1.10"), PhysicalAdapter.Pick([Nic()]));
    }

    [Fact]
    public void Wi_Fi_тоже_подходит()
    {
        var wifi = Nic("Wi-Fi", "Intel Wi-Fi 6", NetworkInterfaceType.Wireless80211, ip: "10.0.0.5");

        Assert.Equal(IPAddress.Parse("10.0.0.5"), PhysicalAdapter.Pick([wifi]));
    }

    [Fact]
    public void Адаптер_туннеля_не_выбирается()
    {
        var tun = Nic("sing-tun", "sing-tun Tunnel", NetworkInterfaceType.Ethernet, ip: "172.19.0.1");

        Assert.Null(PhysicalAdapter.Pick([tun]));
    }

    [Fact]
    public void Адрес_туннеля_пропускается_даже_под_другим_именем()
    {
        var odd = Nic("Ethernet 5", "Some adapter", ip: "172.19.0.1");
        var real = Nic(ip: "192.168.1.10");

        Assert.Equal(IPAddress.Parse("192.168.1.10"), PhysicalAdapter.Pick([odd, real]));
    }

    [Theory]
    [InlineData("vEthernet (WSL)", "Hyper-V Virtual Ethernet Adapter")]
    [InlineData("VMware Network Adapter VMnet8", "VMware Virtual Ethernet Adapter")]
    [InlineData("VirtualBox Host-Only Network", "VirtualBox Host-Only Ethernet Adapter")]
    [InlineData("Tailscale", "Tailscale Tunnel")]
    [InlineData("Ethernet 2", "Wintun Userspace Tunnel")]
    public void Виртуальные_адаптеры_пропускаются(string name, string description)
    {
        var virtualNic = Nic(name, description, ip: "192.168.56.1");
        var real = Nic(ip: "192.168.1.10");

        Assert.Equal(IPAddress.Parse("192.168.1.10"), PhysicalAdapter.Pick([virtualNic, real]));
    }

    [Fact]
    public void Без_шлюза_выхода_в_интернет_у_адаптера_нет()
    {
        Assert.Null(PhysicalAdapter.Pick([Nic(gateway: false)]));
    }

    [Fact]
    public void Отключённый_адаптер_не_выбирается()
    {
        Assert.Null(PhysicalAdapter.Pick([Nic(up: false)]));
    }

    [Fact]
    public void Без_ipv4_адаптер_не_выбирается()
    {
        Assert.Null(PhysicalAdapter.Pick([Nic(ip: null)]));
    }

    [Fact]
    public void Петлевой_адаптер_не_выбирается()
    {
        var loop = Nic("Loopback", "Software Loopback Interface 1", NetworkInterfaceType.Loopback, ip: "127.0.0.1");

        Assert.Null(PhysicalAdapter.Pick([loop]));
    }

    [Fact]
    public void Нет_подходящих_адаптеров_значит_null()
    {
        Assert.Null(PhysicalAdapter.Pick([]));
    }
}