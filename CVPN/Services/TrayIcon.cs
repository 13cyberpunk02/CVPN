using System.Drawing;
using System.Windows.Forms;
using CVPN.Models.Enums;

using Application = System.Windows.Application;

namespace CVPN.Services;

/// <summary>
/// Значок в области уведомлений. В WPF своего нет, поэтому берём NotifyIcon
/// из WinForms — это дешевле, чем реализовывать Shell_NotifyIcon вручную.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly Icon _idle;
    private readonly Icon _connected;
    private readonly Icon _failing;

    public TrayIcon()
    {
        _idle = Load("cvpn-idle.ico");
        _connected = Load("cvpn-connected.ico");
        _failing = Load("cvpn.ico");

        _toggleItem = new ToolStripMenuItem("Подключить", null, (_, _) => ToggleRequested?.Invoke());

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Открыть CVPN", null, (_, _) => ShowRequested?.Invoke()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_toggleItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Выход", null, (_, _) => ExitRequested?.Invoke()));

        _icon = new NotifyIcon
        {
            Icon = _idle,
            Text = "CVPN",
            Visible = true,
            ContextMenuStrip = menu
        };

        _icon.DoubleClick += (_, _) => ShowRequested?.Invoke();
    }

    public event Action? ShowRequested;
    public event Action? ToggleRequested;
    public event Action? ExitRequested;

    /// <summary>Иконка и подсказка отражают состояние — свёрнутое окно тоже должно быть информативным.</summary>
    public void Update(TunnelState state, string? profileName)
    {
        _icon.Icon = state switch
        {
            TunnelState.Connected => _connected,
            TunnelState.Failing => _failing,
            _ => _idle
        };

        var status = state switch
        {
            TunnelState.Connected => "подключено",
            TunnelState.Connecting => "подключение",
            TunnelState.Failing => "ошибка",
            _ => "отключено"
        };

        var text = profileName is { Length: > 0 }
            ? $"CVPN — {status} · {profileName}"
            : $"CVPN — {status}";

        // Windows обрезает подсказку на 63 символах
        _icon.Text = text.Length > 63 ? text[..63] : text;

        _toggleItem.Text = state is TunnelState.Connected or TunnelState.Connecting
            ? "Отключить"
            : "Подключить";
    }

    public void Notify(string title, string message)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = message;
        _icon.ShowBalloonTip(3000);
    }

    private static Icon Load(string fileName)
    {
        var uri = new Uri($"pack://application:,,,/Assets/{fileName}", UriKind.Absolute);
        using var stream = Application.GetResourceStream(uri)?.Stream
                           ?? throw new InvalidOperationException($"Иконка {fileName} не найдена в ресурсах");

        return new Icon(stream);
    }

    public void Dispose()
    {
        // Без явного скрытия значок остаётся висеть в трее до наведения мышью
        _icon.Visible = false;
        _icon.Dispose();
        _idle.Dispose();
        _connected.Dispose();
        _failing.Dispose();
    }
}