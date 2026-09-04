using CVPN.Localization;

namespace CVPN.Tests;

/// <summary>
/// Общая коллекция для тестов, зависящих от языка.
///
/// Loc.Culture - глобальное состояние, а xUnit по умолчанию гоняет классы
/// параллельно: один тест мог переключить язык под ногами у другого.
/// Классы в одной коллекции выполняются последовательно.
/// </summary>
[CollectionDefinition("Localization")]
public sealed class LocalizationCollection : ICollectionFixture<LocalizationFixture>;

/// <summary>
/// Фиксирует английский язык на время тестов.
///
/// Без этого результат зависел от языка машины: на русской Windows тесты
/// проходили, на англоязычном раннере GitHub - падали.
/// </summary>
public sealed class LocalizationFixture
{
    public LocalizationFixture() => Loc.Apply("en");
}