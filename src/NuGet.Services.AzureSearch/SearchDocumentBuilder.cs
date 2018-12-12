﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using NuGet.Protocol.Catalog;
using NuGet.Services.Entities;

namespace NuGet.Services.AzureSearch
{
    public class SearchDocumentBuilder : ISearchDocumentBuilder
    {
        public SearchDocument.LatestFlags LatestFlagsOrNull(VersionLists versionLists, SearchFilters searchFilters)
        {
            var latest = versionLists.GetLatestVersionInfoOrNull(searchFilters);
            if (latest == null)
            {
                return null;
            }

            // The latest version, given the "include prerelease" bit of the search filter, may or may not be the
            // absolute latest version when considering both prerelease and stable versions. Consider the following
            // cases:
            //
            // Case #1:
            //   SearchFilters.Default:
            //     All versions: 1.0.0, 2.0.0-alpha
            //     Latest version given filters: 1.0.0
            //     V2 search document flags:
            //       IsLatestStable = true
            //       IsLatest       = false
            //
            // Case #2:
            //   SearchFilters.Default:
            //     All versions: 1.0.0
            //     Latest version given filters: 1.0.0
            //     V2 search document flags:
            //       IsLatestStable = true
            //       IsLatest       = true
            //
            // Case #3:
            //   SearchFilters.IncludePrerelease:
            //     All versions: 1.0.0, 2.0.0-alpha
            //     Latest version given filters: 2.0.0-alpha
            //     V2 search document flags:
            //       IsLatestStable = false
            //       IsLatest       = true
            //
            // Case #4:
            //   SearchFilters.IncludePrerelease:
            //     All versions: 1.0.0
            //     Latest version given filters: 1.0.0
            //     V2 search document flags:
            //       IsLatestStable = true
            //       IsLatest       = true
            //
            // In cases #1 and #2, we know the value of IsLatestStable will always be true. We cannot know whether
            // IsLatest is true or false without looking at the version list that includes prerelease versions. For
            // cases #3 and #4, we know IsLatest will always be true and we can determine IsLatestStable by looking
            // at whether the latest version is prerelease or not.
            bool isLatestStable;
            bool isLatest;
            if ((searchFilters & SearchFilters.IncludePrerelease) == 0)
            {
                // This is the case where prerelease versions are excluded.
                var latestIncludePrerelease = versionLists
                    .GetLatestVersionInfoOrNull(searchFilters | SearchFilters.IncludePrerelease);
                Guard.Assert(
                    latestIncludePrerelease != null,
                    "If a search filter excludes prerelease and has a latest version, then there is a latest version including prerelease.");
                isLatestStable = true;
                isLatest = latestIncludePrerelease.ParsedVersion == latest.ParsedVersion;
            }
            else
            {
                // This is the case where prerelease versions are included.
                isLatestStable = !latest.ParsedVersion.IsPrerelease;
                isLatest = true;
            }

            return new SearchDocument.LatestFlags(latest, isLatestStable, isLatest);
        }

        public KeyedDocument Keyed(
            string packageId,
            SearchFilters searchFilters)
        {
            var document = new KeyedDocument();

            PopulateKey(document, packageId, searchFilters);

            return document;
        }

        public SearchDocument.UpdateVersionList UpdateVersionList(
            string packageId,
            SearchFilters searchFilters,
            string[] versions,
            bool isLatestStable,
            bool isLatest)
        {
            var document = new SearchDocument.UpdateVersionList();

            PopulateVersions(document, packageId, searchFilters, versions, isLatestStable, isLatest);

            return document;
        }

        public SearchDocument.UpdateLatest UpdateLatest(
            SearchFilters searchFilters,
            string[] versions,
            bool isLatestStable,
            bool isLatest,
            string normalizedVersion,
            string fullVersion,
            PackageDetailsCatalogLeaf leaf)
        {
            var document = new SearchDocument.UpdateLatest();

            PopulateUpdateLatest(document, leaf.PackageId, searchFilters, versions, isLatestStable, isLatest, fullVersion);
            DocumentUtilities.PopulateMetadata(document, normalizedVersion, leaf);

            return document;
        }

        public SearchDocument.Full Full(
            string packageId,
            SearchFilters searchFilters,
            string[] versions,
            bool isLatestStable,
            bool isLatest,
            string fullVersion,
            Package package,
            string[] owners,
            long totalDownloadCount)
        {
            var document = new SearchDocument.Full();

            PopulateAddFirst(document, packageId, searchFilters, versions, isLatestStable, isLatest, fullVersion, owners);
            DocumentUtilities.PopulateMetadata(document, packageId, package);
            document.TotalDownloadCount = totalDownloadCount;

            return document;
        }

        private static void PopulateVersions<T>(
            T document,
            string packageId,
            SearchFilters searchFilters,
            string[] versions,
            bool isLatestStable,
            bool isLatest) where T : KeyedDocument, SearchDocument.IVersions
        {
            PopulateKey(document, packageId, searchFilters);
            document.Versions = versions;
            document.IsLatestStable = isLatestStable;
            document.IsLatest = isLatest;
        }

        private static void PopulateKey(KeyedDocument document, string packageId, SearchFilters searchFilters)
        {
            document.Key = DocumentUtilities.GetSearchDocumentKey(packageId, searchFilters);
        }

        private static void PopulateUpdateLatest(
            SearchDocument.UpdateLatest document,
            string packageId,
            SearchFilters searchFilters,
            string[] versions,
            bool isLatestStable,
            bool isLatest,
            string fullVersion)
        {
            PopulateVersions(document, packageId, searchFilters, versions, isLatestStable, isLatest);
            document.SearchFilters = searchFilters.ToString();
            document.FullVersion = fullVersion;
        }

        private static void PopulateAddFirst(
            SearchDocument.AddFirst document,
            string packageId,
            SearchFilters searchFilters,
            string[] versions,
            bool isLatestStable,
            bool isLatest,
            string fullVersion,
            string[] owners)
        {
            PopulateUpdateLatest(document, packageId, searchFilters, versions, isLatestStable, isLatest, fullVersion);
            document.Owners = owners;
        }
    }
}
