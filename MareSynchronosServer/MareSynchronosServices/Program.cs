using MareSynchronosServices;
using MareSynchronosShared.Services;
using MareSynchronosShared.Utils.Configuration;
using Serilog;
using Serilog.Core;

public class Program
{
    public static void Main(string[] args)
    {
        var levelSwitch = new LoggingLevelSwitch();
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.Seq("http://localhost:5341", 
                apiKey: "wR6I02R95SNDUy3IXSHK",
                controlLevelSwitch: levelSwitch
                )
            .CreateLogger();
        var hostBuilder = CreateHostBuilder(args);
        var host = hostBuilder.Build();

        using (var scope = host.Services.CreateScope())
        {
            var options = host.Services.GetService<IConfigurationService<ServicesConfiguration>>();
            var optionsServer = host.Services.GetService<IConfigurationService<ServerConfiguration>>();
            var logger = host.Services.GetService<ILogger<Program>>();
            logger.LogInformation("Loaded MareSynchronos Services Configuration (IsMain: {isMain})", options.IsMain);
            logger.LogInformation(options.ToString());
            logger.LogInformation("Loaded MareSynchronos Server Configuration (IsMain: {isMain})", optionsServer.IsMain);
            logger.LogInformation(optionsServer.ToString());
        }

        host.Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseSystemd()
            .UseConsoleLifetime()
            .ConfigureAppConfiguration((ctx, config) =>
            {
                var appSettingsPath = Environment.GetEnvironmentVariable("APPSETTINGS_PATH");
                if (!string.IsNullOrEmpty(appSettingsPath))
                {
                    config.AddJsonFile(appSettingsPath, optional: true, reloadOnChange: true);
                }
                else
                {
                    if (string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Development", StringComparison.Ordinal))
                    {
                        config.AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true);
                    }
                    else
                    {
                        config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                    }
                   
                }

                config.AddEnvironmentVariables();
            })
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseContentRoot(AppContext.BaseDirectory);
                webBuilder.ConfigureLogging((ctx, builder) =>
                {
                    builder.AddConfiguration(ctx.Configuration.GetSection("Logging"));
                    builder.AddFile(o => o.RootPath = AppContext.BaseDirectory);
                });
                webBuilder.ConfigureKestrel((opt) =>
                {
                });
                webBuilder.UseStartup<Startup>();
            });
}