using CVPN.Shared;

namespace CVPN.Service;

/// <summary>
/// Держит именованный канал открытым всё время жизни службы.
/// Заменяет собой Worker.cs из шаблона — его можно удалить.
/// </summary>
public sealed class TunnelWorker(CoreRunner runner, ServiceOptions options, ILogger<TunnelWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        FileLog.Initialize(Path.Combine(options.DataDir, "logs"));
        FileLog.Current.Write($"=== служба запущена, ядро: {options.CorePath}");

        logger.LogInformation("Служба запущена. Ядро: {Core}", options.CorePath);

        // Права выставляем на старте, а не при первом запуске туннеля:
        // каталог мог остаться от прежней версии с открытым доступом
        DataDirectory.Prepare(options.DataDir);

        var server = new PipeServer(runner, options.CorePath, options.DataDir);
        await server.RunAsync(stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Туннель не должен пережить службу
        runner.Stop();

        FileLog.Current.Write("=== служба остановлена");
        logger.LogInformation("Служба остановлена");

        await base.StopAsync(cancellationToken);
    }
}