using System.Drawing;
using System.Windows.Forms;
using CVPN.Localization;
using CVPN.Models.Enums;
using Application = System.Windows.Application;

namespace CVPN.Services;

/// <summary>
/// Значок в области уведомлений. В WPF своего нет, поэтому берём NotifyIcon
/// из WinForms - это дешевле, чем реализовывать Shell_NotifyIcon вручную.
/// </summary>
/// <summary>
/// Значок в области уведомлений. В WPF своего нет, поэтому берём NotifyIcon
/// из WinForms - это дешевле, чем реализовывать Shell_NotifyIcon вручную.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly ToolStripMenuItem _serversItem;
    private readonly Icon _idle;
    private readonly Icon _connected;
    private readonly Icon _failing;

    public TrayIcon()
    {
        _idle = Load("cvpn-idle.ico");
        _connected = Load("cvpn-connected.ico");
        _failing = Load("cvpn.ico");

        _toggleItem = new ToolStripMenuItem(Loc.T("Action_Connect"), null, (_, _) => ToggleRequested?.Invoke());

        // Сервер меняют чаще, чем открывают окно, - список прямо в меню
        _serversItem = new ToolStripMenuItem(Loc.T("Tray_Server")) { Enabled = false };

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem(Loc.T("Tray_Open"), null, (_, _) => ShowRequested?.Invoke()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_toggleItem);
        menu.Items.Add(_serversItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(Loc.T("Tray_Exit"), null, (_, _) => ExitRequested?.Invoke()));

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

    /// <summary>Из меню выбрали сервер - передаётся его название.</summary>
    public event Action<string>? ProfileRequested;

    /// <summary>
    /// Перестраивает список серверов. Вызывается при изменении профилей
    /// и при смене активного: пункты меню WinForms не умеют привязок.
    /// </summary>
    public void SetProfiles(IEnumerable<(string Name, bool IsActive)> profiles)
    {
        _serversItem.DropDownItems.Clear();

        var any = false;

        foreach (var (name, isActive) in profiles)
        {
            var item = new ToolStripMenuItem(name) { Checked = isActive, CheckOnClick = false };

            // Замыкание по name, а не по item: список пересоздаётся целиком
            var chosen = name;
            item.Click += (_, _) => ProfileRequested?.Invoke(chosen);

            _serversItem.DropDownItems.Add(item);
            any = true;
        }

        // Пустое подменю выглядит как поломка - лучше показать его недоступным
        _serversItem.Enabled = any;
    }

    /// <summary>Иконка и подсказка отражают состояние - свёрнутое окно тоже должно быть информативным.</summary>
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
            TunnelState.Connected => Loc.T("State_Connected"),
            TunnelState.Connecting => Loc.T("State_Connecting"),
            TunnelState.Failing => Loc.T("State_Failed"),
            _ => Loc.T("State_Disconnected")
        };

        var text = profileName is { Length: > 0 }
            ? $"CVPN - {status} · {profileName}"
            : $"CVPN - {status}";

        // Windows обрезает подсказку на 63 символах
        _icon.Text = text.Length > 63 ? text[..63] : text;

        _toggleItem.Text = state is TunnelState.Connected or TunnelState.Connecting
            ? Loc.T("Action_Disconnect")
            : Loc.T("Action_Connect");
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
                           ?? throw new InvalidOperationException($"icon {fileName} not found in resources");

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