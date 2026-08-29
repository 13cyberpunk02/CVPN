using System.IO;
using System.Text.Json;
using CVPN.Service;

namespace CVPN.Tests;

/// <summary>
/// Санитайзер - граница между обычным пользователем и процессом под SYSTEM.
/// Каждый тест здесь описывает конкретный способ злоупотребления.
/// </summary>
public class ConfigSanitizerTests
{
    private const string DataDir = @"C:\ProgramData\CVPN";
 
    private static JsonElement Sanitize(string json) =>
        JsonDocument.Parse(ConfigSanitizer.Sanitize(json, DataDir)).RootElement.Clone();
 
    /// <summary>Произвольный путь кэша - это запись файла от имени SYSTEM.</summary>
    [Fact]
    public void Путь_кэша_переписывается_на_служебный()
    {
        const string json = """
            { "experimental": { "cache_file": { "enabled": true, "path": "C:\\Windows\\System32\\evil.db" } } }
            """;
 
        var result = Sanitize(json);
        var path = result.GetProperty("experimental").GetProperty("cache_file").Str("path");
 
        Assert.Equal(@"C:\ProgramData\CVPN\cache.db", path);
    }
 
    /// <summary>log.output - второй способ заставить SYSTEM создать файл где угодно.</summary>
    [Fact]
    public void Файл_лога_запрещён()
    {
        const string json = """{ "log": { "level": "info", "output": "C:\\Windows\\Temp\\evil.log" } }""";
 
        var result = Sanitize(json);
 
        Assert.False(result.GetProperty("log").TryGetProperty("output", out _));
    }
 
    [Fact]
    public void Отсутствующий_блок_лога_создаётся()
    {
        var result = Sanitize("{}");
 
        Assert.Equal("info", result.GetProperty("log").Str("level"));
    }
 
    [Fact]
    public void Локальный_набор_вне_каталога_службы_отбрасывается()
    {
        const string json = """
            { "route": { "rule_set": [
                { "type": "local", "tag": "evil", "format": "binary", "path": "C:\\Users\\me\\evil.srs" }
            ] } }
            """;
 
        var result = Sanitize(json);
 
        Assert.Equal(0, result.GetProperty("route").GetProperty("rule_set").GetArrayLength());
    }
 
    [Fact]
    public void Локальный_набор_внутри_каталога_службы_остаётся()
    {
        const string json = """
            { "route": { "rule_set": [
                { "type": "local", "tag": "ok", "format": "binary",
                  "path": "C:\\ProgramData\\CVPN\\rules\\twitch.srs" }
            ] } }
            """;
 
        var result = Sanitize(json);
        var set = result.GetProperty("route").GetProperty("rule_set").EnumerateArray().Single();
 
        Assert.Equal("ok", set.Str("tag"));
    }
 
    /// <summary>Классический обход проверки префиксом: ..\ уводит наружу.</summary>
    [Fact]
    public void Путь_с_переходом_вверх_отбрасывается()
    {
        const string json = """
            { "route": { "rule_set": [
                { "type": "local", "tag": "escape", "format": "binary",
                  "path": "C:\\ProgramData\\CVPN\\rules\\..\\..\\..\\Windows\\evil.srs" }
            ] } }
            """;
 
        var result = Sanitize(json);
 
        Assert.Equal(0, result.GetProperty("route").GetProperty("rule_set").GetArrayLength());
    }
 
    [Fact]
    public void Удалённые_наборы_сохраняются()
    {
        const string json = """
            { "route": { "rule_set": [
                { "type": "remote", "tag": "geosite-youtube", "format": "binary",
                  "url": "https://example.com/geosite-youtube.srs" }
            ] } }
            """;
 
        var result = Sanitize(json);
        var set = result.GetProperty("route").GetProperty("rule_set").EnumerateArray().Single();
 
        Assert.Equal("remote", set.Str("type"));
    }
 
    /// <summary>API наружу открыло бы управление туннелем всей сети.</summary>
    [Theory]
    [InlineData("0.0.0.0:9090")]
    [InlineData("192.168.1.10:9090")]
    [InlineData(":9090")]
    public void Clash_api_возвращается_на_петлю(string listen)
    {
        var json = $$"""{ "experimental": { "clash_api": { "external_controller": "{{listen}}" } } }""";
 
        var result = Sanitize(json);
        var api = result.GetProperty("experimental").GetProperty("clash_api");
 
        Assert.StartsWith("127.0.0.1:", api.Str("external_controller"));
    }
 
    [Fact]
    public void Петлевой_адрес_не_меняется()
    {
        const string json = """
            { "experimental": { "clash_api": { "external_controller": "127.0.0.1:9191" } } }
            """;
 
        var result = Sanitize(json);
 
        Assert.Equal("127.0.0.1:9191",
            result.GetProperty("experimental").GetProperty("clash_api").Str("external_controller"));
    }
 
    [Fact]
    public void Не_объект_отвергается()
    {
        Assert.Throws<InvalidDataException>(() => ConfigSanitizer.Sanitize("[1,2,3]", DataDir));
    }
 
    /// <summary>Остальные секции конфига трогать нельзя - иначе туннель не поднимется.</summary>
    [Fact]
    public void Выходы_и_маршруты_остаются_нетронутыми()
    {
        const string json = """
            {
              "outbounds": [{ "type": "vless", "tag": "proxy", "server": "de.example.net" }],
              "route": { "final": "proxy", "rules": [{ "action": "sniff" }] }
            }
            """;
 
        var result = Sanitize(json);
 
        Assert.Equal("proxy", result.GetProperty("route").Str("final"));
        Assert.Equal("de.example.net",
            result.GetProperty("outbounds").EnumerateArray().Single().Str("server"));
    }
}