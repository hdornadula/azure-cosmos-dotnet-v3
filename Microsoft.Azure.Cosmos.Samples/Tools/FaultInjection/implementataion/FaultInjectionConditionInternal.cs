﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------
namespace Microsoft.Azure.Cosmos.FaultInjection
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Azure.Documents;
    using Microsoft.Azure.Documents.Rntbd;
    
    internal class FaultInjectionConditionInternal
    {
        private readonly List<IFaultInjectionConditionValidator> validators;

        private string containerResourceId;
        private OperationType operationType;
        private List<Uri> regionEndpoints;
        private List<Uri> physicalAddresses;

        public FaultInjectionConditionInternal()
        {
            this.validators = new List<IFaultInjectionConditionValidator>();
        }

        public OperationType GetOperationType()
        {
            return this.operationType;
        }

        public void SetContainerResourceId(string containerResourceId)
        {
            this.containerResourceId = containerResourceId;
            if (!string.IsNullOrEmpty(this.containerResourceId))
            {
                this.validators.Add(new ContainerValidator(this.containerResourceId));
            }
        }

        public void SetOperationType(OperationType operationType)
        {
            this.operationType = operationType;
        }

        public void SetRegionEndpoints(List<Uri> regionEndpoints)
        {
            this.regionEndpoints = regionEndpoints;
            if (this.regionEndpoints != null)
            {
                this.validators.Add(new RegionEndpointValidator(this.regionEndpoints));
            }
        }

        public string GetContainerResourceId()
        {
            return this.containerResourceId;
        }

        public List<Uri> GetRegionEndpoints()
        {
            return this.regionEndpoints;
        }

        public List<Uri> GetPhysicalAddresses()
        {
            return this.physicalAddresses;
        }

        public void SetAddresses(List<Uri> physicalAddresses)
        {
            this.physicalAddresses = physicalAddresses;
            if (this.physicalAddresses != null && physicalAddresses.Count > 0)
            {
                this.validators.Add(new AddressValidator(this.physicalAddresses));
            }
        }

        public bool IsApplicable(string ruleId, ChannelCallArguments args)
        {
            foreach (IFaultInjectionConditionValidator validator in this.validators)
            {
                if (!validator.IsApplicable(ruleId, args))
                {
                    return false;
                }
            }

            return true;
        }

        //Used for connection delay
        public bool IsApplicable(Uri callUri, DocumentServiceRequest request)
        {
            foreach (IFaultInjectionConditionValidator validator in this.validators)
            {
                if (validator.GetType() == typeof(RegionEndpointValidator))
                {
                    RegionEndpointValidator regionEndpointValidator = (RegionEndpointValidator)validator;
                    if (!regionEndpointValidator.IsApplicable(request))
                    {
                        return false;
                    }
                }
                else if (validator.GetType() == typeof(OperationTypeValidator))
                {
                    OperationTypeValidator operationTypeValidator = (OperationTypeValidator)validator;
                    if (!operationTypeValidator.IsApplicable(request))
                    {
                        return false;
                    }
                }
                else if (validator.GetType() == typeof(ContainerValidator))
                {
                    ContainerValidator containerValidator = (ContainerValidator)validator;
                    if (!containerValidator.IsApplicable(request))
                    {
                        return false;
                    }
                }
                else if (validator.GetType() == typeof(AddressValidator))
                {
                    AddressValidator addressValidator = (AddressValidator)validator;
                    if (!addressValidator.IsApplicable(callUri))
                    {
                        return false;
                    }
                }
                else
                {
                    throw new ArgumentException($"Unknown validator type {validator.GetType()}");
                }
            }

            return true;
        }

        private interface IFaultInjectionConditionValidator
        {
            public bool IsApplicable(string ruleId, ChannelCallArguments args);
        }

        private class RegionEndpointValidator : IFaultInjectionConditionValidator
        {
            private readonly List<Uri> regionEndpoints;

            public RegionEndpointValidator(List<Uri> regionEndpoints)
            {
                this.regionEndpoints = regionEndpoints;
            }

            public bool IsApplicable(string ruleId, ChannelCallArguments args)
            {
                bool isApplicable = this.regionEndpoints.Any(uri => args.PreparedCall.Uri.AbsoluteUri.StartsWith(uri.AbsoluteUri));

                return isApplicable;
            }

            //Used for Connection Delay
            public bool IsApplicable(DocumentServiceRequest request)
            {
                bool isApplicable = this.regionEndpoints.Contains(request.RequestContext.LocationEndpointToRoute);

                return isApplicable;
            }
        }

        private class OperationTypeValidator : IFaultInjectionConditionValidator
        {
            private readonly OperationType operationType;

            public OperationTypeValidator(OperationType operationType)
            {
                this.operationType = operationType;
            }

            public bool IsApplicable(string ruleId, ChannelCallArguments args)
            {
                bool isApplicable = args.OperationType == this.operationType;

                return isApplicable;
            }

            //Used for Connection Delay
            public bool IsApplicable(DocumentServiceRequest request)
            {
                bool isApplicable = request.OperationType == this.operationType;

                return isApplicable;
            }
        }

        private class ContainerValidator : IFaultInjectionConditionValidator
        {
            private readonly string containerResourceId;

            public ContainerValidator(string containerResourceId)
            {
                this.containerResourceId = containerResourceId;
            }
            public bool IsApplicable(string ruleId, ChannelCallArguments args)
            {
                bool isApplicable = String.Equals(this.containerResourceId, args.ResolvedCollectionRid);

                return isApplicable;
            }

            //Used for Connection Delay
            public bool IsApplicable(DocumentServiceRequest request)
            {
                bool isApplicable = String.Equals(this.containerResourceId, request.RequestContext.ResolvedCollectionRid);

                return isApplicable;
            }
        }

        private class AddressValidator : IFaultInjectionConditionValidator
        {
            private readonly List<Uri> addresses;

            public AddressValidator(List<Uri> addresses)
            {
                this.addresses = addresses;
            }

            public bool IsApplicable(string ruleId, ChannelCallArguments args)
            {
                bool isApplicable = this.addresses.Exists(uri => args.PreparedCall.Uri.AbsoluteUri.StartsWith(uri.AbsoluteUri));

                return isApplicable;
            }

            //Used for Connection Delay
            public bool IsApplicable(Uri callUri)
            {
                bool isApplicable = this.addresses.Exists(uri => callUri.AbsoluteUri.StartsWith(uri.AbsoluteUri));

                return isApplicable;
            }
        }
    }
}
