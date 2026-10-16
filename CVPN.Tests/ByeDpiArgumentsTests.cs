using CVPN.Services;

namespace CVPN.Tests;

/// <summary>
/// Аргументы ciadpi определяют, кто увидит локальный SOCKS-прокси. По умолчанию
/// ByeDPI слушает 0.0.0.0, то есть всю сеть, - поэтому адрес и порт приложение
/// задаёт само, и это проверяется здесь.
/// </summary>
public class ByeDpiArgumentsTests
{
    [Fact]
    public void Адрес_и_порт_подставляются_первыми()
    {
        var result = ByeDpiArguments.Build("--tlsrec 1+s", 1081);

        Assert.Equal(["--ip", "127.0.0.1", "--port", "1081", "--tlsrec", "1+s"], result.Arguments);
        Assert.Empty(result.Dropped);
    }

    [Fact]
    public void Пустая_строка_даёт_только_адрес_и_порт()
    {
        var result = ByeDpiArguments.Build("   ", 9000);

        Assert.Equal(["--ip", "127.0.0.1", "--port", "9000"], result.Arguments);
    }

    /// <summary>Чужой адрес открыл бы прокси локальной сети; чужой порт разошёлся бы с конфигом.</summary>
    [Theory]
    [InlineData("--ip 0.0.0.0")]
    [InlineData("--ip=0.0.0.0")]
    [InlineData("-i 0.0.0.0")]
    [InlineData("-i0.0.0.0")]
    [InlineData("--port 8080")]
    [InlineData("--port=8080")]
    [InlineData("-p 8080")]
    [InlineData("-p8080")]
    public void Свои_адрес_и_порт_выбрасываются(string input)
    {
        var result = ByeDpiArguments.Build($"{input} --tlsrec 1+s", 1081);

        Assert.Equal(["--ip", "127.0.0.1", "--port", "1081", "--tlsrec", "1+s"], result.Arguments);
        Assert.Single(result.Dropped);
    }

    [Fact]
    public void Значение_выброшенной_опции_уходит_вместе_с_ней()
    {
        var result = ByeDpiArguments.Build("--ip 0.0.0.0 --disorder 1", 1081);

        Assert.DoesNotContain("0.0.0.0", result.Arguments);
        Assert.Contains("--disorder", result.Arguments);
        Assert.Equal("--ip 0.0.0.0", result.Dropped.Single());
    }

    /// <summary>Похожие по имени опции - не адрес и не порт, их трогать нельзя.</summary>
    [Theory]
    [InlineData("--pf")]
    [InlineData("--ttl")]
    [InlineData("--auto=torst")]
    [InlineData("--split")]
    public void Прочие_опции_не_затрагиваются(string option)
    {
        var result = ByeDpiArguments.Build($"{option} 1", 1081);

        Assert.Contains(option, result.Arguments);
        Assert.Empty(result.Dropped);
    }

    [Fact]
    public void Кавычки_сохраняют_аргумент_с_пробелами_целым()
    {
        var tokens = ByeDpiArguments.Tokenize("--hosts \"C:\\my lists\\ru.txt\" --split 1");

        Assert.Equal(["--hosts", @"C:\my lists\ru.txt", "--split", "1"], tokens);
    }

    [Fact]
    public void Лишние_пробелы_и_переводы_строк_не_создают_пустых_аргументов()
    {
        var tokens = ByeDpiArguments.Tokenize("  --a   1 \r\n --b\t2  ");

        Assert.Equal(["--a", "1", "--b", "2"], tokens);
    }

    [Fact]
    public void Строка_запуска_заключает_пробельные_аргументы_в_кавычки()
    {
        var result = ByeDpiArguments.Build("--hosts \"C:\\my lists\\ru.txt\"", 1081);

        Assert.Contains("\"C:\\my lists\\ru.txt\"", result.CommandLine);
    }

    [Fact]
    public void Адрес_всегда_петлевой()
    {
        Assert.Equal("127.0.0.1", ByeDpiArguments.LoopbackAddress);
    }

    [Fact]
    public void Привязка_к_адаптеру_добавляется_после_адреса_и_порта()
    {
        var result = ByeDpiArguments.Build("--tlsrec 1+s", 1081, "192.168.1.10");

        Assert.Equal(
            ["--ip", "127.0.0.1", "--port", "1081", "--conn-ip", "192.168.1.10", "--tlsrec", "1+s"],
            result.Arguments);
    }

    [Fact]
    public void Без_адреса_привязка_не_добавляется()
    {
        Assert.DoesNotContain("--conn-ip", ByeDpiArguments.Build("--tlsrec 1+s", 1081).Arguments);
        Assert.DoesNotContain("--conn-ip", ByeDpiArguments.Build("--tlsrec 1+s", 1081, "  ").Arguments);
    }

    /// <summary>Если человек привязал ciadpi сам, его выбор важнее автоматического.</summary>
    [Theory]
    [InlineData("--conn-ip 10.0.0.9")]
    [InlineData("--conn-ip=10.0.0.9")]
    [InlineData("-I 10.0.0.9")]
    [InlineData("-I10.0.0.9")]
    public void Свой_conn_ip_побеждает_автоматический(string input)
    {
        var result = ByeDpiArguments.Build(input, 1081, "192.168.1.10");

        Assert.DoesNotContain("192.168.1.10", result.Arguments);
        Assert.Empty(result.Dropped);
    }

    /// <summary>Заглавная -I (привязка исходящих) и строчная -i (адрес прослушивания) - разные опции.</summary>
    [Fact]
    public void Заглавная_I_не_путается_со_строчной_i()
    {
        var result = ByeDpiArguments.Build("-i 0.0.0.0 -I 10.0.0.9", 1081);

        Assert.Equal(["-i 0.0.0.0"], result.Dropped);
        Assert.Contains("-I", result.Arguments);
    }
}