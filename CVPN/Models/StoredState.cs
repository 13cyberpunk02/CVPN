using CVPN.Services;

namespace CVPN.Models;

public sealed class StoredState
{
    public List<ServerProfile> Profiles { get; set; } = [];
    public List<RouteRule> Rules { get; set; } = [];
    public AppSettings Settings { get; set; } = new();
    public string? ActiveProfileName { get; set; }
}