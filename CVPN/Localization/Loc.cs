using System.Globalization;
using System.Resources;
using System.Windows.Markup;

namespace CVPN.Localization;

/// <summary>
/// Доступ к переводам. Ключи - строки, а не сгенерированные свойства: файл
/// Strings.Designer.cs создаёт Visual Studio, и проект переставал бы собираться
/// одной консольной командой. Опечатки в ключах ловит тест, который сверяет
/// всё используемое с содержимым ресурсов.
/// </summary>
public static class Loc
{
    private static readonly ResourceManager Manager =
        new("CVPN.Localization.Strings", typeof(Loc).Assembly);

    /// <summary>Язык интерфейса. Смена требует перезапуска приложения.</summary>
    public static CultureInfo Culture { get; private set; } = CultureInfo.CurrentUICulture;

    /// <summary>
    /// Языки, на которые переведён интерфейс.
    ///
    /// Это record, а не кортеж: у ValueTuple поля Code и Name - именно поля,
    /// а привязки WPF читают только свойства. С кортежем список выглядел бы
    /// пустым, а выбор не записывался бы в настройку.
    /// </summary>
    public static IReadOnlyList<LanguageOption> Available =>
    [
        new("", "Системный / System"),
        new("en", "English"),
        new("ru", "Русский")
    ];

    /// <summary>
    /// Применяет язык из настроек. Пустая строка означает язык системы.
    /// Вызывается один раз при запуске, до создания окна.
    /// </summary>
    public static void Apply(string? code)
    {
        Culture = string.IsNullOrWhiteSpace(code)
            ? CultureInfo.CurrentUICulture
            : new CultureInfo(code);

        CultureInfo.CurrentUICulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
    }

    /// <summary>
    /// Перевод по ключу. Если ключа нет, возвращает сам ключ в скобках -
    /// пропущенную строку так видно сразу, а приложение не падает.
    /// </summary>
    public static string T(string key)
    {
        try
        {
            return Manager.GetString(key, Culture) ?? $"[{key}]";
        }
        catch (Exception)
        {
            return $"[{key}]";
        }
    }

    /// <summary>Перевод с подстановкой: «Профиль «{0}» создан».</summary>
    public static string T(string key, params object?[] args)
    {
        var format = T(key);

        try
        {
            return string.Format(Culture, format, args);
        }
        catch (FormatException)
        {
            return format;
        }
    }
}

/// <summary>Язык интерфейса для выпадающего списка.</summary>
/// <param name="Code">Код культуры; пустая строка - язык системы.</param>
/// <param name="Name">Название для показа.</param>
public sealed record LanguageOption(string Code, string Name);

/// <summary>
/// Разметочное расширение: <c>Text="{loc:T Logs_Title}"</c>.
///
/// Язык фиксируется при запуске, поэтому значение вычисляется один раз -
/// без подписок и уведомлений. Смена языка на лету потребовала бы пересборки
/// всего визуального дерева и того не стоит.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension()
    {
    }

    public TExtension(string key) => Key = key;

    [ConstructorArgument("key")] public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);
}