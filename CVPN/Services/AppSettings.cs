using CVPN.Core;

namespace CVPN.Services;

public sealed class AppSettings : ObservableObject
{
    private string _corePath = AppPaths.DefaultCorePath;
    private bool _tunEnabled = true;
    private int _mixedPort = 2080;
    private string _remoteDns = "1.1.1.1";
    private string _localDns = "8.8.8.8";
    private bool _proxyByDefault = true;
    private int _clashApiPort = 9191;
    private string _logLevel = "info";
 
    /// <summary>Полный путь к sing-box.exe.</summary>
    public string CorePath { get => _corePath; set => Set(ref _corePath, value); }
 
    /// <summary>TUN перехватывает весь системный трафик, но требует прав администратора.</summary>
    public bool TunEnabled { get => _tunEnabled; set => Set(ref _tunEnabled, value); }
 
    /// <summary>HTTP+SOCKS на одном порту — для приложений, которые настраивают прокси сами.</summary>
    public int MixedPort { get => _mixedPort; set => Set(ref _mixedPort, value); }
 
    public string RemoteDns { get => _remoteDns; set => Set(ref _remoteDns, value); }
    public string LocalDns { get => _localDns; set => Set(ref _localDns, value); }
 
    /// <summary>Что делать с трафиком вне правил: true — через прокси, false — напрямую.</summary>
    public bool ProxyByDefault { get => _proxyByDefault; set => Set(ref _proxyByDefault, value); }
 
    /// <summary>Порт Clash API — через него читаются счётчики трафика и задержки.</summary>
    public int ClashApiPort { get => _clashApiPort; set => Set(ref _clashApiPort, value); }
 
    public string LogLevel { get => _logLevel; set => Set(ref _logLevel, value); }
}