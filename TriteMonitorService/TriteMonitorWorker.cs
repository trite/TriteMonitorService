using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using InfluxDB.LineProtocol.Client;
using InfluxDB.LineProtocol.Payload;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TriteMonitorService
{
    class TriteMonitorWorker : BackgroundService
    {
        private string serverName;
        private string monitorURI;
        private readonly ILogger<TriteMonitorWorker> logger;
        private HttpClient httpClient;
        private Stopwatch timer;
        private DateTime nextScan;
        private TimeSpan delay;
        private LineProtocolClient influxClient;
        private LineProtocolPayload payload;
        private string measurementName;

        public TriteMonitorWorker(string serverName,
            string monitorURI,
            int offsetCount,
            TriteMonitorWorkerSettings monitorSettings,
            ILogger<TriteMonitorWorker> logger)
        {
            this.serverName = serverName;
            this.monitorURI = monitorURI;
            this.logger = logger;

            timer = new Stopwatch();
            this.nextScan = DateTime.Now + (monitorSettings.OffsetAmount * offsetCount);
            delay = monitorSettings.ScanDelay;

            influxClient = new LineProtocolClient(
                new Uri(monitorSettings.Influx.URI),
                monitorSettings.Influx.database,
                monitorSettings.Influx.username,
                monitorSettings.Influx.password);
            measurementName = monitorSettings.Influx.measurement;

            logger.LogInformation($"Starting worker for: {monitorURI} (Scan delay: {delay}, next scan: {nextScan})");
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(2)
            };

            base.StartAsync(cancellationToken);
            return Task.CompletedTask;
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            httpClient.Dispose();
            logger.LogInformation($"Stopping worker for: {monitorURI}");
            base.StopAsync(cancellationToken);
            return Task.CompletedTask;
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            // First hit after startup has extra delay that we don't care about
            // so hit it once and ignore the results:
            _ = await httpClient.GetAsync(monitorURI, cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    nextScan += delay;
                    timer.Start();
                    HttpResponseMessage result = await httpClient.GetAsync(monitorURI, cancellationToken);
                    timer.Stop();
                    long timeTaken = timer.ElapsedMilliseconds;
                    timer.Reset();

                    payload = new LineProtocolPayload();
                    LineProtocolPoint pointData = new LineProtocolPoint(
                        measurementName,
                        new Dictionary<string, object>()
                        {
                            { "responsetime", timeTaken },
                            { "statuscode", (int)(result.StatusCode) },
                            { "responselength", result.Content.Headers.ContentLength }
                        },
                        new Dictionary<string, string>()
                        {
                            { "host", serverName }
                        },
                        DateTime.UtcNow);
                    payload.Add(pointData);

                    var influxResult = await influxClient.WriteAsync(payload, cancellationToken);
                    if (!influxResult.Success)
                    {
                        string payloadJSON = JsonConvert.SerializeObject(pointData);
                        logger.LogError($"Failure writing to influx, data that should have been written: {payloadJSON}");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, $"Error processing healthcheck for {serverName}");
                }

                await Task.Delay(nextScan - DateTime.Now, cancellationToken);
            }
        }
    }
}
