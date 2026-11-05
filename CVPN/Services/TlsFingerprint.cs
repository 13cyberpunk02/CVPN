namespace CVPN.Services;

/// <summary>
/// Отпечаток TLS (uTLS): как выглядит ClientHello клиента для стороннего наблюдателя.
///
/// Раньше всем VLESS-профилям жёстко ставился chrome, а значение fp из ссылки
/// терялось. Теперь отпечаток хранится в профиле: берётся из ссылки или
/// импортированного конфига и меняется в редакторе.
/// </summary>
public static class TlsFingerprint
{
    /// <summary>То, что подставляется VLESS-профилю без явного значения - прежнее поведение.</summary>
    public const string VlessDefault = "chrome";

    /// <summary>
    /// Значения, которые принимает sing-box. random выбирает один из реальных
    /// браузеров при запуске ядра, randomized генерирует случайный ClientHello.
    /// </summary>
    public static readonly IReadOnlyList<string> Known =
        ["chrome", "firefox", "safari", "edge", "ios", "android", "360", "qq", "random", "randomized"];

    /// <summary>
    /// Приводит значение из ссылки или поля к виду, понятному ядру.
    /// Неизвестное или пустое - пустая строка: «использовать значение по умолчанию».
    /// Неизвестное не передаём ядру как есть: оно отвергнет весь конфиг.
    /// </summary>
    public static string Normalize(string? value)
    {
        var v = (value ?? "").Trim().ToLowerInvariant();
        return Known.Contains(v) ? v : "";
    }

    /// <summary>Отпечаток для VLESS: свой или chrome, если профиль ничего не задал.</summary>
    public static string ForVless(string? value) =>
        Normalize(value) is { Length: > 0 } v ? v : VlessDefault;
}