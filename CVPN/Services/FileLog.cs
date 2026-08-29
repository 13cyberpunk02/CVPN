using System.IO;
using System.Text;

namespace CVPN.Services;

/// <summary>
/// Лог в файл. Экран показывает последние 500 строк и очищается при закрытии
/// приложения - этого достаточно, пока разбираешь проблему сам, и бесполезно,
/// когда о ней сообщает пользователь.
///
/// Файл на день, старые удаляются. Ошибки записи проглатываются: сломанное
/// логирование не должно ронять приложение.
/// </summary>
public sealed class FileLog
{
    private const int KeepDays = 7;
    private const long MaxBytes = 10 * 1024 * 1024;
 
    private readonly string _directory;
    private readonly Lock _gate = new();
    private DateOnly _day;
 
    public FileLog(string directory)
    {
        _directory = directory;
        _day = DateOnly.FromDateTime(DateTime.Now);
    }
 
    /// <summary>Общий экземпляр приложения.</summary>
    public static FileLog Current { get; private set; } = new(Path.Combine(AppPaths.DataDir, "logs"));
 
    public string Directory => _directory;
 
    public string CurrentFile => Path.Combine(_directory, $"cvpn-{_day:yyyy-MM-dd}.log");
 
    /// <summary>Вызывается один раз при старте: создаёт каталог и подчищает старое.</summary>
    public static void Initialize()
    {
        Current = new FileLog(Path.Combine(AppPaths.DataDir, "logs"));
        Current.Prepare();
    }
 
    public void Prepare()
    {
        try
        {
            System.IO.Directory.CreateDirectory(_directory);
            RemoveOld();
        }
        catch (Exception)
        {
            // Каталог недоступен - работаем без файлового лога
        }
    }
 
    public void Write(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
 
        lock (_gate)
        {
            try
            {
                var today = DateOnly.FromDateTime(DateTime.Now);
 
                if (today != _day)
                {
                    _day = today;
                    RemoveOld();
                }
 
                System.IO.Directory.CreateDirectory(_directory);
 
                var path = CurrentFile;
 
                // Один зациклившийся источник не должен съесть диск
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                    File.Move(path, path + ".1", overwrite: true);
 
                File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff}  {line}{Environment.NewLine}", Encoding.UTF8);
            }
            catch (Exception)
            {
                // Занят другим процессом, нет места, нет прав - молча пропускаем
            }
        }
    }
 
    /// <summary>Многострочная запись: аварии удобнее читать одним блоком.</summary>
    public void WriteBlock(string title, string body)
    {
        var separator = new string('─', 60);
 
        Write(separator);
        Write(title);
 
        foreach (var line in body.Split('\n'))
            Write("  " + line.TrimEnd());
 
        Write(separator);
    }
 
    private void RemoveOld()
    {
        var threshold = DateTime.Now.AddDays(-KeepDays);
 
        foreach (var file in System.IO.Directory.GetFiles(_directory, "cvpn-*.log*"))
        {
            try
            {
                if (File.GetLastWriteTime(file) < threshold) File.Delete(file);
            }
            catch (Exception)
            {
                // Файл занят - попробуем в следующий раз
            }
        }
    }
}
