using CVPN.Services;

namespace CVPN.Models;

public sealed class StoredState
{
    public List<ServerProfile> Profiles { get; set; } = [];
    public AppSettings Settings { get; set; } = new();
    public string? ActiveProfileName { get; set; }
 
    public List<RoutingProfile> RoutingProfiles { get; set; } = [];
    public string? ActiveRoutingProfile { get; set; }
 
    /// <summary>
    /// Старый формат: правила лежали одним списком. Читается ради совместимости
    /// и при первой же загрузке превращается в набор «Основной».
    /// </summary>
    public List<RouteRule>? Rules { get; set; }
 
    public void Migrate()
    {
        if (RoutingProfiles.Count == 0)
        {
            RoutingProfiles.Add(new RoutingProfile
            {
                Name = "Основной",
                ProxyByDefault = Settings.ProxyByDefault,
                Rules = [.. Rules ?? []]
            });
        }
 
        Rules = null;
        ActiveRoutingProfile ??= RoutingProfiles[0].Name;
    }
}