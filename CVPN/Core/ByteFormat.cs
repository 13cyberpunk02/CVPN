using CVPN.Localization;

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
        < (long)Kb => Loc.T("Unit_B", bytes),
        < (long)Mb => Loc.T("Unit_KB", (bytes / Kb).ToString("0.#")),
        < (long)Gb => Loc.T("Unit_MB", (bytes / Mb).ToString("0.#")),
        _ => Loc.T("Unit_GB", (bytes / Gb).ToString("0.##"))
    };

    /// <summary>Скорость: значение и единица отдельно, чтобы подписать мелким шрифтом.</summary>
    public static (string Value, string Unit) Rate(long bytesPerSecond) =>
        bytesPerSecond >= Mb
            ? ((bytesPerSecond / Mb).ToString("0.0"), Loc.T("Unit_MBps"))
            : ((bytesPerSecond / Kb).ToString("0.0"), Loc.T("Unit_KBps"));
}