using System.Text.Json;
using CVPN.Models;
using CVPN.Models.Enums;
using CVPN.Services;

namespace CVPN.Tests;

/// <summary>Заготовки профилей и разбор результата - чтобы тесты читались как утверждения.</summary>
public static class TestData
{
    public static ServerProfile Reality(string name = "Germany-Frankfurt") => new()
    {
        Name = name,
        Host = "de.example.net",
        Port = 8443,
        Protocol = ProtocolKind.VlessReality,
        Uuid = "8f1ce66e-719d-48b8-9ee6-804b52887082",
        PublicKey = "A32t3s5q0ea82zSZKJOtT-HrXKwkwa9r9pjgnRveVT8",
        ShortId = "f40fa49adab4f9a9",
        Sni = "www.google.com",
        Flow = "xtls-rprx-vision"
    };
 
    public static ServerProfile WebSocket(string name = "Amsterdam") => new()
    {
        Name = name,
        Host = "nl.example.net",
        Port = 443,
        Protocol = ProtocolKind.VlessWs,
        Uuid = "11111111-2222-3333-4444-555555555555",
        Sni = "cdn.example.net",
        Path = "/ws"
    };
 
    public static ServerProfile AnyTls(string name = "Helsinki") => new()
    {
        Name = name,
        Host = "fi.example.net",
        Port = 8443,
        Protocol = ProtocolKind.AnyTls,
        Password = "s3cret",
        Sni = "fi.example.net"
    };
 
    public static ServerProfile Naive(string name = "Tokyo") => new()
    {
        Name = name,
        Host = "jp.example.net",
        Port = 443,
        Protocol = ProtocolKind.Naive,
        Username = "user",
        Password = "s3cret"
    };
 
    public static RoutingProfile Routing(params RouteRule[] rules) => new()
    {
        Name = "Основной",
        ProxyByDefault = true,
        Rules = [.. rules]
    };
 
    public static RouteRule Rule(MatchKind match, string value, RouteAction action) =>
        new() { Match = match, Value = value, Action = action };
 
    public static AppSettings Settings() => new()
    {
        TunEnabled = true,
        MixedPort = 2080,
        ClashApiPort = 9191,
        RemoteDns = "https://1.1.1.1/dns-query",
        LogLevel = "info"
    };
 
    /// <summary>Собирает конфиг и возвращает его разобранным.</summary>
    public static JsonElement BuildConfig(
        IReadOnlyList<ServerProfile>? profiles = null,
        RoutingProfile? routing = null,
        AppSettings? settings = null,
        ServerProfile? active = null)
    {
        profiles ??= [Reality()];
        active ??= profiles[0];
 
        var json = ConfigBuilder.Build(profiles, active, routing ?? Routing(), settings ?? Settings());
 
        return JsonDocument.Parse(json).RootElement.Clone();
    }
 
    public static IEnumerable<JsonElement> Outbounds(this JsonElement config) =>
        config.GetProperty("outbounds").EnumerateArray();
 
    public static JsonElement OutboundOfType(this JsonElement config, string type) =>
        config.Outbounds().Single(o => o.GetProperty("type").GetString() == type);
 
    public static string? Str(this JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetString() : null;
}
