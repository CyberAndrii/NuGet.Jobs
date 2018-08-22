﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.DataMovement;
using Microsoft.WindowsAzure.Storage.RetryPolicies;

namespace NuGet.Services.Metadata.Catalog.Persistence
{
    public class AzureStorage : Storage, IAzureStorage
    {
        private readonly bool _compressContent;
        private readonly CloudBlobDirectory _directory;
        private readonly BlobRequestOptions _blobRequestOptions;
        private readonly bool _useServerSideCopy;

        public static readonly TimeSpan DefaultServerTimeout = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan DefaultMaxExecutionTime = TimeSpan.FromMinutes(10);

        public AzureStorage(
            CloudStorageAccount account,
            string containerName,
            string path,
            Uri baseAddress,
            bool useServerSideCopy,
            bool compressContent,
            bool verbose)
            : this(
                  account,
                  containerName,
                  path,
                  baseAddress,
                  DefaultMaxExecutionTime,
                  DefaultServerTimeout,
                  useServerSideCopy,
                  compressContent,
                  verbose)
        {
        }

        public AzureStorage(
            CloudStorageAccount account,
            string containerName,
            string path,
            Uri baseAddress,
            TimeSpan maxExecutionTime,
            TimeSpan serverTimeout,
            bool useServerSideCopy,
            bool compressContent,
            bool verbose)
           : this(account.CreateCloudBlobClient().GetContainerReference(containerName).GetDirectoryReference(path),
                 baseAddress,
                 maxExecutionTime,
                 serverTimeout)
        {
            _useServerSideCopy = useServerSideCopy;
            _compressContent = compressContent;
            Verbose = verbose;
        }

        private AzureStorage(CloudBlobDirectory directory, Uri baseAddress, TimeSpan maxExecutionTime, TimeSpan serverTimeout)
            : base(baseAddress ?? GetDirectoryUri(directory))
        {
            _directory = directory;

            if (_directory.Container.CreateIfNotExists())
            {
                _directory.Container.SetPermissions(new BlobContainerPermissions { PublicAccess = BlobContainerPublicAccessType.Blob });

                if (Verbose)
                {
                    Trace.WriteLine(String.Format("Created '{0}' publish container", _directory.Container.Name));
                }
            }

            _blobRequestOptions = new BlobRequestOptions()
            {
                ServerTimeout = serverTimeout,
                MaximumExecutionTime = maxExecutionTime,
                RetryPolicy = new ExponentialRetry()
            };
        }

        public override async Task<OptimisticConcurrencyControlToken> GetOptimisticConcurrencyControlTokenAsync(
            Uri resourceUri,
            CancellationToken cancellationToken)
        {
            if (resourceUri == null)
            {
                throw new ArgumentNullException(nameof(resourceUri));
            }

            cancellationToken.ThrowIfCancellationRequested();

            string blobName = GetName(resourceUri);
            CloudBlockBlob blob = _directory.GetBlockBlobReference(blobName);

            await blob.FetchAttributesAsync(cancellationToken);

            return new OptimisticConcurrencyControlToken(blob.Properties.ETag);
        }

        private static Uri GetDirectoryUri(CloudBlobDirectory directory)
        {
            Uri uri = new UriBuilder(directory.Uri)
            {
                Scheme = "http",
                Port = 80
            }.Uri;

            return uri;
        }

        //Blob exists
        public override bool Exists(string fileName)
        {
            Uri packageRegistrationUri = ResolveUri(fileName);
            string blobName = GetName(packageRegistrationUri);

            CloudBlockBlob blob = _directory.GetBlockBlobReference(blobName);

            if (blob.Exists())
            {
                return true;
            }
            if (Verbose)
            {
                Trace.WriteLine(String.Format("The blob {0} does not exist.", packageRegistrationUri));
            }
            return false;
        }

        public override async Task<IEnumerable<StorageListItem>> ListAsync(CancellationToken cancellationToken)
        {
            var files = await _directory.ListBlobsAsync(cancellationToken);

            return files.Select(GetStorageListItem).AsEnumerable();
        }

        private StorageListItem GetStorageListItem(IListBlobItem listBlobItem)
        {
            var lastModified = (listBlobItem as CloudBlockBlob)?.Properties.LastModified?.UtcDateTime;

            return new StorageListItem(listBlobItem.Uri, lastModified);
        }

