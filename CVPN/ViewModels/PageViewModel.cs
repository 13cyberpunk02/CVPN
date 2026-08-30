using CVPN.Core;

namespace CVPN.ViewModels;

/// <summary>
/// Основа для вьюмодели страницы.
///
/// Страницы живут в одном экземпляре на всё приложение: раньше навигация
/// пересоздавала вьюху при каждом переходе, и состояние - прокрутка, введённый
/// текст, накопленные данные - терялось. Теперь пересоздаётся только разметка,
/// а состояние остаётся здесь.
/// </summary>
public abstract class PageViewModel : ObservableObject
{
    /// <summary>Страница показана. Здесь запускаются таймеры и подписки.</summary>
    public virtual void Activate() { }
 
    /// <summary>Со страницы ушли. Всё, что тикает в фоне, надо остановить.</summary>
    public virtual void Deactivate() { }
}
