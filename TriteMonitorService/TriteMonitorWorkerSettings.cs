using System;
using System.Collections.Generic;
using System.Text;

namespace TriteMonitorService
{
    public class TriteMonitorWorkerSettings
    {
        public TimeSpan OffsetAmount { get; set; }
        public TimeSpan ScanDelay { get; set; }
        public InfluxDBSettings Influx { get; set; }
    }
}
