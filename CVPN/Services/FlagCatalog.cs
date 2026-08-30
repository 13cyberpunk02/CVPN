using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CVPN.Services;

/// <summary>
/// Загрузка флагов из ресурсов. Раньше путь отдавался строкой, и если файл
/// не находился, WPF молча ничего не рисовал. Здесь ошибка сохраняется,
/// а результат кэшируется: одна и та же страна грузится один раз.
/// </summary>
public static class FlagCatalog
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Коды, для которых загрузка не удалась. Пусто - значит всё на месте.</summary>
    public static IReadOnlyCollection<string> Missing => MissingCodes;

    private static readonly HashSet<string> MissingCodes = new(StringComparer.OrdinalIgnoreCase);

    public static string? LastError { get; private set; }

    public static ImageSource? Get(string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Length != 2) return null;

        var code = countryCode.ToLowerInvariant();

        if (Cache.TryGetValue(code, out var cached)) return cached;

        ImageSource? image = null;

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri($"pack://application:,,,/Assets/Flags/{code}.png", UriKind.Absolute);
            // OnLoad освобождает поток сразу, иначе файл остаётся заблокированным
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            image = bitmap;
        }
        catch (Exception ex)
        {
            MissingCodes.Add(code);
            LastError = $"{code}.png - {ex.Message}";
        }

        Cache[code] = image;
        return image;
    }
}