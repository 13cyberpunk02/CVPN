using CVPN.Services;

namespace CVPN.Tests;

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("V2.0", "2.0")]
    [InlineData("v1.2.3.4", "1.2.3.4")]
    public void Тег_разбирается_в_версию(string tag, string expected)
    {
        Assert.Equal(Version.Parse(expected), UpdateChecker.ParseTag(tag));
    }

    /// <summary>Version не понимает одиночное число - дополняем до двух частей.</summary>
    [Fact]
    public void Тег_из_одного_числа_дополняется()
    {
        Assert.Equal(new Version(2, 0), UpdateChecker.ParseTag("v2"));
    }

    [Theory]
    [InlineData("v1.2.3-beta", "1.2.3")]
    [InlineData("v1.2.3+build7", "1.2.3")]
    public void Суффиксы_отбрасываются(string tag, string expected)
    {
        Assert.Equal(Version.Parse(expected), UpdateChecker.ParseTag(tag));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("релиз")]
    [InlineData(null)]
    public void Непонятный_тег_даёт_null(string? tag)
    {
        Assert.Null(UpdateChecker.ParseTag(tag));
    }

    [Theory]
    [InlineData("v1.1.0", "1.0.0", true)]
    [InlineData("v1.0.1", "1.0.0", true)]
    [InlineData("v2.0", "1.9.9", true)]
    [InlineData("v1.0.0", "1.0.0", false)]
    [InlineData("v0.9.0", "1.0.0", false)]
    public void Новее_определяется_сравнением_версий(string tag, string current, bool expected)
    {
        Assert.Equal(expected, UpdateChecker.IsNewer(tag, Version.Parse(current)));
    }

    /// <summary>Мусорный тег не должен выглядеть как доступное обновление.</summary>
    [Fact]
    public void Неразобранный_тег_обновлением_не_считается()
    {
        Assert.False(UpdateChecker.IsNewer("latest", new Version(1, 0, 0)));
        Assert.False(UpdateChecker.IsNewer(null, new Version(1, 0, 0)));
    }
}