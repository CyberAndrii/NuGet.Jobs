﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NuGet.Services.Configuration;
using NuGet.Services.Metadata.Catalog;
using NuGet.Services.Metadata.Catalog.Helpers;
using NuGet.Services.Metadata.Catalog.Persistence;

namespace Ng.Jobs
{
    public class Feed2CatalogJob : LoopingNgJob
    {
        private const string FeedUrlFormatSuffix = "&$top={2}&$select=Created,LastEdited,Published,LicenseNames,LicenseReportUrl&semVerLevel=2.0.0";
        private static readonly DateTime DateTimeMinValueUtc = new DateTime(0L, DateTimeKind.Utc);

        protected bool Verbose;
        protected string Gallery;
        protected IStorage CatalogStorage;
        protected IStorage AuditingStorage;
        protected DateTime? StartDate;
        protected TimeSpan Timeout;
        protected int Top;

        public Feed2CatalogJob(ITelemetryService telemetryService, ILoggerFactory loggerFactory)
            : base(telemetryService, loggerFactory)
        {
        }

        public override string GetUsage()
        {
            return "Usage: ng feed2catalog "
                   + $"-{Arguments.Gallery} <v2-feed-address> "
                   + $"-{Arguments.StorageBaseAddress} <storage-base-address> "
                   + $"-{Arguments.StorageType} file|azure "
                   + $"[-{Arguments.StoragePath} <path>]"
                   + "|"
                   + $"[-{Arguments.StorageAccountName} <azure-acc> "
                   + $"-{Arguments.StorageKeyValue} <azure-key> "
                   + $"-{Arguments.StorageContainer} <azure-container> "
                   + $"-{Arguments.StoragePath} <path> "
                   + $"[-{Arguments.VaultName} <keyvault-name> "
                   + $"-{Arguments.ClientId} <keyvault-client-id> "
                   + $"-{Arguments.CertificateThumbprint} <keyvault-certificate-thumbprint> "
                   + $"[-{Arguments.ValidateCertificate} true|false]]] "
                   + $"-{Arguments.StorageTypeAuditing} file|azure "
                   + $"[-{Arguments.StoragePathAuditing} <path>]"
                   + "|"
                   + $"[-{Arguments.StorageAccountNameAuditing} <azure-acc> "
                   + $"-{Arguments.StorageKeyValueAuditing} <azure-key> "
                   + $"-{Arguments.StorageContainerAuditing} <azure-container> "
                   + $"-{Arguments.StoragePathAuditing} <path>] "
                   + $"[-{Arguments.Verbose} true|false] "
                   + $"[-{Arguments.Interval} <seconds>] "
                   + $"[-{Arguments.StartDate} <DateTime>]";
        }

        protected override void Init(IDictionary<string, string> arguments, CancellationToken cancellationToken)
        {
            Gallery = arguments.GetOrThrow<string>(Arguments.Gallery);
            Verbose = arguments.GetOrDefault(Arguments.Verbose, false);
            StartDate = arguments.GetOrDefault(Arguments.StartDate, DateTimeMinValueUtc);

            var catalogStorageFactory = CommandHelpers.CreateStorageFactory(arguments, Verbose);
            var auditingStorageFactory = CommandHelpers.CreateSuffixedStorageFactory("Auditing", arguments, Verbose);

            Logger.LogInformation("CONFIG source: \"{ConfigSource}\" storage: \"{Storage}\"", Gallery, catalogStorageFactory);

            CatalogStorage = catalogStorageFactory.Create();
            AuditingStorage = auditingStorageFactory.Create();

            Top = 20;
            Timeout = TimeSpan.FromSeconds(300);
        }

