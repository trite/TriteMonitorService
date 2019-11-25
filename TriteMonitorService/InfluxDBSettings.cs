using System;
using System.Collections.Generic;
using System.Text;

namespace TriteMonitorService
{
    public class InfluxDBSettings
    {
        public string URI { get; set; }
        public string database { get; set; }
        public string username { get; set; }
        public string password { get; set; }
        public string measurement { get; set; }
    }
}
