using System.IO;
using CVPN.Core;

namespace CVPN.Services;

/// <summary>Все пути приложения в одном месте, чтобы не плодить Path.Combine по коду.</summary>
public static class AppPaths
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CVPN");

    public static string ProfilesFile => Path.Combine(DataDir, "profiles.json");
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");

    /// <summary>Конфиг перегенерируется при каждом запуске — правим только в UI, не руками.</summary>
    public static string GeneratedConfig => Path.Combine(DataDir, "config.json");

    public static string CacheFile => Path.Combine(DataDir, "cache.db");

    public static string DefaultCorePath =>
        Path.Combine(AppContext.BaseDirectory, "core", "sing-box.exe");

    public static void EnsureCreated() => Directory.CreateDirectory(DataDir);
}