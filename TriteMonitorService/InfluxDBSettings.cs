﻿using System;
using System.Collections.Generic;
using System.Text;

namespace TriteMonitorService
{
    public class InfluxDBSettings
    {
        public string URI { get; set; }
        public string Database { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
    }
}
