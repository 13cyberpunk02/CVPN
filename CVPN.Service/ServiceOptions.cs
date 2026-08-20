namespace CVPN.Service;

/// <summary>Пути, с которыми работает служба. Задаются один раз при старте.</summary>
/// <param name="CorePath">Полный путь к sing-box.exe.</param>
/// <param name="DataDir">Каталог службы: сюда пишутся конфиг, кэш и наборы правил.</param>
public sealed record ServiceOptions(string CorePath, string DataDir);
