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

            TriteURIMonitorWorkerSettings workerSettings = new TriteURIMonitorWorkerSettings()
            {
                OffsetAmount = TimeSpan.FromMilliseconds(configuration.GetSection("ScanSettings:ScanOffsetMilliseconds").Get<int>()),
                ScanDelay = TimeSpan.FromSeconds(configuration.GetSection("ScanSettings:ScanDelaySeconds").Get<int>()),
                Influx = new InfluxDBSettings()
                {
                    URI = configuration.GetSection("InfluxDBInfo:uri").Get<string>(),
                    Database = configuration.GetSection("InfluxDBInfo:database").Get<string>(),
                    Username = configuration.GetSection("InfluxDBInfo:username").Get<string>(),
                    Password = configuration.GetSection("InfluxDBInfo:password").Get<string>()
                }
            };

            #region MonitorTemplateAddLater
            /* TODO: Add section of "monitor templates" where a template can be used to simplify the config
                 
                 Example of template:
                 {
                    "TemplateName": "HomeDC",
                    "TemplateData": {
                      "Name": "{Name}",
                      "MonitorPing": {
                        "Enabled": true,
                        "Address": "{Name}"
                      },
                      "MonitorURI": {
                        "Enabled": true,
                        "URI": "http://{Name}/"
                      },
                      "Tags": [
                        {
                          "Name": "DC",
                          "Value": "{DCTag}"
                        },
                        {
                          "Name": "SomeTag",
                          "Value": "{SomeOtherTag}"
                        }
                      ]
                    }
                }
                 
                Example of monitor:
                    {
                        "FromTemplate": true,
                        "TemplateName": "HomeDC",
                        "Replacements":
                        {
                            "Name": "google.com",
                            "DCTag": "Home",
                            "SomeOtherTag": "SomeTagValue"
                        }
                    }
                 
            // TODO: Revisit this later:
            Dictionary<string, IConfigurationSection> templates = new Dictionary<string, IConfigurationSection>();

            foreach (IConfigurationSection template in configuration.GetSection("Monitoring:MonitorTemplates").GetChildren())
            {
                templates.Add(
                    template.GetValue<string>("TemplateName"),
                    template.GetSection("Template")
                );
            }

            foreach (IConfigurationSection monitor in configuration.GetSection("Monitoring:Monitors").GetChildren())
            {
                bool fromTemplate = monitor.GetSection("FromTemplate").Get<bool>();
                if (fromTemplate)
                {
                    string templateName = monitor.GetSection("TemplateName").Get<string>();
                    IConfigurationSection template = templates[templateName];

                    Dictionary<string, string> replacements = new Dictionary<string, string>();
                    foreach (IConfigurationSection replacement in monitor.GetSection("Replacements").GetChildren())
                    {
                        replacements.Add(
                            replacement.GetValue<string>("Key"),
                            replacement.GetValue<string>("Value")
                        );
                    }

                    string monitorName = replacements["{Name}"];
                    if (monitorName is null)
                    {
                        monitorName = monitor.GetValue<string>("Name");
                        if (monitorName is null)
                        {
                            throw new Exception("Monitor is listed as a template but lacks a Name replacement");
                        }
                    }

                    bool monitorPing = template.GetSection("MonitorPing:Enabled").Get<bool>();
                    string pingAddress;
                    if (monitorPing)
                    {
                        pingAddress = template.GetSection("MonitorPing:Address").Get<string>();
                        foreach (var replacement in replacements)
                        {
                            pingAddress = pingAddress.Replace(replacement.Key, replacement.Value);
                        }
                    }
                    else
                    {
                        monitorPing = monitor.GetSection("MonitorPing:Enabled").Get<bool>();
                        if (monitorPing)
                        {
                            pingAddress = monitor.GetSection("MonitorPing:Address").Get<string>();
                        }
                    }
                    if (monitorPing)
                    {
                        // Fire up ping monitor
                    }

                    bool monitorURI = template.GetSection("MonitorURI:Enabled").Get<bool>();
                    string uri;
                    if (monitorURI)
                    {
                        uri = template.GetSection("MonitorURI:URI").Get<string>();
                        foreach (var replacement in replacements)
                        {
                            uri = uri.Replace(replacement.Key, replacement.Value);
                        }
                    }
                    else
                    {
                        monitorPing = monitor.GetSection("MonitorPing:Enabled").Get<bool>();
                        if (monitorURI)
                        {
                            uri = monitor.GetSection("MonitorPing:Address").Get<string>();
                        }
                    }
                    if (monitorURI)
                    {
                        // Fire up ping monitor
                    }

                    string adsf = "";
                }
                else
                {

                }
            }
            */
            #endregion MonitorTemplateAddLater

            // New way to get server list, ref: https://stackoverflow.com/a/44392604
            // TODO: This should be replaced with templating later:
            
            IEnumerable<IConfigurationSection> monitorEnumerable = configuration.GetSection("Monitors").GetChildren();
            List<IConfigurationSection> monitorList = new List<IConfigurationSection>();
            foreach (IConfigurationSection monitor in monitorEnumerable)
            {
                monitorList.Add(monitor);
            }
            IConfigurationSection[] monitors = monitorList.ToArray();

            foreach (IConfigurationSection monitor in monitors)
            {
                string monitorName = monitor.GetValue<string>("Name");

                Dictionary<string, string> monitorTags = new Dictionary<string, string>();
                foreach (IConfigurationSection tags in monitor.GetSection("Tags").GetChildren())
                {
                    monitorTags.Add(
                        tags.GetValue<string>("Name"),
                        tags.GetValue<string>("Value")
                    );
                }

                bool monitorPing = monitor.GetSection("MonitorPing:Enabled").Get<bool>();
                if (monitorPing)
                {
                    string pingAddress = monitor.GetSection("MonitorPing:Address").Get<string>();
                    string pingMeasurement = monitor.GetSection("MonitorPing:Measurement").Get<string>();
                    // TODO: Fire up ping monitor here (also: create ping monitor worker)
                }

                bool monitorURI = monitor.GetSection("MonitorURI:Enabled").Get<bool>();
                if (monitorURI)
                {
                    string uri = monitor.GetSection("MonitorURI:URI").Get<string>();
                    string urimeasurement = monitor.GetSection("MonitorURI:Measurement").Get<string>();

                    services.AddSingleton<IHostedService>(provider => new TriteURIMonitorWorker(
                        monitorName,
                        uri,
                        Array.IndexOf(monitors, monitor),
                        workerSettings,
                        monitorTags,
                        urimeasurement,
                        provider.GetService<ILogger<TriteURIMonitorWorker>>()));
                }
            }
            


            /* TODO: remove this after implementing updated monitor config stuff, keeping for reference for now

            string[] serverList = configuration.GetSection("ScanSettings:ServerList").Get<string[]>();
            string uriFormat = configuration.GetSection("ScanSettings:URIFormat").Get<string>();
            
            foreach (string server in serverList)
            {
                string fullURI = string.Format(uriFormat, server);
                services.AddSingleton<IHostedService>(provider => new TriteURIMonitorWorker(
                    server, 
                    fullURI, 
                    Array.IndexOf(serverList, server), 
                    workerSettings, 
                    provider.GetService<ILogger<TriteMonitorWorker>>()));
            }
            */
        }
    }
}