        protected override async Task OnCopyAsync(
            Uri sourceUri,
            IStorage destinationStorage,
            Uri destinationUri,
            IReadOnlyDictionary<string, string> destinationProperties,
            CancellationToken cancellationToken)
        {
            var azureDestinationStorage = destinationStorage as AzureStorage;

            if (azureDestinationStorage == null)
            {
                throw new NotImplementedException("Copying is only supported from Azure storage to Azure storage.");
            }

            string sourceName = GetName(sourceUri);
            string destinationName = azureDestinationStorage.GetName(destinationUri);

            CloudBlockBlob sourceBlob = _directory.GetBlockBlobReference(sourceName);
            CloudBlockBlob destinationBlob = azureDestinationStorage._directory.GetBlockBlobReference(destinationName);

            var context = new SingleTransferContext();

            if (destinationProperties?.Count > 0)
            {
                context.SetAttributesCallback = new SetAttributesCallback((destination) =>
                {
                    var blob = (CloudBlockBlob)destination;

                    // The copy statement copied all properties from the source blob to the destination blob; however,
                    // there may be required properties on destination blob, all of which may have not already existed
                    // on the source blob at the time of copy.
                    foreach (var property in destinationProperties)
                    {
                        switch (property.Key)
                        {
                            case StorageConstants.CacheControl:
                                blob.Properties.CacheControl = property.Value;
                                break;

                            case StorageConstants.ContentType:
                                blob.Properties.ContentType = property.Value;
                                break;

                            default:
                                throw new NotImplementedException($"Storage property '{property.Value}' is not supported.");
                        }
                    }
                });
            }

            context.ShouldOverwriteCallback = new ShouldOverwriteCallback((source, destination) => true);

            await TransferManager.CopyAsync(sourceBlob, destinationBlob, _useServerSideCopy, options: null, context: context);
        }

        protected override async Task OnSaveAsync(Uri resourceUri, StorageContent content, CancellationToken cancellationToken)
        {
            string name = GetName(resourceUri);

            CloudBlockBlob blob = _directory.GetBlockBlobReference(name);
            blob.Properties.ContentType = content.ContentType;
            blob.Properties.CacheControl = content.CacheControl;

            if (_compressContent)
            {
                blob.Properties.ContentEncoding = "gzip";
                using (Stream stream = content.GetContentStream())
                {
                    MemoryStream destinationStream = new MemoryStream();

                    using (GZipStream compressionStream = new GZipStream(destinationStream, CompressionMode.Compress, true))
                    {
                        await stream.CopyToAsync(compressionStream);
                    }

                    destinationStream.Seek(0, SeekOrigin.Begin);

                    await blob.UploadFromStreamAsync(destinationStream,
                        accessCondition: null,
                        options: _blobRequestOptions,
                        operationContext: null,
                        cancellationToken: cancellationToken);

                    Trace.WriteLine(string.Format("Saved compressed blob {0} to container {1}", blob.Uri.ToString(), _directory.Container.Name));
                }
            }
            else
            {
                using (Stream stream = content.GetContentStream())
                {
                    await blob.UploadFromStreamAsync(stream,
                        accessCondition: null,
                        options: _blobRequestOptions,
                        operationContext: null,
                        cancellationToken: cancellationToken);
                }

                Trace.WriteLine(string.Format("Saved uncompressed blob {0} to container {1}", blob.Uri.ToString(), _directory.Container.Name));
            }

            await TryTakeBlobSnapshotAsync(blob);
        }

        /// <summary>
        /// Take one snapshot only if there is not any snapshot for the specific blob
        /// This will prevent the blob to be deleted by a not intended delete action
        /// </summary>
        /// <param name="blob"></param>
        /// <returns></returns>
        private async Task<bool> TryTakeBlobSnapshotAsync(CloudBlockBlob blob)
        {
            if (blob == null)
            {
                //no action
                return false;
            }

            var stopwatch = Stopwatch.StartNew();

            try
            {
                var allSnapshots = blob.Container.
                                   ListBlobs(prefix: blob.Name,
                                             useFlatBlobListing: true,
                                             blobListingDetails: BlobListingDetails.Snapshots);
                //the above call will return at least one blob the original
                if (allSnapshots.Count() == 1)
                {
                    var snapshot = await blob.CreateSnapshotAsync();
                    stopwatch.Stop();
                    Trace.WriteLine($"SnapshotCreated:milliseconds={stopwatch.ElapsedMilliseconds}:{blob.Uri.ToString()}:{snapshot.SnapshotQualifiedUri}");
                }
                return true;
            }
            catch (StorageException storageException)
            {
                stopwatch.Stop();
                Trace.WriteLine($"EXCEPTION:milliseconds={stopwatch.ElapsedMilliseconds}:CreateSnapshot: Failed to take the snapshot for blob {blob.Uri.ToString()}. Exception{storageException.ToString()}");
                return false;
            }
        }

