using System.ComponentModel;
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
    protected PageViewModel(MainViewModel shell)
    {
        Shell = shell;

        // Строку состояния показывают все страницы, поэтому подписка тут,
        // а не в каждой по отдельности
        shell.PropertyChanged += OnShellChanged;
    }

    /// <summary>
    /// Оболочка. Временный мостик: когда все страницы переедут, общее состояние
    /// вынесется в отдельный сервис, и зависимость станет узкой.
    /// </summary>
    protected MainViewModel Shell { get; }

    /// <summary>Общая строка состояния - сообщения там ждут глазами.</summary>
    public string Status => Shell.Status;

    /// <summary>Страница показана. Здесь запускаются таймеры и подписки.</summary>
    public virtual void Activate()
    {
    }

    /// <summary>Со страницы ушли. Всё, что тикает в фоне, надо остановить.</summary>
    public virtual void Deactivate()
    {
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Status)) Raise(nameof(Status));
    }
}