using System.Text.Json;
using CVPN.Models;

namespace CVPN.Tests;

public class ProtectedSerializationTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
 
    [Fact]
    public void Секреты_профиля_не_видны_в_файле()
    {
        var profile = TestData.Reality();
        profile.Password = "пароль-для-теста";
 
        var json = JsonSerializer.Serialize(profile, Options);
 
        Assert.DoesNotContain(profile.Uuid, json);
        Assert.DoesNotContain("пароль-для-теста", json);
        Assert.Contains("dpapi:", json);
    }
 
    [Fact]
    public void Профиль_читается_обратно_без_потерь()
    {
        var profile = TestData.Reality();
 
        var restored = JsonSerializer.Deserialize<ServerProfile>(
            JsonSerializer.Serialize(profile, Options), Options)!;
 
        Assert.Equal(profile.Uuid, restored.Uuid);
        Assert.Equal(profile.Password, restored.Password);
    }
 
    /// <summary>Адрес, порт и SNI секретами не являются и остаются читаемыми.</summary>
    [Fact]
    public void Несекретные_поля_остаются_открытыми()
    {
        var json = JsonSerializer.Serialize(TestData.Reality(), Options);
 
        Assert.Contains("de.example.net", json);
        Assert.Contains("www.google.com", json);
    }
 
    [Fact]
    public void Старый_файл_с_открытым_паролем_читается()
    {
        const string json = """
                            { "Name": "Старый", "Host": "old.example.net", "Port": 443,
                              "Protocol": "AnyTls", "Password": "открытым-текстом" }
                            """;
 
        var profile = JsonSerializer.Deserialize<ServerProfile>(json, Options)!;
 
        Assert.Equal("открытым-текстом", profile.Password);
    }
}