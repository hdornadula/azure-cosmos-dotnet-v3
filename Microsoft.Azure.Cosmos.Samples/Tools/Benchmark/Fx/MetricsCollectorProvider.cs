﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace CosmosBenchmark
{
    using System;
    using System.Diagnostics.Metrics;
    using System.Threading;
    using System.Threading.Tasks;
    using OpenTelemetry.Metrics;

    /// <summary>
    /// Represents the metrics collector provider.
    /// </summary>
    internal class MetricsCollectorProvider
    {
        private MetricCollectionWindow metricCollectionWindow;

        private static readonly object metricCollectionWindowLock = new object();

        private readonly MetricsCollector insertOperationMetricsCollector;

        private readonly MetricsCollector queryOperationMetricsCollector;

        private readonly MetricsCollector readOperationMetricsCollector;

        private readonly Meter insertOperationMeter = new("CosmosBenchmarkInsertOperationMeter");

        private readonly Meter queryOperationMeter = new("CosmosBenchmarkQueryOperationMeter");

        private readonly Meter readOperationMeter = new("CosmosBenchmarkReadOperationMeter");

        private readonly MeterProvider meterProvider;

        public MetricsCollectorProvider(BenchmarkConfig config, MeterProvider meterProvider)
        {
            this.meterProvider = meterProvider;
            this.insertOperationMetricsCollector ??= new MetricsCollector(this.insertOperationMeter, "Insert");
            this.queryOperationMetricsCollector ??= new MetricsCollector(this.queryOperationMeter, "Query");
            this.readOperationMetricsCollector ??= new MetricsCollector(this.readOperationMeter, "Read");
            this.metricCollectionWindow ??= new MetricCollectionWindow(config);

            ThreadPool.QueueUserWorkItem(async state =>
            {
                while (true)
                {
                    Console.WriteLine("metricsCollection window .."); // TODO REMOVE

                    this.meterProvider.ForceFlush();
                    Console.WriteLine("metricsCollection force flush "); // TODO REMOVE

                    await Task.Delay(TimeSpan.FromSeconds(config.MetricsReportingIntervalInSec));
                }
            });
        }

        private MetricCollectionWindow GetCurrentMetricCollectionWindow(BenchmarkConfig config)
        {
            if (this.metricCollectionWindow is null || !this.metricCollectionWindow.IsValid)
            {
                lock (metricCollectionWindowLock)
                {
                    this.metricCollectionWindow ??= new MetricCollectionWindow(config);
                }
            }

            return this.metricCollectionWindow;
        }




        /// <summary>
        /// Gets the metric collector.
        /// </summary>
        /// <param name="benchmarkOperation">Benchmark operation.</param>
        /// <param name="config">Benchmark configuration.</param>
        /// <returns>Metrics collector.</returns>
        /// <exception cref="NotSupportedException">Thrown if provided benchmark operation is not covered supported to collect metrics.</exception>
        public IMetricsCollector GetMetricsCollector(IBenchmarkOperation benchmarkOperation, BenchmarkConfig config)
        {
            return benchmarkOperation.OperationType switch
            {
                BenchmarkOperationType.Insert => this.insertOperationMetricsCollector,
                BenchmarkOperationType.Query => this.queryOperationMetricsCollector,
                BenchmarkOperationType.Read => this.readOperationMetricsCollector,
                _ => throw new NotSupportedException($"The type of {nameof(benchmarkOperation)} is not supported for collecting metrics."),
            };
        }
    }
}