        protected override async Task RunInternal(CancellationToken cancellationToken)
        {
            using (var client = CreateHttpClient())
            {
                client.Timeout = Timeout;

                // baseline timestamps
                var catalogProperties = await FeedHelpers.GetCatalogPropertiesAsync(CatalogStorage, TelemetryService, cancellationToken);
                var lastCreated = catalogProperties.LastCreated ?? (StartDate ?? DateTimeMinValueUtc);
                var lastEdited = catalogProperties.LastEdited ?? lastCreated;
                var lastDeleted = catalogProperties.LastDeleted ?? lastCreated;

                if (lastDeleted == DateTime.MinValue.ToUniversalTime())
                {
                    lastDeleted = lastCreated;
                }

                // fetch and add all DELETED packages
                if (lastDeleted > DateTime.MinValue.ToUniversalTime())
                {
                    SortedList<DateTime, IList<FeedPackageIdentity>> deletedPackages;
                    var previousLastDeleted = DateTime.MinValue;
                    do
                    {
                        // Get deleted packages
                        Logger.LogInformation("CATALOG LastDeleted: {CatalogDeletedTime}", lastDeleted.ToString("O"));

                        deletedPackages = await GetDeletedPackages(AuditingStorage, lastDeleted);

                        Logger.LogInformation("FEED DeletedPackages: {DeletedPackagesCount}", deletedPackages.Count);

                        // We want to ensure a commit only contains each package once at most.
                        // Therefore we segment by package id + version.
                        var deletedPackagesSegments = SegmentPackageDeletes(deletedPackages);
                        foreach (var deletedPackagesSegment in deletedPackagesSegments)
                        {
                            lastDeleted = await Deletes2Catalog(
                                deletedPackagesSegment, CatalogStorage, lastCreated, lastEdited, lastDeleted, cancellationToken);

                            // Wait for one second to ensure the next catalog commit gets a new timestamp
                            Thread.Sleep(TimeSpan.FromSeconds(1));
                        }

                        if (previousLastDeleted == lastDeleted)
                        {
                            break;
                        }
                        previousLastDeleted = lastDeleted;
                    }
                    while (deletedPackages.Count > 0);
                }

                //  THEN fetch and add all newly CREATED packages - in order
                SortedList<DateTime, IList<FeedPackageDetails>> createdPackages;
                var previousLastCreated = DateTime.MinValue;
                do
                {
                    Logger.LogInformation("CATALOG LastCreated: {CatalogLastCreatedTime}", lastCreated.ToString("O"));

                    createdPackages = await GetCreatedPackages(client, Gallery, lastCreated, Top);
                    Logger.LogInformation("FEED CreatedPackages: {CreatedPackagesCount}", createdPackages.Count);

                    lastCreated = await FeedHelpers.DownloadMetadata2Catalog(
                        client,
                        createdPackages,
                        CatalogStorage,
                        lastCreated,
                        lastEdited,
                        lastDeleted,
                        createdPackages: true,
                        cancellationToken: cancellationToken,
                        telemetryService: TelemetryService,
                        logger: Logger);
                    if (previousLastCreated == lastCreated)
                    {
                        break;
                    }
                    previousLastCreated = lastCreated;
                }
                while (createdPackages.Count > 0);

                //  THEN fetch and add all EDITED packages - in order
                SortedList<DateTime, IList<FeedPackageDetails>> editedPackages;
                var previousLastEdited = DateTime.MinValue;
                do
                {
                    Logger.LogInformation("CATALOG LastEdited: {CatalogLastEditedTime}", lastEdited.ToString("O"));

                    editedPackages = await GetEditedPackages(client, Gallery, lastEdited, Top);

                    Logger.LogInformation("FEED EditedPackages: {EditedPackagesCount}", editedPackages.Count);

                    lastEdited = await FeedHelpers.DownloadMetadata2Catalog(
                        client,
                        editedPackages,
                        CatalogStorage,
                        lastCreated,
                        lastEdited,
                        lastDeleted,
                        createdPackages: false,
                        cancellationToken: cancellationToken,
                        telemetryService: TelemetryService,
                        logger: Logger);
                    if (previousLastEdited == lastEdited)
                    {
                        break;
                    }
                    previousLastEdited = lastEdited;
                }
                while (editedPackages.Count > 0);
            }
        }

        // Wrapper function for CatalogUtility.CreateHttpClient
        // Overriden by NgTests.TestableFeed2CatalogJob
        protected virtual HttpClient CreateHttpClient()
        {
            return FeedHelpers.CreateHttpClient(CommandHelpers.GetHttpMessageHandlerFactory(TelemetryService, Verbose));
        }

        private static Uri MakeCreatedUri(string source, DateTime since, int top)
        {
            var address = string.Format("{0}/Packages?$filter=Created gt DateTime'{1}'&$orderby=Created" + FeedUrlFormatSuffix,
                source.Trim('/'),
                since.ToString("o"),
                top);

            return new Uri(address);
        }

        private static Uri MakeLastEditedUri(string source, DateTime since, int top)
        {
            var address = string.Format("{0}/Packages?$filter=LastEdited gt DateTime'{1}'&$orderby=LastEdited" + FeedUrlFormatSuffix,
                source.Trim('/'),
                since.ToString("o"),
                top);

            return new Uri(address);
        }

