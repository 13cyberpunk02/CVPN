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
        logger.LogInformation("Служба запущена. Ядро: {Core}", options.CorePath);
 
        var server = new PipeServer(runner, options.CorePath, options.DataDir);
        await server.RunAsync(stoppingToken);
    }
 
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Туннель не должен пережить службу
        runner.Stop();
        logger.LogInformation("Служба остановлена");
 
        await base.StopAsync(cancellationToken);
    }
}
