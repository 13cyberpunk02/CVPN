using System.Net.Http;
using System.Text;
using CVPN.Models;

namespace CVPN.Services;

/// <summary>
/// Загрузка списка серверов по ссылке подписки. Формат почти везде одинаковый:
/// список ссылок построчно, целиком закодированный в base64. Встречается и без
/// кодирования, поэтому проверяются оба варианта.
/// </summary>
public static class SubscriptionService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static async Task<(List<ServerProfile> Profiles, string Error)> FetchAsync(
        string url, CancellationToken ct = default)
    {
        var profiles = new List<ServerProfile>();

        if (string.IsNullOrWhiteSpace(url)) return (profiles, "Ссылка подписки не указана");

        string body;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            // Некоторые панели отдают разный формат в зависимости от клиента
            request.Headers.TryAddWithoutValidation("User-Agent", "CVPN/1.0 (sing-box)");

            using var response = await Http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return (profiles, $"Сервер подписки ответил {(int)response.StatusCode}");

            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            return (profiles, $"Не удалось загрузить подписку: {ex.Message}");
        }

        foreach (var line in Decode(body).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (LinkParser.TryParse(line, out var profile, out _))
            {
                profile.Subscription = url;
                profiles.Add(profile);
            }
        }

        return profiles.Count > 0
            ? (profiles, "")
            : (profiles, "В подписке не нашлось поддерживаемых серверов");
    }

    /// <summary>Если тело - base64, разворачиваем; иначе возвращаем как есть.</summary>
    private static string Decode(string body)
    {
        var trimmed = body.Trim();

        // Ссылки в открытом виде начинаются со схемы - декодировать нечего
        if (trimmed.Contains("://", StringComparison.Ordinal)) return trimmed;

        try
        {
            var padded = trimmed.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');

            return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        }
        catch (FormatException)
        {
            return trimmed;
        }
    }
}