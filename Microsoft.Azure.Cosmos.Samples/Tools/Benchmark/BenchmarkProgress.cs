﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------
namespace CosmosBenchmark
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Newtonsoft.Json;

    /// <summary>
    /// Represents a class that is used as an item in CosmosDB to track benchmark progress.
    /// </summary>
    public class BenchmarkProgress
    {
        [JsonProperty]
        public string id { get; set; }
        [JsonProperty]
        public string MachineName { get; set; }
        [JsonProperty]
        public string JobStatus { get; set; }
        [JsonProperty]
        public DateTime JobStartTime { get; set; }
        [JsonProperty]
        public DateTime JobEndTime { get; set; }

    }
}
