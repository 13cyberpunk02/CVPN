using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CVPN.Core;
using CVPN.Models;
using CVPN.Services;
using Microsoft.Win32;

namespace CVPN.ViewModels;

/// <summary>
/// Страница профилей: импорт, создание, экспорт, проверка серверов и подписка.
///
/// Сам список остаётся в оболочке - из него строится конфигурация. Здесь всё,
/// что с ним делают руками.
/// </summary>
public sealed class ProfilesViewModel : PageViewModel
{
    private string _linkText = "";
    private bool _busy;

    public ProfilesViewModel(MainViewModel shell) : base(shell)
    {
        ImportLink = new RelayCommand(Import);
        ImportFile = new RelayCommand(ImportFromFile);
        Create = new RelayCommand(() => Edit(null));

        EditProfileCommand = new RelayCommand(p =>
        {
            if (p is ServerProfile sp) Edit(sp);
        });
        RemoveProfile = new RelayCommand(p =>
        {
            if (p is ServerProfile sp) Delete(sp);
        });
        SelectProfile = new RelayCommand(p =>
        {
            if (p is ServerProfile sp) _ = shell.SelectServerAsync(sp);
        });
        ExportProfile = new RelayCommand(p =>
        {
            if (p is ServerProfile sp) ShowExport(sp);
        });

        ExportAll = new RelayCommand(ShowExportAll, () => Profiles.Count > 0);
        PingAll = new RelayCommand(async () => await PingAsync(), () => !IsBusy);
        UpdateSubscription = new RelayCommand(async () => await UpdateSubscriptionAsync(), () => !IsBusy);
    }

    public ObservableCollection<ServerProfile> Profiles => Shell.Profiles;

    public AppSettings Settings => Shell.Settings;

    /// <summary>Текст в поле импорта по ссылке.</summary>
    public string LinkText
    {
        get => _linkText;
        set => Set(ref _linkText, value);
    }

    /// <summary>Идёт долгая операция: проверка серверов или загрузка подписки.</summary>
    public bool IsBusy
    {
        get => _busy;
        private set => Set(ref _busy, value);
    }

    public ICommand ImportLink { get; }
    public ICommand ImportFile { get; }
    public ICommand Create { get; }
    public ICommand EditProfileCommand { get; }
    public ICommand RemoveProfile { get; }
    public ICommand SelectProfile { get; }
    public ICommand ExportProfile { get; }
    public ICommand ExportAll { get; }
    public ICommand PingAll { get; }
    public ICommand UpdateSubscription { get; }

    /// <summary>
    /// Обновление подписки при запуске - не чаще раза в сутки. Вызывается
    /// оболочкой: страница может не открыться ни разу за сеанс, а список
    /// серверов должен быть свежим.
    /// </summary>
    public async Task StartupRefreshAsync()
    {
        if (!Settings.AutoUpdateSubscription) return;
        if (string.IsNullOrWhiteSpace(Settings.SubscriptionUrl)) return;

        var last = Settings.SubscriptionUpdated;
        if (last is not null && DateTimeOffset.Now - last.Value < TimeSpan.FromDays(1)) return;

        // Пауза, чтобы не соперничать со стартом за сеть
        await Task.Delay(TimeSpan.FromSeconds(8));
        await UpdateSubscriptionAsync();
    }

    /// <summary>
    /// При открытии списка меряем непроверенные профили: прочерки вместо чисел
    /// читаются как поломка, а не как «данных пока нет».
    /// </summary>
    public override void Activate() => _ = EnsureLatencyAsync();

    // ===================== импорт =====================

    private void Import()
    {
        if (!LinkParser.TryParse(LinkText, out var profile, out var error))
        {
            Shell.Notify(error);
            return;
        }

        Profiles.Add(profile);
        Shell.Active ??= profile;
        LinkText = "";

        Shell.Notify($"Профиль «{profile.Name}» добавлен");
        Shell.Persist();
    }

    /// <summary>Принимает конфиг sing-box, массив outbound'ов или одиночный объект.</summary>
    private void ImportFromFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите конфигурацию sing-box",
            Filter = "Конфигурации (*.json)|*.json|Все файлы (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true) return;

        if (!ConfigImporter.TryImportFile(dialog.FileName, out var imported, out var error))
        {
            Shell.Notify(error);
            return;
        }

        foreach (var profile in imported) Profiles.Add(profile);

        Shell.Active ??= Profiles.FirstOrDefault();

