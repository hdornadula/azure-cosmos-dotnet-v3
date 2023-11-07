﻿// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Tracing
{
    using System;
    using System.Collections.Generic;

    internal sealed class MergedTrace : ITrace
    {
        private static readonly IReadOnlyDictionary<string, object> EmptyDictionary = new Dictionary<string, object>();
        private readonly List<ITrace> children;
        private readonly Lazy<Dictionary<string, object>> data;

        public MergedTrace(
            List<ITrace> traces,
            DateTime startTime,
            TimeSpan elapsedTime,
            TraceSummary summary)
        {
            this.children = traces;
            this.Id = Guid.NewGuid();
            this.StartTime = startTime;
            this.Duration = elapsedTime;
            this.Summary = summary ?? throw new ArgumentNullException(nameof(summary));
            this.data = new Lazy<Dictionary<string, object>>();

            foreach (ITrace trace in traces)
            {
                if (trace.Data.Count > 0)
                {
                    foreach (KeyValuePair<string, object> kvp in trace.Data)
                    {
                        if (!this.data.Value.ContainsKey(kvp.Key))
                        {
                            this.data.Value.Add(kvp.Key, kvp.Value);

                        }
                    }
                }
            }
        }
        public string Name => "Multi-request Trace Instance: " + this.Id.ToString();

        public Guid Id { get; }

        public DateTime StartTime { get; }

        public TimeSpan Duration { get; }

        public TraceLevel Level => default;

        public TraceComponent Component => default;

        public TraceSummary Summary { get; }

        public ITrace Parent => null;

        public IReadOnlyList<ITrace> Children => this.children;

        public IReadOnlyDictionary<string, object> Data => this.data.IsValueCreated ? this.data.Value : MergedTrace.EmptyDictionary;

        public void AddChild(ITrace child)
        {
            lock (this.children)
            {
                this.children.Add(child);
            }
        }

        public void AddDatum(string key, TraceDatum traceDatum)
        {
            this.data.Value.Add(key, traceDatum);
            this.Summary.UpdateRegionContacted(traceDatum);
        }

        public void AddDatum(string key, object value)
        {
            this.data.Value.Add(key, value);
        }

        public void AddOrUpdateDatum(string key, object value)
        {
            this.data.Value[key] = value;
        }

        public void Dispose()
        {
            // No Op
        }

        public ITrace StartChild(
            string name)
        {
            return this.StartChild(
                name,
                component: this.Component,
                level: TraceLevel.Info);
        }

        public ITrace StartChild(
            string name,
            TraceComponent component,
            TraceLevel level)
        {
            return this;
        }
    }
}
