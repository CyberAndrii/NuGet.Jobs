﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace NuGet.Services.Metadata.Catalog.Monitoring
{
    public class RegistrationIdValidator : RegistrationIndexValidator
    {
        public RegistrationIdValidator(
            IDictionary<FeedType, SourceRepository> feedToSource, 
            ILogger<RegistrationIdValidator> logger) 
            : base(feedToSource, logger)
        {
        }

        public override Task<bool> ShouldRunIndex(ValidationContext data, PackageRegistrationIndexMetadata v2, PackageRegistrationIndexMetadata v3)
        {
            return Task.FromResult(v2 != null && v3 != null);
        }

        public override Task CompareIndex(ValidationContext data, PackageRegistrationIndexMetadata v2, PackageRegistrationIndexMetadata v3)
        {
            if (!v2.Id.Equals(v3.Id, System.StringComparison.OrdinalIgnoreCase))
            {
                throw new MetadataFieldInconsistencyException<PackageRegistrationIndexMetadata>(
                    v2, v3, 
                    nameof(PackageRegistrationIndexMetadata.Id),
                    m => m.Id);
            }

            return Task.FromResult(0);
        }
    }
}
