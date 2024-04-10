﻿namespace TestWorkloadV2
{
    using Newtonsoft.Json;
    using System.Collections.Generic;
    using System.Threading;
    using System;

    internal class DataSource
    {
        // private readonly List<string> additionalProperties = new List<string>();
        private readonly int partitionKeyCount;
        private string padding;
        private int itemId;

        public readonly string PartitionKeyValuePrefix;
        public readonly string[] PartitionKeyStrings;

        public int InitialItemId { get; private set; }

        public int ItemId => this.itemId;

        private const string idFormatSpecifier = "D9";

        public DataSource(CommonConfiguration configuration)
        {
            this.PartitionKeyValuePrefix = DateTime.UtcNow.ToString("MMddHHmm-");
            this.partitionKeyCount = Math.Min(configuration.PartitionKeyCount, configuration.TotalRequestCount);
            this.PartitionKeyStrings = this.GetPartitionKeys(this.partitionKeyCount);
            // Setup properties - reduce some for standard properties like PK and Id we are adding
            //for (int i = 0; i < configuration.ItemPropertyCount - 10; i++)
            //{
            //    this.additionalProperties.Add(i.ToString());
            //}
            this.padding = string.Empty;
        }

        // Ugly as the caller has to remember to do this, but anyway looks optional
        public void InitializePaddingAndInitialItemId(string padding, int? itemIndex = null)
        {
            this.padding = padding;
            this.InitialItemId = itemIndex ?? 0;
            this.itemId = this.InitialItemId;
        }

        public string GetId(int itemId)
        {
            return itemId.ToString(idFormatSpecifier);
        }

        public (MyDocument, int) GetNextItem()
        {
            int currentIndex = Interlocked.Add(ref this.itemId, 0);
            int currentPKIndex = currentIndex % this.partitionKeyCount;
            string partitionKey = this.PartitionKeyStrings[currentPKIndex];
            string id = this.ItemId.ToString(idFormatSpecifier);
            Interlocked.Increment(ref this.itemId);

            return (new MyDocument()
            {
                Id = id,
                PK = partitionKey,
                //Arr = this.additionalProperties,
                Other = this.padding
            }, currentPKIndex);
        }

        private string[] GetPartitionKeys(int partitionKeyCount)
        {
            string[] partitionKeys = new string[partitionKeyCount];
            int partitionKeySuffixLength = (partitionKeyCount - 1).ToString().Length;
            string partitionKeySuffixFormatSpecifier = "D" + partitionKeySuffixLength;

            for (int i = 0; i < partitionKeyCount; i++)
            {
                partitionKeys[i] = this.PartitionKeyValuePrefix + i.ToString(partitionKeySuffixFormatSpecifier);
            }

            return partitionKeys;
        }
    }

}