        protected override async Task<StorageContent> OnLoadAsync(Uri resourceUri, CancellationToken cancellationToken)
        {
            // the Azure SDK will treat a starting / as an absolute URL,
            // while we may be working in a subdirectory of a storage container
            // trim the starting slash to treat it as a relative path
            string name = GetName(resourceUri).TrimStart('/');

            CloudBlockBlob blob = _directory.GetBlockBlobReference(name);

            if (blob.Exists())
            {
                MemoryStream originalStream = new MemoryStream();
                await blob.DownloadToStreamAsync(originalStream,
                                                 accessCondition: null,
                                                 options: _blobRequestOptions,
                                                 operationContext: null,
                                                 cancellationToken: cancellationToken);

                originalStream.Seek(0, SeekOrigin.Begin);

                string content;

                if (blob.Properties.ContentEncoding == "gzip")
                {
                    using (var uncompressedStream = new GZipStream(originalStream, CompressionMode.Decompress))
                    {
                        using (var reader = new StreamReader(uncompressedStream))
                        {
                            content = await reader.ReadToEndAsync();
                        }
                    }
                }
                else
                {
                    using (var reader = new StreamReader(originalStream))
                    {
                        content = await reader.ReadToEndAsync();
                    }
                }

                return new StringStorageContent(content);
            }

            if (Verbose)
            {
                Trace.WriteLine(String.Format("Can't load '{0}'. Blob doesn't exist", resourceUri));
            }

            return null;
        }

        protected override async Task OnDeleteAsync(Uri resourceUri, CancellationToken cancellationToken)
        {
            string name = GetName(resourceUri);

            CloudBlockBlob blob = _directory.GetBlockBlobReference(name);
            await blob.DeleteAsync(deleteSnapshotsOption: DeleteSnapshotsOption.IncludeSnapshots,
                                   accessCondition: null,
                                   options: _blobRequestOptions,
                                   operationContext: null,
                                   cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Returns the uri of the blob based on the Azure cloud directory
        /// </summary>
        /// <param name="name">The blob name.</param>
        /// <returns>The blob uri.</returns>
        public override Uri GetUri(string name)
        {
            return new Uri(_directory.Uri, name);
        }

        public override async Task<bool> AreSynchronized(Uri firstResourceUri, Uri secondResourceUri)
        {
            var destination = _directory.GetBlockBlobReference(GetName(secondResourceUri));
            var source = new CloudBlockBlob(firstResourceUri);
            if (await destination.ExistsAsync())
            {
                if (await source.ExistsAsync())
                {
                    return !string.IsNullOrEmpty(source.Properties.ContentMD5) && source.Properties.ContentMD5 == destination.Properties.ContentMD5;
                }
                return true;
            }
            return !(await source.ExistsAsync());
        }

        public async Task<ICloudBlockBlob> GetCloudBlockBlobReferenceAsync(Uri blobUri)
        {
            string blobName = GetName(blobUri);
            CloudBlockBlob blob = _directory.GetBlockBlobReference(blobName);
            var blobExists = await blob.ExistsAsync();

            if (Verbose && !blobExists)
            {
                Trace.WriteLine($"The blob {blobUri.AbsoluteUri} does not exist.");
            }

            return new AzureCloudBlockBlob(blob);
        }

        public async Task<bool> HasPropertiesAsync(Uri blobUri, string contentType, string cacheControl)
        {
            var blobName = GetName(blobUri);
            var blob = _directory.GetBlockBlobReference(blobName);

            if (await blob.ExistsAsync())
            {
                await blob.FetchAttributesAsync();

                return string.Equals(blob.Properties.ContentType, contentType)
                    && string.Equals(blob.Properties.CacheControl, cacheControl);
            }

            return false;
        }
    }
}