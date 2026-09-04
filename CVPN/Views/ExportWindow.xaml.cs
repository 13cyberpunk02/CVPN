using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using QRCoder;


namespace CVPN.Views;

public partial class ExportWindow : Window
{
    private readonly string _payload;

    /// <param name="payload">Ссылка профиля либо подписка целиком.</param>
    /// <param name="title">Заголовок окна.</param>
    /// <param name="subtitle">Пояснение под заголовком.</param>
    public ExportWindow(string payload, string title, string subtitle)
    {
        InitializeComponent();

        _payload = payload;

        HeaderText.Text = title;
        SubtitleText.Text = subtitle;
        LinkBox.Text = payload;

        QrImage.Source = Render(payload);
    }

    /// <summary>
    /// PngByteQRCode отдаёт готовый PNG и не тянет System.Drawing - в отличие
    /// от классического QRCode-рендерера из той же библиотеки.
    /// </summary>
    private static BitmapImage? Render(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;

        try
        {
            using var generator = new QRCodeGenerator();
            // Уровень Q переживает до 25% повреждений - запас на переснятое фото экрана
            using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
            using var png = new PngByteQRCode(data);

            var bytes = png.GetGraphic(10);

            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = new MemoryStream(bytes);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();

            return image;
        }
        catch (Exception)
        {
            // Слишком длинная подписка может не поместиться в QR - тогда остаётся текст
            return null;
        }
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(_payload, true);
                CopyButton.Content = "Скопировано";
                return;
            }
            catch (Exception) when (attempt < 5)
            {
                Thread.Sleep(100);
            }
            catch (Exception)
            {
                CopyButton.Content = "Не удалось скопировать";
            }
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}