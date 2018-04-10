﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NuGet.Services.Metadata.Catalog.Persistence;
using NuGet.Versioning;

namespace NuGet.Services.Metadata.Catalog.Dnx
{
    public class DnxMaker
    {
        private readonly StorageFactory _storageFactory;

        public class DnxEntry
        {
            public Uri Nupkg { get; set; }
            public Uri Nuspec { get; set; }
        }

        public DnxMaker(StorageFactory storageFactory)
        {
            _storageFactory = storageFactory ?? throw new ArgumentNullException(nameof(storageFactory));
        }

        public Task AddPackageToIndex(
            string id,
            string version,
            CancellationToken cancellationToken)
        {
            var storage = _storageFactory.Create(id);

            return AddPackageToIndex(storage, id, version, cancellationToken);
        }

        public async Task<DnxEntry> AddPackage(
            Stream nupkgStream,
            string nuspec,
            string id,
            string version,
            CancellationToken cancellationToken)
        {
            var storage = _storageFactory.Create(id);

            var nuspecUri = await SaveNuspec(storage, id, version, nuspec, cancellationToken);
            var nupkgUri = await SaveNupkg(nupkgStream, storage, id, version, cancellationToken);
            await AddPackageToIndex(storage, id, version, cancellationToken);

            return new DnxEntry { Nupkg = nupkgUri, Nuspec = nuspecUri };
        }

        private Task AddPackageToIndex(
            Storage storage,
            string id,
            string version,
            CancellationToken cancellationToken)
        {
            return UpdateMetadata(storage, versions => versions.Add(NuGetVersion.Parse(version)), cancellationToken);
        }

        public async Task DeletePackage(string id, string version, CancellationToken cancellationToken)
        {
            var storage = _storageFactory.Create(id);

            await UpdateMetadata(storage, versions => versions.Remove(NuGetVersion.Parse(version)), cancellationToken);
            await DeleteNuspec(storage, id, version, cancellationToken);
            await DeleteNupkg(storage, id, version, cancellationToken);
        }

        public async Task<bool> HasPackageInIndex(Storage storage, string id, string version, CancellationToken cancellationToken)
        {
            var versionsContext = await GetVersionsAsync(storage, cancellationToken);
            var parsedVersion = NuGetVersion.Parse(version);
            return versionsContext.Versions.Contains(parsedVersion);
        }

        private async Task<Uri> SaveNuspec(Storage storage, string id, string version, string nuspec, CancellationToken cancellationToken)
        {
            var relativeAddress = GetRelativeAddressNuspec(id, version);
            var nuspecUri = new Uri(storage.BaseAddress, relativeAddress);
            await storage.Save(nuspecUri, new StringStorageContent(nuspec, "text/xml", "max-age=120"), cancellationToken);
            return nuspecUri;
        }

        private async Task UpdateMetadata(Storage storage, Action<HashSet<NuGetVersion>> updateAction, CancellationToken cancellationToken)
        {
            var versionsContext = await GetVersionsAsync(storage, cancellationToken);
            var relativeAddress = versionsContext.RelativeAddress;
            var resourceUri = versionsContext.ResourceUri;
            var versions = versionsContext.Versions;

            updateAction(versions);
            List<NuGetVersion> result = new List<NuGetVersion>(versions);

            if (result.Any())
            {
                // Store versions (sorted)
                result.Sort();
                await storage.Save(resourceUri, CreateContent(result.Select((v) => v.ToString())), cancellationToken);
            }
            else
            {
                // Remove versions file if no versions are present
                if (storage.Exists(relativeAddress))
                {
                    await storage.Delete(resourceUri, cancellationToken);
                }
            }
        }

        private async Task<VersionsResult> GetVersionsAsync(Storage storage, CancellationToken cancellationToken)
        {
            var relativeAddress = "index.json";
            var resourceUri = new Uri(storage.BaseAddress, relativeAddress);
            var versions = GetVersions(await storage.LoadString(resourceUri, cancellationToken));

            return new VersionsResult(relativeAddress, resourceUri, versions);
        }

        private static HashSet<NuGetVersion> GetVersions(string json)
        {
            var result = new HashSet<NuGetVersion>();
            if (json != null)
            {
                JObject obj = JObject.Parse(json);

                JArray versions = obj["versions"] as JArray;

                if (versions != null)
                {
                    foreach (JToken version in versions)
                    {
                        result.Add(NuGetVersion.Parse(version.ToString()));
                    }
                }
            }
            return result;
        }

        private StorageContent CreateContent(IEnumerable<string> versions)
        {
            JObject obj = new JObject { { "versions", new JArray(versions) } };
            return new StringStorageContent(obj.ToString(), "application/json", "no-store");
        }

        private async Task<Uri> SaveNupkg(Stream nupkgStream, Storage storage, string id, string version, CancellationToken cancellationToken)
        {
            Uri nupkgUri = new Uri(storage.BaseAddress, GetRelativeAddressNupkg(id, version));
            await storage.Save(nupkgUri, new StreamStorageContent(nupkgStream, "application/octet-stream", "max-age=120"), cancellationToken);
            return nupkgUri;
        }

        private async Task DeleteNuspec(Storage storage, string id, string version, CancellationToken cancellationToken)
        {
            string relativeAddress = GetRelativeAddressNuspec(id, version);
            Uri nuspecUri = new Uri(storage.BaseAddress, relativeAddress);
            if (storage.Exists(relativeAddress))
            {
                await storage.Delete(nuspecUri, cancellationToken);
            }
        }

        private async Task DeleteNupkg(Storage storage, string id, string version, CancellationToken cancellationToken)
        {
            string relativeAddress = GetRelativeAddressNupkg(id, version);
            Uri nupkgUri = new Uri(storage.BaseAddress, relativeAddress);
            if (storage.Exists(relativeAddress))
            {
                await storage.Delete(nupkgUri, cancellationToken);
            }
        }

        private static string GetRelativeAddressNuspec(string id, string version)
        {
            return $"{NuGetVersion.Parse(version).ToNormalizedString()}/{id}.nuspec"; 
        }

        public static string GetRelativeAddressNupkg(string id, string version)
        {
            return $"{NuGetVersion.Parse(version).ToNormalizedString()}/{id}.{NuGetVersion.Parse(version).ToNormalizedString()}.nupkg";
        }

        private class VersionsResult
        {
            public VersionsResult(string relativeAddress, Uri resourceUri, HashSet<NuGetVersion> versions)
            {
                RelativeAddress = relativeAddress;
                ResourceUri = resourceUri;
                Versions = versions;
            }

            public string RelativeAddress { get; }
            public Uri ResourceUri { get; }
            public HashSet<NuGetVersion> Versions { get; }
        }
    }
}
