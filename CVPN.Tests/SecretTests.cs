using CVPN.Core;

namespace CVPN.Tests;

public class SecretTests
{
        [Fact]
    public void Значение_расшифровывается_обратно()
    {
        const string plain = "8f1ce66e-719d-48b8-9ee6-804b52887082";
 
        var protectedValue = Secret.Protect(plain);
 
        Assert.NotEqual(plain, protectedValue);
        Assert.Equal(plain, Secret.Unprotect(protectedValue));
    }
 
    [Fact]
    public void Шифротекст_не_содержит_исходной_строки()
    {
        const string plain = "очень-секретный-пароль";
 
        Assert.DoesNotContain(plain, Secret.Protect(plain));
    }
 
    [Fact]
    public void Кириллица_и_спецсимволы_переживают_шифрование()
    {
        const string plain = "пароль с пробелами и «кавычками» - 42";
 
        Assert.Equal(plain, Secret.Unprotect(Secret.Protect(plain)));
    }
 
    /// <summary>Файлы, созданные до появления шифрования, должны читаться.</summary>
    [Fact]
    public void Строка_без_метки_возвращается_как_есть()
    {
        Assert.Equal("открытый-пароль", Secret.Unprotect("открытый-пароль"));
    }
 
    [Fact]
    public void Пустые_значения_остаются_пустыми()
    {
        Assert.Equal("", Secret.Protect(""));
        Assert.Equal("", Secret.Protect(null));
        Assert.Equal("", Secret.Unprotect(""));
        Assert.Equal("", Secret.Unprotect(null));
    }
 
    /// <summary>Чужой или повреждённый шифротекст не должен ронять загрузку профилей.</summary>
    [Fact]
    public void Повреждённое_значение_даёт_пустую_строку_без_исключения()
    {
        Secret.ResetFailures();
 
        Assert.Equal("", Secret.Unprotect("dpapi:это-не-base64"));
        Assert.Equal("", Secret.Unprotect("dpapi:AAAAAAAAAAAAAAAAAAAAAA=="));
 
        Assert.True(Secret.FailureCount >= 2);
    }
 
    [Fact]
    public void Метка_распознаётся()
    {
        Assert.True(Secret.IsProtected(Secret.Protect("значение")));
        Assert.False(Secret.IsProtected("значение"));
        Assert.False(Secret.IsProtected(null));
    }
 
    [Fact]
    public void Одно_значение_шифруется_каждый_раз_по_разному()
    {
        // DPAPI добавляет случайность - одинаковый результат означал бы,
        // что по файлу видно совпадение паролей у разных профилей
        var first = Secret.Protect("одинаковый");
        var second = Secret.Protect("одинаковый");
 
        Assert.NotEqual(first, second);
        Assert.Equal(Secret.Unprotect(first), Secret.Unprotect(second));
    }
}