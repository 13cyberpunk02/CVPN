using System.Windows;
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

        if (existing is not null) HeaderText.Text = "Изменение профиля";

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
            error = "Укажите адрес сервера";
        else if (_draft.Port is < 1 or > 65535)
            error = "Порт должен быть числом от 1 до 65535";
        else if (_draft.Protocol is ProtocolKind.VlessReality or ProtocolKind.VlessWs
                 && !Guid.TryParse(_draft.Uuid, out _))
            error = "UUID указан неверно: ожидается вид 8f1ce66e-719d-48b8-9ee6-804b52887082";
        else if (_draft.Protocol is ProtocolKind.VlessReality && string.IsNullOrWhiteSpace(_draft.PublicKey))
            error = "Для Reality нужен public key";
        else if (_draft.Protocol is ProtocolKind.AnyTls && string.IsNullOrWhiteSpace(_draft.Password))
            error = "Укажите пароль";
        else if (_draft.Protocol is ProtocolKind.Naive
                 && (string.IsNullOrWhiteSpace(_draft.Username) || string.IsNullOrWhiteSpace(_draft.Password)))
            error = "Для NaiveProxy нужны имя пользователя и пароль";

        return error.Length == 0;
    }
}