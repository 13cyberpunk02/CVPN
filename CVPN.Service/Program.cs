using CVPN.Service;

var dataDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CVPN");

var corePath = new[]
                   {
                       Path.Combine(AppContext.BaseDirectory, "core", "sing-box.exe"),
                       Path.Combine(AppContext.BaseDirectory, "..", "core", "sing-box.exe")
                   }
                   .Select(Path.GetFullPath)
                   .FirstOrDefault(File.Exists)
               ?? Path.Combine(AppContext.BaseDirectory, "core", "sing-box.exe");

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options => options.ServiceName = "CVPNTunnel");
builder.Services.AddSingleton<CoreRunner>();
builder.Services.AddSingleton(new ServiceOptions(corePath, dataDir));
builder.Services.AddHostedService<TunnelWorker>();

await builder.Build().RunAsync();