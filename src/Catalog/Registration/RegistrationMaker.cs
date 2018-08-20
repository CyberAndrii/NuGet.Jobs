﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Services.Metadata.Catalog.Persistence;
using VDS.RDF;

namespace NuGet.Services.Metadata.Catalog.Registration
{
    public static class RegistrationMaker
    {
        public static async Task ProcessAsync(
            RegistrationKey registrationKey,
            IReadOnlyDictionary<string, IGraph> newItems,
            StorageFactory storageFactory,
            Uri contentBaseAddress,
            int partitionSize,
            int packageCountThreshold,
            ITelemetryService telemetryService,
            CancellationToken cancellationToken)
        {
            await ProcessAsync(
                registrationKey,
                newItems,
                (k, u, g) => true,
                storageFactory,
                contentBaseAddress,
                partitionSize,
                packageCountThreshold,
                telemetryService,
                cancellationToken);
        }

        public static async Task ProcessAsync(
            RegistrationKey registrationKey,
            IReadOnlyDictionary<string, IGraph> newItems,
            ShouldIncludeRegistrationPackage shouldInclude,
            StorageFactory storageFactory,
            Uri contentBaseAddress,
            int partitionSize,
            int packageCountThreshold,
            ITelemetryService telemetryService,
            CancellationToken cancellationToken)
        {
            Trace.TraceInformation("RegistrationMaker.Process: registrationKey = {0} newItems: {1}", registrationKey, newItems.Count);

            IRegistrationPersistence registration = new RegistrationPersistence(storageFactory, registrationKey, partitionSize, packageCountThreshold, contentBaseAddress);

            IDictionary<RegistrationEntryKey, RegistrationCatalogEntry> existing = await registration.Load(cancellationToken);

            Trace.TraceInformation("RegistrationMaker.Process: existing = {0}", existing.Count);

            IDictionary<RegistrationEntryKey, RegistrationCatalogEntry> delta = PromoteRegistrationKey(newItems, shouldInclude);

            Trace.TraceInformation("RegistrationMaker.Process: delta = {0}", delta.Count);
            telemetryService.TrackMetric(TelemetryConstants.RegistrationDeltaCount, (uint)delta.Count, new Dictionary<string, string>()
            {
                { TelemetryConstants.ContentBaseAddress, contentBaseAddress.AbsoluteUri }
            });

            IDictionary<RegistrationEntryKey, RegistrationCatalogEntry> resulting = Apply(existing, delta);

            Trace.TraceInformation("RegistrationMaker.Process: resulting = {0}", resulting.Count);
            await registration.Save(resulting, cancellationToken);
        }

        private static IDictionary<RegistrationEntryKey, RegistrationCatalogEntry> PromoteRegistrationKey(
            IReadOnlyDictionary<string, IGraph> newItems,
            ShouldIncludeRegistrationPackage shouldInclude)
        {
            IDictionary<RegistrationEntryKey, RegistrationCatalogEntry> promoted = new Dictionary<RegistrationEntryKey, RegistrationCatalogEntry>();
            foreach (var newItem in newItems)
            {
                var promotedEntry = RegistrationCatalogEntry.Promote(
                    newItem.Key,
                    newItem.Value,
                    shouldInclude,
                    isExistingItem: false);

                promoted[promotedEntry.Key] = promotedEntry.Value;
            }

            return promoted;
        }

        private static IDictionary<RegistrationEntryKey, RegistrationCatalogEntry> Apply(
            IDictionary<RegistrationEntryKey, RegistrationCatalogEntry> existing,
            IDictionary<RegistrationEntryKey, RegistrationCatalogEntry> delta)
        {
            IDictionary<RegistrationEntryKey, RegistrationCatalogEntry> resulting = new Dictionary<RegistrationEntryKey, RegistrationCatalogEntry>();

            foreach (var item in existing)
            {
                if (delta.ContainsKey(item.Key))
                {
                    resulting.Add(item.Key, delta[item.Key]);
                    delta.Remove(item.Key);
                }
                else
                {
                    resulting.Add(item);
                }
            }

            foreach (var item in delta)
            {
                resulting.Add(item);
            }

            return resulting;
        }
    }
}