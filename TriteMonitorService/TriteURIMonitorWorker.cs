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
    class TriteURIMonitorWorker : BackgroundService
    {
        private string serverName;
        private string monitorURI;
        private readonly ILogger<TriteURIMonitorWorker> logger;
        private HttpClient httpClient;
        private Stopwatch timer;
        private DateTime nextScan;
        private TimeSpan delay;
        private LineProtocolClient influxClient;
        private LineProtocolPayload payload;
        private string measurementName;
        private Dictionary<string, string> tags;

        public TriteURIMonitorWorker(string serverName,
            string monitorURI,
            int offsetCount,
            TriteURIMonitorWorkerSettings monitorSettings,
            Dictionary<string, string> tags,
            string measurement,
            ILogger<TriteURIMonitorWorker> logger)
        {
            this.serverName = serverName;
            this.monitorURI = monitorURI;
            this.logger = logger;

            timer = new Stopwatch();
            this.nextScan = DateTime.Now + (monitorSettings.OffsetAmount * offsetCount);
            delay = monitorSettings.ScanDelay;

            influxClient = new LineProtocolClient(
                new Uri(monitorSettings.Influx.URI),
                monitorSettings.Influx.Database,
                monitorSettings.Influx.Username,
                monitorSettings.Influx.Password);
            measurementName = measurement;

            this.tags = tags;
            this.tags.Add("host", serverName);

            logger.LogInformation($"Starting worker for: {monitorURI} (Scan delay: {delay}, next scan: {nextScan})");
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            httpClient = new HttpClient(new HttpClientHandler { MaxConnectionsPerServer = 5 })
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
            // First hit after startup sometimes has an extra delay that
            // we don't care about so hit it once and ignore the results:
            // TODO: See if adding this is the reason the very first hit is always faster than the rest
            //_ = await httpClient.GetAsync(monitorURI, cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    TimeSpan wait = nextScan - DateTime.Now;
                    if (wait > TimeSpan.Zero)
                    {
                        await Task.Delay(wait, cancellationToken);
                    }

                    nextScan += delay;
                    HttpRequestMessage reqMsg = new HttpRequestMessage(HttpMethod.Get, monitorURI);
                    timer.Start();
                    // HttpResponseMessage result = await httpClient.GetAsync(monitorURI, cancellationToken);
                    HttpResponseMessage response = await httpClient.SendAsync(reqMsg, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    long timeToHeaders = timer.ElapsedMilliseconds;
                    var result = await response.Content.ReadAsStringAsync();
                    timer.Stop();
                    long timeTaken = timer.ElapsedMilliseconds;
                    timer.Reset();

                    payload = new LineProtocolPayload();
                    LineProtocolPoint pointData = new LineProtocolPoint(
                        measurementName,
                        new Dictionary<string, object>()
                        {
                            { "timetoheaders", timeToHeaders },
                            { "responsetime", timeTaken },
                            { "statuscode", (int)(response.StatusCode) },
                            { "responselength", result.Length }
                        },
                        tags,
                        DateTime.UtcNow);
                    payload.Add(pointData);

                    var influxResult = await influxClient.WriteAsync(payload, cancellationToken);
                    if (!influxResult.Success)
                    {
                        string payloadJSON = JsonConvert.SerializeObject(pointData);
                        logger.LogError($"Failure writing to influx, data that should have been written: {payloadJSON}");
                    }
                }
                catch (TaskCanceledException)
                {
                    // om nom nom
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, $"Error processing healthcheck for {serverName}");
                }
            }
        }
    }
}
