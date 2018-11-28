﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace NuGet.Services.Metadata.Catalog.Monitoring
{
    public class RegistrationExistsValidator : RegistrationLeafValidator
    {
        public RegistrationExistsValidator(ValidatorConfiguration config, ILogger<RegistrationExistsValidator> logger)
            : base(config, logger)
        {
        }

        public override Task<bool> ShouldRunLeafAsync(
            ValidationContext context,
            PackageRegistrationLeafMetadata v2,
            PackageRegistrationLeafMetadata v3)
        {
            return Task.FromResult(true);
        }

        public override Task CompareLeafAsync(
            ValidationContext context,
            PackageRegistrationLeafMetadata v2,
            PackageRegistrationLeafMetadata v3)
        {
            var v2Exists = v2 != null;
            var v3Exists = v3 != null;
            var completedTask = Task.FromResult(0);

            if (v2Exists != v3Exists)
            {
                // Currently, leaf nodes are not deleted after a package is deleted.
                // This is a known bug. Do not fail validations because of it.
                // See https://github.com/NuGet/NuGetGallery/issues/4475
                if (v3Exists && !(v3 is PackageRegistrationIndexMetadata))
                {
                    Logger.LogInformation("{PackageId} {PackageVersion} doesn't exist in V2 but has a leaf node in V3!", context.Package.Id, context.Package.Version);
                    return completedTask;
                }

                const string existsString = "exists";
                const string doesNotExistString = "doesn't exist";

                throw new MetadataInconsistencyException<PackageRegistrationLeafMetadata>(
                    v2,
                    v3,
                    $"V2 {(v2Exists ? existsString : doesNotExistString)} but V3 {(v3Exists ? existsString : doesNotExistString)}!");
            }

            return completedTask;
        }
    }
}