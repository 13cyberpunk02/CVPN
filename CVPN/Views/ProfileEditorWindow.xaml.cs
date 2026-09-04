using System.Windows;
using CVPN.Localization;
using CVPN.Models;
using CVPN.Models.Enums;

namespace CVPN.Views;

public partial class ProfileEditorWindow : Window
{
    private readonly ServerProfile _draft;

    /// <summary>
    /// Правки идут в копию: если пользователь передумает, исходный профиль
    /// останется нетронутым.
    /// </summary>
    public ProfileEditorWindow(ServerProfile? existing = null)
    {
        InitializeComponent();

        _draft = Clone(existing);
        DataContext = _draft;

        if (existing is not null) HeaderText.Text = Loc.T("Editor_Edit");

        ProtocolBox.SelectedIndex = (int)_draft.Protocol;
        ApplyProtocolFields();
    }

    /// <summary>Заполненный профиль. Валиден только когда DialogResult равен true.</summary>
    public ServerProfile Result => _draft;

    private static ServerProfile Clone(ServerProfile? p) => p is null
        ? new ServerProfile { Port = 443, Flow = "xtls-rprx-vision", Path = "/" }
        : new ServerProfile
        {
            Name = p.Name,
            Host = p.Host,
            Port = p.Port,
            Protocol = p.Protocol,
            Uuid = p.Uuid,
            Password = p.Password,
            Sni = p.Sni,
            PublicKey = p.PublicKey,
            ShortId = p.ShortId,
            Flow = p.Flow,
            Path = p.Path,
            Username = p.Username,
            CountryCode = p.CountryCode
        };

    private void OnProtocolChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ProtocolBox.SelectedIndex < 0) return;

        _draft.Protocol = (ProtocolKind)ProtocolBox.SelectedIndex;
        ApplyProtocolFields();
    }

    /// <summary>
    /// Каждый протокол требует своего набора полей. Показывать все сразу -
    /// значит заставлять гадать, какие из них обязательны.
    /// </summary>
    private void ApplyProtocolFields()
    {
        if (VlessGroup is null) return;

        var kind = _draft.Protocol;

        Show(VlessGroup, kind is ProtocolKind.VlessReality or ProtocolKind.VlessWs);
        Show(RealityGroup, kind is ProtocolKind.VlessReality);
        Show(WsGroup, kind is ProtocolKind.VlessWs);
        Show(UserGroup, kind is ProtocolKind.Naive);
        Show(PasswordGroup, kind is ProtocolKind.AnyTls or ProtocolKind.Naive);
    }

    private static void Show(UIElement element, bool visible) =>
        element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!Validate(out var error))
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        if (string.IsNullOrWhiteSpace(_draft.Sni)) _draft.Sni = _draft.Host;
        if (string.IsNullOrWhiteSpace(_draft.Name)) _draft.Name = _draft.Host;

        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>Проверяем до сохранения: ядро откажет позже и куда менее внятно.</summary>
    private bool Validate(out string error)
    {
        error = "";

        if (string.IsNullOrWhiteSpace(_draft.Host))
            error = Loc.T("Editor_NeedHost");
        else if (_draft.Port is < 1 or > 65535)
            error = Loc.T("Editor_BadPort");
        else
            error = _draft.Protocol switch
            {
                ProtocolKind.VlessReality or ProtocolKind.VlessWs when !Guid.TryParse(_draft.Uuid, out _) => Loc.T(
                    "Editor_BadUuid"),
                ProtocolKind.VlessReality when string.IsNullOrWhiteSpace(_draft.PublicKey) => Loc.T(
                    "Editor_NeedPublicKey"),
                ProtocolKind.AnyTls when string.IsNullOrWhiteSpace(_draft.Password) => Loc.T("Editor_NeedPassword"),
                ProtocolKind.Naive when (string.IsNullOrWhiteSpace(_draft.Username) ||
                                         string.IsNullOrWhiteSpace(_draft.Password)) => Loc.T("Editor_NeedCredentials"),
                _ => error
            };

        return error.Length == 0;
    }
}