using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using CVPN.Shared;

namespace CVPN.Service;

/// <summary>
/// Именованный канал, через который приложение управляет туннелем.
///
/// Доступ открыт всем локальным пользователям - иначе клиенту снова понадобились
/// бы права администратора, и вся затея теряет смысл. Безопасность держится
/// на ConfigSanitizer: команд, кроме запуска и остановки, здесь нет,
/// а конфиг переписывается перед применением.
/// </summary>
public sealed class PipeServer(CoreRunner runner, string corePath, string dataDir)
{
    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var pipe = NamedPipeServerStreamAcl.Create(
                    IpcContract.PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    inBufferSize: 0,
                    outBufferSize: 0,
                    CreateSecurity());

                await pipe.WaitForConnectionAsync(ct);
                await HandleAsync(pipe, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                // Один сорвавшийся клиент не должен ронять службу
                await Task.Delay(200, ct);
            }
        }
    }

    private static PipeSecurity CreateSecurity()
    {
        var security = new PipeSecurity();

        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        return security;
    }

    private async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        var read = await pipe.ReadAsync(buffer, ct);
        if (read == 0) return;

        var request = JsonSerializer.Deserialize<IpcRequest>(
            Encoding.UTF8.GetString(buffer, 0, read), IpcContract.Json);

        var response = request is null
            ? new IpcResponse { Ok = false, Message = "Некорректный запрос" }
            : Execute(request);

        response.Running = runner.IsRunning;
        response.Log = runner.DrainLog();

        var payload = JsonSerializer.SerializeToUtf8Bytes(response, IpcContract.Json);

        await pipe.WriteAsync(payload, ct);
        await pipe.FlushAsync(ct);
    }

    private IpcResponse Execute(IpcRequest request)
    {
        switch (request.Command)
        {
            case IpcCommand.Ping:
                return new IpcResponse { Ok = true, Message = "Служба работает" };

            case IpcCommand.Status:
                return new IpcResponse { Ok = true };

            case IpcCommand.Stop:
                runner.Stop();
                return new IpcResponse { Ok = true, Message = "Остановлено" };

            case IpcCommand.Start:
                return Start(request.Config, request.RuleSets);

            default:
                return new IpcResponse { Ok = false, Message = "Неизвестная команда" };
        }
    }

    /// <summary>
    /// Раскладывает присланные наборы у себя. Из имени берётся только последний
    /// сегмент: путь от клиента доверия не заслуживает.
    /// </summary>
    private void SaveRuleSets(List<RuleSetFile> ruleSets)
    {
        if (ruleSets.Count == 0) return;

        var rulesDir = Path.Combine(dataDir, "rules");
        Directory.CreateDirectory(rulesDir);

        foreach (var file in ruleSets)
        {
            try
            {
                var name = Path.GetFileName(file.Name);

                if (name.Length == 0 || !name.EndsWith(".srs", StringComparison.OrdinalIgnoreCase))
                    continue;

                File.WriteAllBytes(Path.Combine(rulesDir, name), Convert.FromBase64String(file.Content));
            }
            catch (Exception)
            {
                // Битый файл просто не появится, и набор будет отброшен санитайзером
            }
        }
    }

    private IpcResponse Start(string? config, List<RuleSetFile> ruleSets)
    {
        if (string.IsNullOrWhiteSpace(config))
            return new IpcResponse { Ok = false, Message = "Пустая конфигурация" };

        if (!File.Exists(corePath))
            return new IpcResponse { Ok = false, Message = $"Ядро не найдено: {corePath}" };

        try
        {
            // Каталог закрывается от обычных пользователей: в config.json
            // учётные данные лежат открытым текстом
            DataDirectory.Prepare(dataDir);

            SaveRuleSets(ruleSets);

            // Конфиг пишет служба и только в свой каталог: путь от клиента не принимается
            var configPath = Path.Combine(dataDir, "config.json");
            File.WriteAllText(configPath, ConfigSanitizer.Sanitize(config, dataDir));

            var error = runner.Start(corePath, configPath);

            return error.Length == 0
                ? new IpcResponse { Ok = true, Message = "Запущено" }
                : new IpcResponse { Ok = false, Message = error };
        }
        catch (Exception ex)
        {
            return new IpcResponse { Ok = false, Message = ex.Message };
        }
    }
}