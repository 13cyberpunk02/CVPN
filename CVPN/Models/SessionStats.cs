using System.Text.Json.Serialization;
using CVPN.Core;

namespace CVPN.Models;

/// <summary>
/// Итоги текущей сессии. Считаются из потока счётчиков Clash API, а не
/// запрашиваются отдельно: данные уже приходят раз в секунду.
/// </summary>
public sealed class SessionStats : ObservableObject
{
    private long _upload;
    private long _download;
    private long _peakUpload;
    private long _peakDownload;
    private DateTimeOffset? _started;
    private string _server = "";

    /// <summary>Когда сессия началась. null - сессии ещё не было.</summary>
    public DateTimeOffset? Started
    {
        get => _started;
        private set => Set(ref _started, value);
    }

    public string Server
    {
        get => _server;
        private set => Set(ref _server, value);
    }

    [JsonIgnore] public long Upload => _upload;

    [JsonIgnore] public long Download => _download;

    [JsonIgnore] public string UploadLabel => ByteFormat.Size(_upload);

    [JsonIgnore] public string DownloadLabel => ByteFormat.Size(_download);

    [JsonIgnore]
    public string PeakLabel
    {
        get
        {
            var (value, unit) = ByteFormat.Rate(Math.Max(_peakUpload, _peakDownload));
            return $"{value} {unit}";
        }
    }

    [JsonIgnore]
    public string DurationLabel
    {
        get
        {
            if (Started is null) return "-";

            var elapsed = DateTimeOffset.Now - Started.Value;

            return elapsed.TotalHours >= 1
                ? $"{(int)elapsed.TotalHours} ч {elapsed.Minutes} мин"
                : elapsed.TotalMinutes >= 1
                    ? $"{elapsed.Minutes} мин"
                    : $"{elapsed.Seconds} с";
        }
    }

    /// <summary>Есть что показывать.</summary>
    [JsonIgnore]
    public bool HasData => Started is not null;

    public void Begin(string server)
    {
        _upload = _download = _peakUpload = _peakDownload = 0;

        Server = server;
        Started = DateTimeOffset.Now;

        RaiseAll();
    }

    /// <summary>Байты за очередную секунду.</summary>
    public void Add(long upload, long download)
    {
        _upload += upload;
        _download += download;

        // Пик считаем по секундным отсчётам: он и есть скорость канала
        if (upload > _peakUpload) _peakUpload = upload;
        if (download > _peakDownload) _peakDownload = download;

        RaiseAll();
    }

    /// <summary>Итоги остаются после отключения: их для того и собирали.</summary>
    public void RaiseElapsed() => Raise(nameof(DurationLabel));

    private void RaiseAll()
    {
        Raise(nameof(UploadLabel));
        Raise(nameof(DownloadLabel));
        Raise(nameof(PeakLabel));
        Raise(nameof(DurationLabel));
        Raise(nameof(HasData));
    }
}