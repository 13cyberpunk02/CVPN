using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CVPN.Models;
using CVPN.Models.Enums;

namespace CVPN.Services;

public static class ProfileStore
{
    /// <summary>Заполняется при загрузке, если часть секретов не расшифровалась.</summary>
    public static string LastLoadWarning { get; private set; } = "";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static StoredState Load()
    {
        try
        {
            if (!File.Exists(AppPaths.ProfilesFile)) return Seed();

            Core.Secret.ResetFailures();

            var json = File.ReadAllText(AppPaths.ProfilesFile);
            var state = JsonSerializer.Deserialize<StoredState>(json, Options) ?? Seed();
            state.Migrate();

            // Файл перенесли с другой машины или из другой учётной записи:
            // DPAPI привязан к пользователю, и секреты стали нечитаемыми
            if (Core.Secret.FailureCount > 0)
                LastLoadWarning = $"Не удалось расшифровать значений: {Core.Secret.FailureCount}. " +
                                  "Учётные данные придётся ввести заново.";

            return state;
        }
        catch
        {
            // Битый файл не должен мешать запуску: начинаем с набора по умолчанию
            return Seed();
        }
    }

    public static void Save(StoredState state)
    {
        AppPaths.EnsureCreated();
        File.WriteAllText(AppPaths.ProfilesFile, JsonSerializer.Serialize(state, Options));
    }

    /// <summary>Набор правил, с которого разумно начинать: реклама в блок, частные адреса напрямую.</summary>
    private static StoredState Seed()
    {
        var state = new StoredState
        {
            RoutingProfiles =
            [
                new RoutingProfile
                {
                    Name = "Основной",
                    Rules =
                    [
                        new RouteRule { Match = MatchKind.Geosite, Value = "category-ads-all", Action = RouteAction.Block },
                        new RouteRule { Match = MatchKind.Geoip, Value = "ru", Action = RouteAction.Direct }
                    ]
                }
            ]
        };

        state.Migrate();
        return state;
    }
}