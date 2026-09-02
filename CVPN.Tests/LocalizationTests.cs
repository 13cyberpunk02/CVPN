using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CVPN.Localization;

namespace CVPN.Tests;

/// <summary>
/// Ключи переводов - строки, поэтому опечатку не поймает компилятор.
/// Эти проверки заменяют его: сверяют всё используемое с содержимым ресурсов.
/// </summary>
public class LocalizationTests
{
    /// <summary>Поднимаемся от папки с тестами к корню решения.</summary>
    private static string ProjectRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
 
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "CVPN", "Localization")))
                dir = dir.Parent;
 
            return dir?.FullName ?? throw new DirectoryNotFoundException("не найден корень решения");
        }
    }
 
    private static string ResourcePath(string name) =>
        Path.Combine(ProjectRoot, "CVPN", "Localization", name);
 
    private static HashSet<string> KeysIn(string file) =>
        XDocument.Load(ResourcePath(file))
            .Root!.Elements("data")
            .Select(d => d.Attribute("name")!.Value)
            .ToHashSet(StringComparer.Ordinal);
 
    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(ProjectRoot, "CVPN"), "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs") || f.EndsWith(".xaml"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
 
    /// <summary>Ключи, которые действительно используются в разметке и коде.</summary>
    private static HashSet<string> UsedKeys()
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
 
        foreach (var file in SourceFiles())
        {
            var text = File.ReadAllText(file);
 
            foreach (Match m in Regex.Matches(text, @"\{loc:T\s+(\w+)\}"))
                used.Add(m.Groups[1].Value);
 
            foreach (Match m in Regex.Matches(text, @"Loc\.T\(""(\w+)"""))
                used.Add(m.Groups[1].Value);
        }
 
        return used;
    }
 
    [Fact]
    public void Все_используемые_ключи_есть_в_ресурсах()
    {
        var declared = KeysIn("Strings.resx");
        var missing = UsedKeys().Except(declared).OrderBy(k => k).ToList();
 
        Assert.True(missing.Count == 0, $"нет в Strings.resx: {string.Join(", ", missing)}");
    }
 
    /// <summary>Пропущенный перевод показывается по-английски - это заметно не сразу.</summary>
    [Fact]
    public void Русский_перевод_покрывает_все_ключи()
    {
        var en = KeysIn("Strings.resx");
        var ru = KeysIn("Strings.ru.resx");
 
        var missing = en.Except(ru).OrderBy(k => k).ToList();
 
        Assert.True(missing.Count == 0, $"нет в Strings.ru.resx: {string.Join(", ", missing)}");
    }
 
    /// <summary>Лишний ключ в переводе означает, что английский отстал.</summary>
    [Fact]
    public void В_переводе_нет_ключей_которых_нет_в_основном_файле()
    {
        var en = KeysIn("Strings.resx");
        var ru = KeysIn("Strings.ru.resx");
 
        var extra = ru.Except(en).OrderBy(k => k).ToList();
 
        Assert.True(extra.Count == 0, $"лишние в Strings.ru.resx: {string.Join(", ", extra)}");
    }
 
    /// <summary>
    /// Подстановки должны совпадать: если в одном языке {0}, а в другом нет,
    /// строка потеряет данные или сломается при форматировании.
    /// </summary>
    [Fact]
    public void Плейсхолдеры_совпадают_в_обоих_языках()
    {
        var en = XDocument.Load(ResourcePath("Strings.resx")).Root!.Elements("data")
            .ToDictionary(d => d.Attribute("name")!.Value, d => d.Element("value")!.Value);
 
        var ru = XDocument.Load(ResourcePath("Strings.ru.resx")).Root!.Elements("data")
            .ToDictionary(d => d.Attribute("name")!.Value, d => d.Element("value")!.Value);
 
        foreach (var (key, value) in en)
        {
            if (!ru.TryGetValue(key, out var translated)) continue;
 
            var expected = Regex.Matches(value, @"\{(\d+)\}").Select(m => m.Value).ToHashSet();
            var actual = Regex.Matches(translated, @"\{(\d+)\}").Select(m => m.Value).ToHashSet();
 
            Assert.True(expected.SetEquals(actual),
                $"{key}: подстановки различаются - «{value}» и «{translated}»");
        }
    }
 
    [Fact]
    public void Отсутствующий_ключ_возвращает_себя_а_не_падает()
    {
        Assert.Equal("[Нет_такого_ключа]", Loc.T("Нет_такого_ключа"));
    }
 
    [Fact]
    public void Подстановка_работает()
    {
        Loc.Apply("en");
 
        Assert.Equal("Copied 42 lines", Loc.T("Logs_CopiedLines", 42));
    }
 
    [Fact]
    public void Русский_перевод_подхватывается()
    {
        Loc.Apply("ru");
 
        Assert.Equal("Логи", Loc.T("Logs_Title"));
    }
}
