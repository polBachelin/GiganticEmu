using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace GiganticEmu.Shared.Backend
{
    public class BackendConfiguration
    {
        public const string GiganticEmu = "GiganticEmu";

        public class Agent
        {
            public string Host { get; set; } = default!;
            public string Region { get; set; } = "NA";
            public string ApiKey { get; set; } = default!;
        }

        public string SalsaCK { get; set; } = default!;
        public string BindInterface { get; set; } = "127.0.0.1";
        public int WebPort { get; set; } = 3000;
        public int MicePort { get; set; } = 4000;
        public string MiceHost { get; set; } = "localhost";
        public int GameVersion { get; set; } = 301530;
        public ICollection<Agent> Agents { get; set; } = new List<Agent>();
        public int NumPlayers { get; set; } = 10;
        public string? ApiKey { get; set; }
        public ICollection<string> Maps { get; set; } = new List<string> { "LV_Canyon", "LV_Mistforge", "LV_Valley" };
    }

    public static class BackendConfigurationExtensions
    {
        public static IConfigurationBuilder AddBackendHostConfiguration(this IConfigurationBuilder config, string[] args)
        {
            config.SetBasePath(Directory.GetCurrentDirectory());
            config.AddJsonFile("hostsettings.json", optional: true);
            config.AddEnvironmentVariables();
            config.AddCommandLine(args);

            return config;
        }

        public static IConfigurationBuilder AddBackendAppConfiguration(this IConfigurationBuilder config, string[] args)
        {
            config.AddJsonFile("appsettings.json", optional: true);
            config.AddEnvironmentVariables();
            config.AddCommandLine(args);

            return config;
        }
        public static IHostBuilder AddBackendLogging(this IHostBuilder config)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                .MinimumLevel.Override("Microsoft.AspNetCore.Hosting.Diagnostics", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level}] {Message:lj}{NewLine}{Exception}")
                //.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            return config.UseSerilog();
        }
    }
}