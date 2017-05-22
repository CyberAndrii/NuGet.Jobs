﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace NuGet.Services.Metadata.Catalog.Monitoring
{
    public class PackageTimestampMetadataResourceV2Provider : ResourceProvider
    {
        public PackageTimestampMetadataResourceV2Provider(ILoggerFactory loggerFactory)
            : base(typeof(IPackageTimestampMetadataResource))
        {
            _loggerFactory = loggerFactory;
        }

        private readonly ILoggerFactory _loggerFactory;

        public override async Task<Tuple<bool, INuGetResource>> TryCreate(SourceRepository source, CancellationToken token)
        {
            PackageTimestampMetadataResourceV2 resource = null;

            if (await source.GetFeedType(token) == FeedType.HttpV2)
            {
                resource = new PackageTimestampMetadataResourceV2(
                    source.PackageSource.Source, 
                    _loggerFactory.CreateLogger<PackageTimestampMetadataResourceV2>());
            }

            return new Tuple<bool, INuGetResource>(resource != null, resource);
        }
    }
}
