using System.Windows;
using CVPN.Localization;
using CVPN.Models;
using CVPN.Models.Enums;
using CVPN.Services;

namespace CVPN.Views;

public partial class ProfileEditorWindow : Window
{
    private static readonly string[] Modes = ["auto", "packet-up", "stream-up", "stream-one"];

    /// <summary>Первый пункт - «по умолчанию» (пустое значение), дальше то, что принимает ядро.</summary>
    private static readonly string[] Fingerprints = ["", .. TlsFingerprint.Known];

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
        ModeBox.SelectedIndex = Array.IndexOf(Modes, _draft.Mode) is var i && i >= 0 ? i : 0;

        // Подпись первого пункта зависит от протокола и ставится в ApplyProtocolFields
        foreach (var fp in Fingerprints) FingerprintBox.Items.Add(fp);
        FingerprintBox.SelectedIndex = Math.Max(0,
            Array.IndexOf(Fingerprints, TlsFingerprint.Normalize(_draft.Fingerprint)));

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
            HttpHost = p.HttpHost,
            Mode = p.Mode,
            Fingerprint = p.Fingerprint,
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

        Show(VlessGroup, kind is ProtocolKind.VlessReality or ProtocolKind.VlessWs or ProtocolKind.VlessXhttp);

        // Reality необязателен для XHTTP: он работает и поверх обычного TLS,
        // поэтому поля ключа показываем, но пустыми их оставить можно
        Show(RealityGroup, kind is ProtocolKind.VlessReality or ProtocolKind.VlessXhttp);

        // Naive идёт через Cronet: отпечаток там задаёт сам Chromium, uTLS не применяется
        Show(FingerprintGroup, kind is not ProtocolKind.Naive);
        RefreshDefaultFingerprintLabel();

        Show(WsGroup, kind is ProtocolKind.VlessWs);
        Show(XhttpGroup, kind is ProtocolKind.VlessXhttp);
        Show(UserGroup, kind is ProtocolKind.Naive);
        Show(PasswordGroup, kind is ProtocolKind.AnyTls or ProtocolKind.Naive);
    }

    private static void Show(UIElement element, bool visible) =>
        element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Что значит «по умолчанию», зависит от протокола: у VLESS - chrome,
    /// у AnyTLS - без uTLS, как было до появления этого поля.
    /// </summary>
    private void RefreshDefaultFingerprintLabel()
    {
        if (FingerprintBox.Items.Count == 0) return;

        var selected = FingerprintBox.SelectedIndex;

        FingerprintBox.Items[0] = _draft.Protocol == ProtocolKind.AnyTls
            ? Loc.T("Editor_FingerprintNone")
            : Loc.T("Editor_FingerprintDefault", TlsFingerprint.VlessDefault);

        FingerprintBox.SelectedIndex = selected;
    }

    private void OnFingerprintChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (FingerprintBox.SelectedIndex >= 0) _draft.Fingerprint = Fingerprints[FingerprintBox.SelectedIndex];
    }

    private void OnModeChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ModeBox.SelectedIndex >= 0) _draft.Mode = Modes[ModeBox.SelectedIndex];
    }

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
        else if (_draft.Protocol is ProtocolKind.VlessReality or ProtocolKind.VlessWs or ProtocolKind.VlessXhttp
                 && !Guid.TryParse(_draft.Uuid, out _))
            error = Loc.T("Editor_BadUuid");
        else if (_draft.Protocol is ProtocolKind.VlessReality && string.IsNullOrWhiteSpace(_draft.PublicKey))
            error = Loc.T("Editor_NeedPublicKey");
        else if (_draft.Protocol is ProtocolKind.AnyTls && string.IsNullOrWhiteSpace(_draft.Password))
            error = Loc.T("Editor_NeedPassword");
        else if (_draft.Protocol is ProtocolKind.Naive
                 && (string.IsNullOrWhiteSpace(_draft.Username) || string.IsNullOrWhiteSpace(_draft.Password)))
            error = Loc.T("Editor_NeedCredentials");

        return error.Length == 0;
    }
}