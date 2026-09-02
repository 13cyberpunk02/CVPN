namespace CVPN.Core;

/// <summary>Перевод байтов в читаемый вид. Одно место на всё приложение.</summary>
public static class ByteFormat
{
    private const double Kb = 1024;
    private const double Mb = Kb * 1024;
    private const double Gb = Mb * 1024;

    /// <summary>Объём: «5,2 МБ».</summary>
    public static string Size(long bytes) => bytes switch
    {
        < 0 => "-",
        < (long)Kb => $"{bytes} Б",
        < (long)Mb => $"{bytes / Kb:0.#} КБ",
        < (long)Gb => $"{bytes / Mb:0.#} МБ",
        _ => $"{bytes / Gb:0.##} ГБ"
    };

    /// <summary>Скорость: значение и единица отдельно, чтобы подписать мелким шрифтом.</summary>
    public static (string Value, string Unit) Rate(long bytesPerSecond) =>
        bytesPerSecond >= Mb
            ? ((bytesPerSecond / Mb).ToString("0.0"), "МБ/с")
            : ((bytesPerSecond / Kb).ToString("0.0"), "КБ/с");
}