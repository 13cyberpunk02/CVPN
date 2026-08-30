using System.IO;
using CVPN.Shared;

namespace CVPN.Tests;

public class FileLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "cvpn-log-tests-" + Guid.NewGuid().ToString("N")[..8]);

    private FileLog CreateLog()
    {
        var log = new FileLog(_dir);
        log.Prepare();

        return log;
    }

    /// <summary>Каталог задаётся снаружи: класс общий для приложения и службы.</summary>
    [Fact]
    public void Инициализация_переключает_общий_экземпляр()
    {
        FileLog.Initialize(_dir);

        Assert.Equal(_dir, FileLog.Current.Directory);
    }

    [Fact]
    public void Строка_попадает_в_файл_с_отметкой_времени()
    {
        var log = CreateLog();
        log.Write("подключение к Frankfurt");

        var text = File.ReadAllText(log.CurrentFile);

        Assert.Contains("подключение к Frankfurt", text);
        Assert.Matches(@"^\d{2}:\d{2}:\d{2}\.\d{3}\s", text);
    }

    [Fact]
    public void Имя_файла_содержит_дату()
    {
        var log = CreateLog();

        Assert.Contains(DateTime.Now.ToString("yyyy-MM-dd"), Path.GetFileName(log.CurrentFile));
    }

    [Fact]
    public void Пустые_строки_игнорируются()
    {
        var log = CreateLog();
        log.Write("   ");
        log.Write("");

        Assert.False(File.Exists(log.CurrentFile));
    }

    [Fact]
    public void Блок_пишется_целиком_с_разделителями()
    {
        var log = CreateLog();
        log.WriteBlock("Ошибка в интерфейсе", "System.InvalidOperationException\n  в методе Foo");

        var text = File.ReadAllText(log.CurrentFile);

        Assert.Contains("Ошибка в интерфейсе", text);
        Assert.Contains("System.InvalidOperationException", text);
        Assert.Contains("в методе Foo", text);
    }

    /// <summary>Файлы старше недели не должны накапливаться годами.</summary>
    [Fact]
    public void Старые_файлы_удаляются_при_подготовке()
    {
        Directory.CreateDirectory(_dir);

        var stale = Path.Combine(_dir, "cvpn-2020-01-01.log");
        File.WriteAllText(stale, "старьё");
        File.SetLastWriteTime(stale, DateTime.Now.AddDays(-30));

        var fresh = Path.Combine(_dir, "cvpn-9999-01-01.log");
        File.WriteAllText(fresh, "свежее");

        CreateLog();

        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(fresh));
    }

    /// <summary>Сломанное логирование не имеет права ронять приложение.</summary>
    [Fact]
    public void Недоступный_каталог_не_приводит_к_исключению()
    {
        var log = new FileLog(@"Z:\нет\такого\пути");

        log.Prepare();
        log.Write("строка");
        log.WriteBlock("заголовок", "тело");
    }

    [Fact]
    public void Запись_из_нескольких_потоков_не_теряет_строк()
    {
        var log = CreateLog();

        Parallel.For(0, 200, i => log.Write($"строка {i}"));

        var lines = File.ReadAllLines(log.CurrentFile);

        Assert.Equal(200, lines.Length);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            /* временный каталог */
        }
    }
}