        Shell.Notify(imported.Count == 1
            ? $"Профиль «{imported[0].Name}» добавлен"
            : $"Добавлено профилей: {imported.Count}");

        Shell.Persist();
    }

    // ===================== создание и правка =====================

    private void Edit(ServerProfile? existing)
    {
        var editor = new Views.ProfileEditorWindow(existing)
        {
            Owner = Application.Current?.MainWindow
        };

        if (editor.ShowDialog() != true) return;

        var result = editor.Result;

        if (existing is null)
        {
            Profiles.Add(result);
            Shell.Active ??= result;
            Shell.Notify($"Профиль «{result.Name}» создан");
        }
        else
        {
            // Заменяем на месте, чтобы не терять позицию в списке
            var index = Profiles.IndexOf(existing);
            if (index >= 0) Profiles[index] = result;

            if (ReferenceEquals(Shell.Active, existing)) Shell.Active = result;
            Shell.Notify($"Профиль «{result.Name}» обновлён");
        }

        Shell.Persist();
    }

    private void Delete(ServerProfile profile)
    {
        Profiles.Remove(profile);

        if (ReferenceEquals(Shell.Active, profile)) Shell.Active = Profiles.FirstOrDefault();

        Shell.Persist();
    }

    // ===================== экспорт =====================

    private void ShowExport(ServerProfile profile)
    {
        var link = ProfileLink.Build(profile);

        if (link.Length == 0)
        {
            Shell.Notify("Для этого протокола ссылка не поддерживается");
            return;
        }

        new Views.ExportWindow(link, profile.Name, $"{profile.ProtocolLabel} · {profile.Endpoint}")
        {
            Owner = Application.Current?.MainWindow
        }.ShowDialog();
    }

    /// <summary>Весь список одной строкой подписки - её можно скормить другому клиенту.</summary>
    private void ShowExportAll()
    {
        var payload = ProfileLink.BuildSubscription(Profiles);

        new Views.ExportWindow(payload, "Все профили",
            $"Список из {Profiles.Count} серверов в формате подписки (base64)")
        {
            Owner = Application.Current?.MainWindow
        }.ShowDialog();
    }

    // ===================== проверка и подписка =====================

    private async Task EnsureLatencyAsync()
    {
        if (IsBusy) return;
        if (Profiles.All(p => p.LatencyMs >= 0)) return;

        await PingAsync(onlyUnknown: true);
    }

    /// <summary>
    /// Замер идёт напрямую по TCP, а не через ядро: так это работает и без
    /// подключения, и сразу для всего списка.
    /// </summary>
    private async Task PingAsync(bool onlyUnknown = false)
    {
        var targets = onlyUnknown
            ? Profiles.Where(p => p.LatencyMs < 0).ToList()
            : Profiles.ToList();

        if (targets.Count == 0) return;

        IsBusy = true;
        Shell.Notify("Проверка серверов…");

        try
        {
            var probes = targets.Select(async profile =>
            {
                var ms = await LatencyProbe.MeasureAsync(profile.Host, profile.Port);
                MainViewModel.Dispatch(() => profile.LatencyMs = ms);
            });

            await Task.WhenAll(probes);

            var alive = targets.Count(p => p.LatencyMs >= 0);
            Shell.Notify($"Ответили {alive} из {targets.Count}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Обновляет профили из подписки. Заменяются только пришедшие из неё:
    /// созданные вручную остаются на месте.
    /// </summary>
    private async Task UpdateSubscriptionAsync()
    {
        IsBusy = true;
        Shell.Notify("Загрузка подписки…");

        try
        {
            var (fetched, error) = await SubscriptionService.FetchAsync(Settings.SubscriptionUrl);

            if (error.Length > 0)
            {
                Shell.Notify(error);
                return;
            }

            var activeName = Shell.Active?.Name;

            foreach (var stale in Profiles.Where(p => p.Subscription == Settings.SubscriptionUrl).ToList())
                Profiles.Remove(stale);

            foreach (var profile in fetched) Profiles.Add(profile);

            // Возвращаем выбор на сервер с тем же именем, если он ещё есть
            Shell.Active = Profiles.FirstOrDefault(p => p.Name == activeName) ?? Profiles.FirstOrDefault();

            Settings.SubscriptionUpdated = DateTimeOffset.Now;

            Shell.Notify($"Из подписки загружено серверов: {fetched.Count}");
            Shell.Persist();
        }
        finally
        {
            IsBusy = false;
        }
    }
}