        private static Task<SortedList<DateTime, IList<FeedPackageDetails>>> GetCreatedPackages(HttpClient client, string source, DateTime since, int top)
        {
            return FeedHelpers.GetPackagesInOrder(client, MakeCreatedUri(source, since, top), package => package.CreatedDate);
        }

        private static Task<SortedList<DateTime, IList<FeedPackageDetails>>> GetEditedPackages(HttpClient client, string source, DateTime since, int top)
        {
            return FeedHelpers.GetPackagesInOrder(client, MakeLastEditedUri(source, since, top), package => package.LastEditedDate);
        }

        private async Task<SortedList<DateTime, IList<FeedPackageIdentity>>> GetDeletedPackages(IStorage auditingStorage, DateTime since)
        {
            var result = new SortedList<DateTime, IList<FeedPackageIdentity>>();

            // Get all audit blobs (based on their filename which starts with a date that can be parsed)
            // NOTE we're getting more files than needed (to account for a time difference between servers)
            var minimumFileTime = since.AddMinutes(-15);
            var auditEntries = await DeletionAuditEntry.GetAsync(auditingStorage, CancellationToken.None,
                minTime: minimumFileTime, logger: Logger);

            foreach (var auditEntry in auditEntries)
            {
                if (!string.IsNullOrEmpty(auditEntry.PackageId) && !string.IsNullOrEmpty(auditEntry.PackageVersion) && auditEntry.TimestampUtc > since)
                {
                    // Mark the package "deleted"
                    IList<FeedPackageIdentity> packages;
                    if (!result.TryGetValue(auditEntry.TimestampUtc.Value, out packages))
                    {
                        packages = new List<FeedPackageIdentity>();
                        result.Add(auditEntry.TimestampUtc.Value, packages);
                    }

                    packages.Add(new FeedPackageIdentity(auditEntry.PackageId, auditEntry.PackageVersion));
                }
            }

            return result;
        }

        private static IEnumerable<SortedList<DateTime, IList<FeedPackageIdentity>>> SegmentPackageDeletes(SortedList<DateTime, IList<FeedPackageIdentity>> packageDeletes)
        {
            var packageIdentityTracker = new HashSet<string>();
            var currentSegment = new SortedList<DateTime, IList<FeedPackageIdentity>>();
            foreach (var entry in packageDeletes)
            {
                if (!currentSegment.ContainsKey(entry.Key))
                {
                    currentSegment.Add(entry.Key, new List<FeedPackageIdentity>());
                }

                var curentSegmentPackages = currentSegment[entry.Key];
                foreach (var packageIdentity in entry.Value)
                {
                    var key = packageIdentity.Id + "|" + packageIdentity.Version;
                    if (packageIdentityTracker.Contains(key))
                    {
                        // Duplicate, return segment
                        yield return currentSegment;

                        // Clear current segment
                        currentSegment.Clear();
                        currentSegment.Add(entry.Key, new List<FeedPackageIdentity>());
                        curentSegmentPackages = currentSegment[entry.Key];
                        packageIdentityTracker.Clear();
                    }

                    // Add to segment
                    curentSegmentPackages.Add(packageIdentity);
                    packageIdentityTracker.Add(key);
                }
            }

            if (currentSegment.Any())
            {
                yield return currentSegment;
            }
        }

        private async Task<DateTime> Deletes2Catalog(
            SortedList<DateTime, IList<FeedPackageIdentity>> packages,
            IStorage storage,
            DateTime lastCreated,
            DateTime lastEdited,
            DateTime lastDeleted,
            CancellationToken cancellationToken)
        {
            var writer = new AppendOnlyCatalogWriter(
                storage,
                TelemetryService,
                maxPageSize: 550);

            if (packages == null || packages.Count == 0)
            {
                return lastDeleted;
            }

            foreach (var entry in packages)
            {
                foreach (var packageIdentity in entry.Value)
                {
                    var catalogItem = new DeleteCatalogItem(packageIdentity.Id, packageIdentity.Version, entry.Key);
                    writer.Add(catalogItem);

                    Logger.LogInformation("Delete: {PackageId} {PackageVersion}", packageIdentity.Id, packageIdentity.Version);
                }

                lastDeleted = entry.Key;
            }

            var commitMetadata = PackageCatalog.CreateCommitMetadata(writer.RootUri, new CommitMetadata(lastCreated, lastEdited, lastDeleted));

            await writer.Commit(commitMetadata, cancellationToken);

            Logger.LogInformation("COMMIT package deletes to catalog.");

            return lastDeleted;
        }
    }
}