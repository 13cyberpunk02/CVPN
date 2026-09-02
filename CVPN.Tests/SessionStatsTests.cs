using CVPN.Core;
using CVPN.Models;

namespace CVPN.Tests;

public class ByteFormatTests
{
    [Theory]
    [InlineData(0, "0 Б")]
    [InlineData(512, "512 Б")]
    [InlineData(1024, "1 КБ")]
    [InlineData(1536, "1,5 КБ")]
    [InlineData(5 * 1024 * 1024, "5 МБ")]
    public void Объём_переводится_в_читаемый_вид(long bytes, string expected)
    {
        Assert.Equal(expected, ByteFormat.Size(bytes).Replace('.', ','));
    }

    [Fact]
    public void Отрицательное_значение_даёт_прочерк()
    {
        Assert.Equal("-", ByteFormat.Size(-1));
    }

    /// <summary>До мегабайта показываем килобайты: иначе на обычном канале одни нули.</summary>
    [Theory]
    [InlineData(2048, "КБ/с")]
    [InlineData(5 * 1024 * 1024, "МБ/с")]
    public void Единица_скорости_подбирается_по_величине(long bytes, string unit)
    {
        Assert.Equal(unit, ByteFormat.Rate(bytes).Unit);
    }
}

public class SessionStatsTests
{
    [Fact]
    public void До_первой_сессии_данных_нет()
    {
        var stats = new SessionStats();

        Assert.False(stats.HasData);
        Assert.Equal("-", stats.DurationLabel);
    }

    [Fact]
    public void Трафик_накапливается()
    {
        var stats = new SessionStats();
        stats.Begin("Frankfurt");

        stats.Add(100, 200);
        stats.Add(300, 400);

        Assert.Equal(400, stats.Upload);
        Assert.Equal(600, stats.Download);
    }

    /// <summary>Пик - максимум из секундных отсчётов, то есть скорость канала.</summary>
    [Fact]
    public void Пик_запоминает_наибольшую_секунду()
    {
        var stats = new SessionStats();
        stats.Begin("Frankfurt");

        stats.Add(0, 1024);
        stats.Add(0, 5 * 1024 * 1024);
        stats.Add(0, 2048);

        Assert.Contains("МБ/с", stats.PeakLabel);
    }

    [Fact]
    public void Новая_сессия_обнуляет_итоги()
    {
        var stats = new SessionStats();

        stats.Begin("Frankfurt");
        stats.Add(1000, 2000);

        stats.Begin("Amsterdam");

        Assert.Equal(0, stats.Upload);
        Assert.Equal(0, stats.Download);
        Assert.Equal("Amsterdam", stats.Server);
    }

    /// <summary>Итоги нужны и после отключения - иначе смотреть было бы не на что.</summary>
    [Fact]
    public void Итоги_остаются_после_окончания()
    {
        var stats = new SessionStats();
        stats.Begin("Frankfurt");
        stats.Add(1000, 2000);

        Assert.True(stats.HasData);
        Assert.Equal("2 КБ", stats.DownloadLabel.Replace('.', ','));
    }

    [Fact]
    public void Длительность_переключает_единицы()
    {
        var stats = new SessionStats();
        stats.Begin("Frankfurt");

        // только что началась - секунды
        Assert.EndsWith("с", stats.DurationLabel);
    }
}