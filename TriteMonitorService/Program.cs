using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace TriteMonitorService
{
    public class Program
    {
        public static void Main(string[] args)
        {
            IConfigurationRoot configRoot = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();

            string logPath = configRoot.GetSection("LogLocation").Get<string>();

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.File(logPath)
                .CreateLogger();
            try
            {
                Console.WriteLine("Running...");
                Log.Information("Starting the service");
                CreateHostBuilder(args).Build().Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "There was a problem starting the service");
            }
            finally
            {
                Log.Information("Service stopped.");
                Log.CloseAndFlush();
            }

            return;
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            List<string> hostsToScan = new List<string>();
            return Host.CreateDefaultBuilder(args)
                .UseWindowsService()
                .ConfigureServices(StartupTasks)
                .UseSerilog();
        }

        public static void StartupTasks(HostBuilderContext hostContext, IServiceCollection services)
        {
            IConfiguration configuration = hostContext.Configuration;
            string[] serverList = configuration.GetSection("ScanSettings:ServerList").Get<string[]>();
            string uriFormat = configuration.GetSection("ScanSettings:URIFormat").Get<string>();

            TriteMonitorWorkerSettings workerSettings = new TriteMonitorWorkerSettings()
            {
                OffsetAmount = TimeSpan.FromSeconds(configuration.GetSection("ScanSettings:ScanOffsetSeconds").Get<int>()),
                ScanDelay = TimeSpan.FromSeconds(configuration.GetSection("ScanSettings:ScanDelaySeconds").Get<int>()),
                Influx = new InfluxDBSettings()
                {
                    URI = configuration.GetSection("InfluxDBInfo:uri").Get<string>(),
                    database = configuration.GetSection("InfluxDBInfo:database").Get<string>(),
                    username = configuration.GetSection("InfluxDBInfo:username").Get<string>(),
                    password = configuration.GetSection("InfluxDBInfo:password").Get<string>(),
                    measurement = configuration.GetSection("InfluxDBInfo:measurement").Get<string>()
                }
            };
            
            foreach (string server in serverList)
            {
                string fullURI = string.Format(uriFormat, server);
                services.AddSingleton<IHostedService>(provider => new TriteMonitorWorker(
                    server, 
                    fullURI, 
                    Array.IndexOf(serverList, server), 
                    workerSettings, 
                    provider.GetService<ILogger<TriteMonitorWorker>>()));
            }
        }
    }
}
