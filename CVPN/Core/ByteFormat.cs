using CVPN.Localization;

namespace CVPN.Core;

/// <summary>Перевод байтов в читаемый вид. Одно место на всё приложение.</summary>
public static class ByteFormat
{
    private const double Kb = 1024;
    private const double Mb = Kb * 1024;
    private const double Gb = Mb * 1024;

    /// <summary>
    /// Объём: «5,2 МБ». Число форматируется по языку интерфейса, а не по языку
    /// системы: иначе на русской Windows с английским интерфейсом получается
    /// «1,5 MB» - разделитель один, единица другая.
    /// </summary>
    public static string Size(long bytes) => bytes switch
    {
        < 0 => "-",
        < (long)Kb => Loc.T("Unit_B", bytes),
        < (long)Mb => Loc.T("Unit_KB", Number(bytes / Kb, "0.#")),
        < (long)Gb => Loc.T("Unit_MB", Number(bytes / Mb, "0.#")),
        _ => Loc.T("Unit_GB", Number(bytes / Gb, "0.##"))
    };

    /// <summary>Скорость: значение и единица отдельно, чтобы подписать мелким шрифтом.</summary>
    public static (string Value, string Unit) Rate(long bytesPerSecond) =>
        bytesPerSecond >= Mb
            ? (Number(bytesPerSecond / Mb, "0.0"), Loc.T("Unit_MBps"))
            : (Number(bytesPerSecond / Kb, "0.0"), Loc.T("Unit_KBps"));

    private static string Number(double value, string format) => value.ToString(format, Loc.Culture);
}