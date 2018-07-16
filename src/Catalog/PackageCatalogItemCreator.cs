﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NuGet.Services.Metadata.Catalog.Helpers;
using NuGet.Services.Metadata.Catalog.Persistence;

namespace NuGet.Services.Metadata.Catalog
{
    public sealed class PackageCatalogItemCreator
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;
        private readonly IAzureStorage _storage;
        private readonly ITelemetryService _telemetryService;

        private PackageCatalogItemCreator(
            HttpClient httpClient,
            ITelemetryService telemetryService,
            ILogger logger,
            IStorage storage)
        {
            _httpClient = httpClient;
            _telemetryService = telemetryService;
            _logger = logger;
            _storage = storage as IAzureStorage;
        }

        public static PackageCatalogItemCreator Create(
            HttpClient httpClient,
            ITelemetryService telemetryService,
            ILogger logger,
            IStorage storage)
        {
            if (httpClient == null)
            {
                throw new ArgumentNullException(nameof(httpClient));
            }

            if (telemetryService == null)
            {
                throw new ArgumentNullException(nameof(telemetryService));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            return new PackageCatalogItemCreator(httpClient, telemetryService, logger, storage);
        }

        public async Task<PackageCatalogItem> CreateAsync(
            FeedPackageDetails packageItem,
            DateTime timestamp,
            CancellationToken cancellationToken)
        {
            if (packageItem == null)
            {
                throw new ArgumentNullException(nameof(packageItem));
            }

            cancellationToken.ThrowIfCancellationRequested();

            PackageCatalogItem item = null;

            if (_storage != null)
            {
                item = await GetPackageViaStorageAsync(packageItem, cancellationToken);
            }

            if (item == null)
            {
                item = await GetPackageViaHttpAsync(packageItem, timestamp, item, cancellationToken);
            }

            return item;
        }

        private async Task<PackageCatalogItem> GetPackageViaStorageAsync(FeedPackageDetails packageItem, CancellationToken cancellationToken)
        {
            PackageCatalogItem item = null;
            var packageFileName = PackageUtility.GetPackageFileNameLowercase(packageItem.PackageId, packageItem.PackageVersion);
            var blob = await _storage.GetCloudBlockBlobReferenceAsync(packageFileName);

            if (blob == null)
            {
                _telemetryService.TrackMetric(
                    TelemetryConstants.NonExistentBlob,
                    metric: 1,
                    properties: GetProperties(packageItem, blob));

                return item;
            }

            await blob.FetchAttributesAsync(cancellationToken);

            string packageHash = null;
            var etag = blob.ETag;

            var metadata = await blob.GetMetadataAsync(cancellationToken);

            if (metadata.TryGetValue(Constants.Sha512, out packageHash))
            {
                using (var stream = await blob.GetStreamAsync(cancellationToken))
                {
                    item = Utils.CreateCatalogItem(
                        packageItem.ContentUri.ToString(),
                        stream,
                        packageItem.CreatedDate,
                        packageItem.LastEditedDate,
                        packageItem.PublishedDate,
                        licenseNames: null,
                        licenseReportUrl: null,
                        packageHash: packageHash);

                    if (item == null)
                    {
                        _logger.LogWarning("Unable to extract metadata from: {PackageDetailsContentUri}", packageItem.ContentUri);
                    }
                }

                if (item != null)
                {
                    // Since obtaining the ETag the first time, it's possible (though unlikely) that the blob may
                    // have changed.  Although reading a blob with a single GET request should return the whole
                    // blob in a consistent state, we're reading the blob using ZipArchive and a seekable stream,
                    // which results in many GET requests.  To guard against the blob having changed since we
                    // obtained the package hash, we check the ETag one more time.  If this check fails, we'll
                    // fallback to using a single HTTP GET request.
                    await blob.FetchAttributesAsync(cancellationToken);

                    if (etag != blob.ETag)
                    {
                        item = null;

                        _telemetryService.TrackMetric(
                            TelemetryConstants.BlobModified,
                            metric: 1,
                            properties: GetProperties(packageItem, blob));
                    }
                }
            }
            else
            {
                _telemetryService.TrackMetric(
                    TelemetryConstants.NonExistentPackageHash,
                    metric: 1,
                    properties: GetProperties(packageItem, blob));
            }

            return item;
        }

        private async Task<PackageCatalogItem> GetPackageViaHttpAsync(FeedPackageDetails packageItem, DateTime timestamp, PackageCatalogItem item, CancellationToken cancellationToken)
        {
            // When downloading the package binary, add a query string parameter
            // that corresponds to the operation's timestamp.
            // This query string will ensure the package is not cached
            // (e.g. on the CDN) and returns the "latest and greatest" package metadata.
            var packageUri = Utilities.GetNugetCacheBustingUri(packageItem.ContentUri, timestamp.ToString("O"));
            HttpResponseMessage response = null;

            try
            {
                using (_telemetryService.TrackDuration(
                    TelemetryConstants.PackageDownloadSeconds,
                    new Dictionary<string, string>()
                    {
                        { TelemetryConstants.Id, packageItem.PackageId?.ToLowerInvariant() },
                        { TelemetryConstants.Version, packageItem.PackageVersion?.ToLowerInvariant() },
                    }))
                {
                    response = await _httpClient.GetAsync(packageUri, cancellationToken);
                }
            }
            catch (TaskCanceledException tce)
            {
                // If the HTTP request timed out, a TaskCanceledException will be thrown.
                throw new HttpClientTimeoutException($"HttpClient request timed out in {nameof(FeedHelpers.DownloadMetadata2Catalog)}.", tce);
            }

            if (response.IsSuccessStatusCode)
            {
                using (var stream = await response.Content.ReadAsStreamAsync())
                {
                    item = Utils.CreateCatalogItem(
                        packageItem.ContentUri.ToString(),
                        stream,
                        packageItem.CreatedDate,
                        packageItem.LastEditedDate,
                        packageItem.PublishedDate);

                    if (item == null)
                    {
                        _logger.LogWarning("Unable to extract metadata from: {PackageDetailsContentUri}", packageItem.ContentUri);
                    }
                }
            }
            else
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    //  the feed is out of sync with the actual package storage - if we don't have the package there is nothing to be done we might as well move onto the next package
                    _logger?.LogWarning("Unable to download: {PackageDetailsContentUri}. Http status: {HttpStatusCode}", packageItem.ContentUri, response.StatusCode);
                }
                else
                {
                    //  this should trigger a restart - of this program - and not move the cursor forward
                    _logger?.LogError("Unable to download: {PackageDetailsContentUri}. Http status: {HttpStatusCode}", packageItem.ContentUri, response.StatusCode);
                    throw new Exception(
                        $"Unable to download: {packageItem.ContentUri} http status: {response.StatusCode}");
                }
            }

            return item;
        }

        private static Dictionary<string, string> GetProperties(FeedPackageDetails packageItem, ICloudBlockBlob blob)
        {
            var properties = new Dictionary<string, string>()
            {
                { TelemetryConstants.Id, packageItem.PackageId.ToLowerInvariant() },
                { TelemetryConstants.Version, packageItem.PackageVersion.ToLowerInvariant() }
            };

            if (blob != null)
            {
                properties.Add(TelemetryConstants.Uri, blob.Uri.AbsoluteUri);
            }

            return properties;
        }
    }
}