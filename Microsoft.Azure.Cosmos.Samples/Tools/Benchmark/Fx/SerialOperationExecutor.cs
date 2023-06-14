﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace CosmosBenchmark
{
    using System;
    using System.Diagnostics;
    using System.Threading.Tasks;
    using Microsoft.ApplicationInsights;
    using Microsoft.Azure.Cosmos;
    using Microsoft.Extensions.Logging;

    internal class SerialOperationExecutor : IExecutor
    {
        private readonly IBenchmarkOperation operation;

        private readonly string executorId;

        public SerialOperationExecutor(
            string executorId,
            IBenchmarkOperation benchmarkOperation)
        {
            this.executorId = executorId;
            this.operation = benchmarkOperation;

            this.SuccessOperationCount = 0;
            this.FailedOperationCount = 0;
        }

        public int SuccessOperationCount { get; private set; }

        public int FailedOperationCount { get; private set; }

        public double TotalRuCharges { get; private set; }

        public async Task ExecuteAsync(
                int iterationCount,
                bool isWarmup,
                bool traceFailures,
                Action completionCallback,
                BenchmarkConfig benchmarkConfig,
                ILogger logger,
                TelemetryClient telemetryClient)
        {
            logger.LogInformation($"Executor {this.executorId} started");

            logger.LogInformation("Initializing counters and metrics.");

            try
            {
                int currentIterationCount = 0;
                do
                {
                    IMetricsCollector metricsCollector = MetricsCollectorProvider.GetMetricsCollector(this.operation, telemetryClient);

                    OperationResult? operationResult = null;

                    await this.operation.PrepareAsync();

                    using (IDisposable telemetrySpan = TelemetrySpan.StartNew(
                                benchmarkConfig,
                                () => operationResult.Value,
                                disableTelemetry: isWarmup,
                                metricsCollector.RecordLatency))
                    {
                        try
                        {
                            operationResult = await this.operation.ExecuteOnceAsync();

                            metricsCollector.CollectMetricsOnSuccess();

                            // Success case
                            this.SuccessOperationCount++;
                            this.TotalRuCharges += operationResult.Value.RuCharges;

                            if (!isWarmup)
                            {
                                CosmosDiagnosticsLogger.Log(operationResult.Value.CosmosDiagnostics);
                            }
                        }
                        catch (Exception ex)
                        {
                            if (traceFailures)
                            {
                                Console.WriteLine(ex.ToString());
                            }

                            metricsCollector.CollectMetricsOnFailure();

                            // failure case
                            this.FailedOperationCount++;

                            // Special case of cosmos exception
                            double opCharge = 0;
                            if (ex is CosmosException cosmosException)
                            {
                                opCharge = cosmosException.RequestCharge;
                                this.TotalRuCharges += opCharge;
                            }

                            operationResult = new OperationResult()
                            {
                                // TODO: Populate account, database, collection context into ComsosDiagnostics
                                RuCharges = opCharge,
                                LazyDiagnostics = () => ex.ToString(),
                            };
                        }
                    }

                    currentIterationCount++;
                } while (currentIterationCount < iterationCount);

                Trace.TraceInformation($"Executor {this.executorId} completed");
            }catch(Exception e)
            {
                Console.WriteLine($"Error: {e.Message}");
            }
            finally
            {
                completionCallback();
            }
        }
    }
}