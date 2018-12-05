﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Search.Models;
using Moq;
using NuGet.Packaging.Core;
using NuGet.Protocol.Catalog;
using NuGet.Services.AzureSearch.Support;
using NuGet.Services.Metadata.Catalog;
using NuGet.Versioning;
using NuGetGallery;
using Xunit;
using Xunit.Abstractions;
using PackageDependency = NuGet.Protocol.Catalog.PackageDependency;

namespace NuGet.Services.AzureSearch.Catalog2AzureSearch
{
    public class CatalogIndexActionBuilderFacts
    {
        public class AddCatalogEntriesAsync : BaseFacts
        {
            public AddCatalogEntriesAsync(ITestOutputHelper output) : base(output)
            {
            }

            [Fact]
            public async Task AddFirstVersion()
            {
                var indexActions = await _target.AddCatalogEntriesAsync(
                    _packageId,
                    _versionListDataResult,
                    _latestEntries,
                    _entryToLeaf);

                Assert.Equal(4, indexActions.Search.Count);
                Assert.All(indexActions.Search, x => Assert.IsType<SearchDocument.UpdateLatest>(x.Document));
                Assert.All(indexActions.Search, x => Assert.Equal(IndexActionType.MergeOrUpload, x.ActionType));

                Assert.Single(indexActions.Hijack);
                Assert.IsType<HijackDocument.Full>(indexActions.Hijack[0].Document);
                Assert.Equal(IndexActionType.MergeOrUpload, indexActions.Hijack[0].ActionType);

                Assert.Same(_versionListDataResult.AccessCondition, indexActions.VersionListDataResult.AccessCondition);
                var properties = indexActions.VersionListDataResult.Result.VersionProperties;
                Assert.Equal(
                    new[] { _packageVersion },
                    properties.Keys.ToArray());
                Assert.True(properties[_packageVersion].Listed);
                Assert.False(properties[_packageVersion].SemVer2);
            }

            [Fact]
            public async Task AddNewLatestVersion()
            {
                var existingVersion = "0.0.1";
                _versionListDataResult = new ResultAndAccessCondition<VersionListData>(
                    new VersionListData(new Dictionary<string, VersionPropertiesData>
                    {
                        { existingVersion, new VersionPropertiesData(listed: true, semVer2: false) },
                    }),
                    _versionListDataResult.AccessCondition);

                var indexActions = await _target.AddCatalogEntriesAsync(
                    _packageId,
                    _versionListDataResult,
                    _latestEntries,
                    _entryToLeaf);

                Assert.Equal(4, indexActions.Search.Count);
                Assert.All(indexActions.Search, x => Assert.IsType<SearchDocument.UpdateLatest>(x.Document));
                Assert.All(indexActions.Search, x => Assert.Equal(IndexActionType.MergeOrUpload, x.ActionType));

                Assert.Equal(2, indexActions.Hijack.Count);
                var existing = indexActions.Hijack.Single(x => x.Document.Key == existingVersion);
                Assert.IsType<HijackDocument.Latest>(existing.Document);
                Assert.Equal(IndexActionType.Merge, existing.ActionType);
                var added = indexActions.Hijack.Single(x => x.Document.Key == _packageVersion);
                Assert.IsType<HijackDocument.Full>(added.Document);
                Assert.Equal(IndexActionType.MergeOrUpload, added.ActionType);

                Assert.Same(_versionListDataResult.AccessCondition, indexActions.VersionListDataResult.AccessCondition);
                var properties = indexActions.VersionListDataResult.Result.VersionProperties;
                Assert.Equal(
                    new[] { existingVersion, _packageVersion },
                    properties.Keys.ToArray());
                Assert.True(properties[existingVersion].Listed);
                Assert.False(properties[existingVersion].SemVer2);
                Assert.True(properties[_packageVersion].Listed);
                Assert.False(properties[_packageVersion].SemVer2);
            }

            [Fact]
            public async Task AddNewNonLatestVersion()
            {
                var existingVersion = "1.0.1";
                _versionListDataResult = new ResultAndAccessCondition<VersionListData>(
                    new VersionListData(new Dictionary<string, VersionPropertiesData>
                    {
                        { existingVersion, new VersionPropertiesData(listed: true, semVer2: false) },
                    }),
                    _versionListDataResult.AccessCondition);

                var indexActions = await _target.AddCatalogEntriesAsync(
                    _packageId,
                    _versionListDataResult,
                    _latestEntries,
                    _entryToLeaf);

                Assert.Equal(4, indexActions.Search.Count);
                Assert.All(indexActions.Search, x => Assert.IsType<SearchDocument.UpdateVersionList>(x.Document));
                Assert.All(indexActions.Search, x => Assert.Equal(IndexActionType.Merge, x.ActionType));

                Assert.Equal(2, indexActions.Hijack.Count);
                var existing = indexActions.Hijack.Single(x => x.Document.Key == existingVersion);
                Assert.IsType<HijackDocument.Latest>(existing.Document);
                Assert.Equal(IndexActionType.Merge, existing.ActionType);
                var added = indexActions.Hijack.Single(x => x.Document.Key == _packageVersion);
                Assert.IsType<HijackDocument.Full>(added.Document);
                Assert.Equal(IndexActionType.MergeOrUpload, added.ActionType);

                Assert.Same(_versionListDataResult.AccessCondition, indexActions.VersionListDataResult.AccessCondition);
                var properties = indexActions.VersionListDataResult.Result.VersionProperties;
                Assert.Equal(
                    new[] { _packageVersion, existingVersion },
                    properties.Keys.ToArray());
                Assert.True(properties[existingVersion].Listed);
                Assert.False(properties[existingVersion].SemVer2);
                Assert.True(properties[_packageVersion].Listed);
                Assert.False(properties[_packageVersion].SemVer2);
            }

            [Fact]
            public async Task Downgrade()
            {
                var existingVersion = "0.0.1";
                var existingLeaf = new PackageDetailsCatalogLeaf
                {
                    CommitTimestamp = new DateTimeOffset(2018, 12, 1, 0, 0, 0, TimeSpan.Zero),
                    Url = "http://example/leaf/0.0.1",
                    PackageId = _packageId,
                    VerbatimVersion = existingVersion,
                    PackageVersion = existingVersion,
                    Listed = true,
                };
                _leaf.Listed = false;
                _versionListDataResult = new ResultAndAccessCondition<VersionListData>(
                    new VersionListData(new Dictionary<string, VersionPropertiesData>
                    {
                        { existingVersion, new VersionPropertiesData(listed: true, semVer2: false) },
                        { _packageVersion, new VersionPropertiesData(listed: true, semVer2: false) },
                    }),
                    _versionListDataResult.AccessCondition);
                _latestCatalogLeaves = new LatestCatalogLeaves(
                    new HashSet<NuGetVersion>(),
                    new Dictionary<NuGetVersion, PackageDetailsCatalogLeaf>
                    {
                        { NuGetVersion.Parse(existingVersion), existingLeaf },
                    });

                var indexActions = await _target.AddCatalogEntriesAsync(
                    _packageId,
                    _versionListDataResult,
                    _latestEntries,
                    _entryToLeaf);

                Assert.Equal(4, indexActions.Search.Count);
                Assert.All(indexActions.Search, x => Assert.IsType<SearchDocument.UpdateLatest>(x.Document));
                Assert.All(indexActions.Search, x => Assert.Equal(IndexActionType.MergeOrUpload, x.ActionType));

                Assert.Equal(2, indexActions.Hijack.Count);
                var existing = indexActions.Hijack.Single(x => x.Document.Key == existingVersion);
                Assert.IsType<HijackDocument.Full>(existing.Document);
                Assert.Equal(IndexActionType.MergeOrUpload, existing.ActionType);
                var added = indexActions.Hijack.Single(x => x.Document.Key == _packageVersion);
                Assert.IsType<HijackDocument.Full>(added.Document);
                Assert.Equal(IndexActionType.MergeOrUpload, added.ActionType);

                Assert.Same(_versionListDataResult.AccessCondition, indexActions.VersionListDataResult.AccessCondition);
                var properties = indexActions.VersionListDataResult.Result.VersionProperties;
                Assert.Equal(
                    new[] { existingVersion, _packageVersion },
                    properties.Keys.ToArray());
                Assert.True(properties[existingVersion].Listed);
                Assert.False(properties[existingVersion].SemVer2);
                Assert.False(properties[_packageVersion].Listed);
                Assert.False(properties[_packageVersion].SemVer2);
            }

            [Fact]
            public async Task DowngradeToUnlist()
            {
                var existingVersion = "0.0.1";
                var existingLeaf = new PackageDetailsCatalogLeaf
                {
                    CommitTimestamp = new DateTimeOffset(2018, 12, 1, 0, 0, 0, TimeSpan.Zero),
                    Url = "http://example/leaf/0.0.1",
                    PackageId = _packageId,
                    VerbatimVersion = existingVersion,
                    PackageVersion = existingVersion,
                    Listed = false,
                };
                _leaf.Listed = false;
                _versionListDataResult = new ResultAndAccessCondition<VersionListData>(
                    new VersionListData(new Dictionary<string, VersionPropertiesData>
                    {
                        { existingVersion, new VersionPropertiesData(listed: true, semVer2: false) },
                        { _packageVersion, new VersionPropertiesData(listed: true, semVer2: false) },
                    }),
                    _versionListDataResult.AccessCondition);
                _latestCatalogLeaves = new LatestCatalogLeaves(
                    new HashSet<NuGetVersion>(),
                    new Dictionary<NuGetVersion, PackageDetailsCatalogLeaf>
                    {
                        { NuGetVersion.Parse(existingVersion), existingLeaf },
                    });

                var indexActions = await _target.AddCatalogEntriesAsync(
                    _packageId,
                    _versionListDataResult,
                    _latestEntries,
                    _entryToLeaf);

                Assert.Equal(4, indexActions.Search.Count);
                Assert.All(indexActions.Search, x => Assert.IsType<KeyedDocument>(x.Document));
                Assert.All(indexActions.Search, x => Assert.Equal(IndexActionType.Delete, x.ActionType));

                Assert.Equal(2, indexActions.Hijack.Count);
                var existing = indexActions.Hijack.Single(x => x.Document.Key == existingVersion);
                Assert.IsType<HijackDocument.Full>(existing.Document);
                Assert.Equal(IndexActionType.MergeOrUpload, existing.ActionType);
                var added = indexActions.Hijack.Single(x => x.Document.Key == _packageVersion);
                Assert.IsType<HijackDocument.Full>(added.Document);
                Assert.Equal(IndexActionType.MergeOrUpload, added.ActionType);

                Assert.Same(_versionListDataResult.AccessCondition, indexActions.VersionListDataResult.AccessCondition);
                var properties = indexActions.VersionListDataResult.Result.VersionProperties;
                Assert.Equal(
                    new[] { existingVersion, _packageVersion },
                    properties.Keys.ToArray());
                Assert.False(properties[existingVersion].Listed);
                Assert.False(properties[existingVersion].SemVer2);
                Assert.False(properties[_packageVersion].Listed);
                Assert.False(properties[_packageVersion].SemVer2);
            }

            [Fact]
            public async Task DowngradeToDelete()
            {
                var existingVersion = "0.0.1";
                _leaf.Listed = false;
                _versionListDataResult = new ResultAndAccessCondition<VersionListData>(
                    new VersionListData(new Dictionary<string, VersionPropertiesData>
                    {
                        { existingVersion, new VersionPropertiesData(listed: true, semVer2: false) },
                        { _packageVersion, new VersionPropertiesData(listed: true, semVer2: false) },
                    }),
                    _versionListDataResult.AccessCondition);
                _latestCatalogLeaves = new LatestCatalogLeaves(
                    new HashSet<NuGetVersion> { NuGetVersion.Parse(existingVersion) },
                    new Dictionary<NuGetVersion, PackageDetailsCatalogLeaf>());

                var indexActions = await _target.AddCatalogEntriesAsync(
                    _packageId,
                    _versionListDataResult,
                    _latestEntries,
                    _entryToLeaf);

                Assert.Equal(4, indexActions.Search.Count);
                Assert.All(indexActions.Search, x => Assert.IsType<KeyedDocument>(x.Document));
                Assert.All(indexActions.Search, x => Assert.Equal(IndexActionType.Delete, x.ActionType));

                Assert.Equal(2, indexActions.Hijack.Count);
                var existing = indexActions.Hijack.Single(x => x.Document.Key == existingVersion);
                Assert.IsType<KeyedDocument>(existing.Document);
                Assert.Equal(IndexActionType.Delete, existing.ActionType);
                var added = indexActions.Hijack.Single(x => x.Document.Key == _packageVersion);
                Assert.IsType<HijackDocument.Full>(added.Document);
                Assert.Equal(IndexActionType.MergeOrUpload, added.ActionType);

                Assert.Same(_versionListDataResult.AccessCondition, indexActions.VersionListDataResult.AccessCondition);
                var properties = indexActions.VersionListDataResult.Result.VersionProperties;
                Assert.Equal(
                    new[] { _packageVersion },
                    properties.Keys.ToArray());
                Assert.False(properties[_packageVersion].Listed);
                Assert.False(properties[_packageVersion].SemVer2);
            }

            [Fact]
            public async Task DetectsSemVer2()
            {
                _leaf.DependencyGroups = new List<PackageDependencyGroup>
                {
                    new PackageDependencyGroup
                    {
                        Dependencies = new List<PackageDependency>
                        {
                            new PackageDependency
                            {
                                Range = "[1.0.0-alpha.1, )",
                            },
                        },
                    },
                };

                var indexActions = await _target.AddCatalogEntriesAsync(
                    _packageId,
                    _versionListDataResult,
                    _latestEntries,
                    _entryToLeaf);

                Assert.Equal(4, indexActions.Search.Count);
                var isSemVer2 = indexActions.Search.ToLookup(x => x.Document.Key.Contains("SemVer2"));
                Assert.All(isSemVer2[false], x => Assert.IsType<KeyedDocument>(x.Document));
                Assert.All(isSemVer2[false], x => Assert.Equal(IndexActionType.Delete, x.ActionType));
                Assert.All(isSemVer2[true], x => Assert.IsType<SearchDocument.UpdateLatest>(x.Document));
                Assert.All(isSemVer2[true], x => Assert.Equal(IndexActionType.MergeOrUpload, x.ActionType));

                Assert.Single(indexActions.Hijack);
                Assert.IsType<HijackDocument.Full>(indexActions.Hijack[0].Document);
                Assert.Equal(IndexActionType.MergeOrUpload, indexActions.Hijack[0].ActionType);

                Assert.Same(_versionListDataResult.AccessCondition, indexActions.VersionListDataResult.AccessCondition);
                var properties = indexActions.VersionListDataResult.Result.VersionProperties;
                Assert.Equal(
                    new[] { _packageVersion },
                    properties.Keys.ToArray());
                Assert.True(properties[_packageVersion].Listed);
                Assert.True(properties[_packageVersion].SemVer2);
            }

            [Fact]
            public async Task DetectsUnlisted()
            {
                _leaf.Listed = false;

                var indexActions = await _target.AddCatalogEntriesAsync(
                    _packageId,
                    _versionListDataResult,
                    _latestEntries,
                    _entryToLeaf);

                Assert.Equal(4, indexActions.Search.Count);
                Assert.All(indexActions.Search, x => Assert.IsType<KeyedDocument>(x.Document));
                Assert.All(indexActions.Search, x => Assert.Equal(IndexActionType.Delete, x.ActionType));

                Assert.Single(indexActions.Hijack);
                Assert.IsType<HijackDocument.Full>(indexActions.Hijack[0].Document);
                Assert.Equal(IndexActionType.MergeOrUpload, indexActions.Hijack[0].ActionType);

                Assert.Same(_versionListDataResult.AccessCondition, indexActions.VersionListDataResult.AccessCondition);
                var properties = indexActions.VersionListDataResult.Result.VersionProperties;
                Assert.Equal(
                    new[] { _packageVersion },
                    properties.Keys.ToArray());
                Assert.False(properties[_packageVersion].Listed);
                Assert.False(properties[_packageVersion].SemVer2);
            }
        }

        public abstract class BaseFacts
        {
            protected readonly Mock<ICatalogLeafFetcher> _fetcher;
            protected readonly Mock<ISearchDocumentBuilder> _search;
            protected readonly Mock<IHijackDocumentBuilder> _hijack;
            protected readonly RecordingLogger<CatalogIndexActionBuilder> _logger;
            protected string _packageId;
            protected string _packageVersion;
            protected CatalogCommitItem _commitItem;
            protected PackageDetailsCatalogLeaf _leaf;
            protected ResultAndAccessCondition<VersionListData> _versionListDataResult;
            protected List<CatalogCommitItem> _latestEntries;
            protected Dictionary<CatalogCommitItem, PackageDetailsCatalogLeaf> _entryToLeaf;
            protected LatestCatalogLeaves _latestCatalogLeaves;
            protected readonly CatalogIndexActionBuilder _target;

            public BaseFacts(ITestOutputHelper output)
            {
                _fetcher = new Mock<ICatalogLeafFetcher>();
                _search = new Mock<ISearchDocumentBuilder>();
                _hijack = new Mock<IHijackDocumentBuilder>();
                _logger = output.GetLogger<CatalogIndexActionBuilder>();

                _packageId = Data.PackageId;
                _packageVersion = "1.0.0";
                _commitItem = GenerateCatalogCommitItem(_packageVersion);
                _leaf = new PackageDetailsCatalogLeaf
                {
                    PackageId = _packageId,
                    PackageVersion = _commitItem.PackageIdentity.Version.ToFullString(),
                    VerbatimVersion = _commitItem.PackageIdentity.Version.OriginalVersion,
                    Listed = true,
                };
                _versionListDataResult = new ResultAndAccessCondition<VersionListData>(
                    new VersionListData(new Dictionary<string, VersionPropertiesData>()),
                    AccessConditionWrapper.GenerateIfNotExistsCondition());
                _latestEntries = new List<CatalogCommitItem> { _commitItem };
                _entryToLeaf = new Dictionary<CatalogCommitItem, PackageDetailsCatalogLeaf>(
                    ReferenceEqualityComparer<CatalogCommitItem>.Default)
                {
                    { _commitItem, _leaf },
                };
                _latestCatalogLeaves = new LatestCatalogLeaves(
                    new HashSet<NuGetVersion>(),
                    new Dictionary<NuGetVersion, PackageDetailsCatalogLeaf>());

                _search
                    .Setup(x => x.Keyed(It.IsAny<string>(), It.IsAny<SearchFilters>()))
                    .Returns<string, SearchFilters>(
                        (i, sf) => new KeyedDocument { Key = sf.ToString() });
                _search
                    .Setup(x => x.UpdateVersionList(It.IsAny<string>(), It.IsAny<SearchFilters>(), It.IsAny<string[]>()))
                    .Returns<string, SearchFilters, string[]>(
                        (i, sf, v) => new SearchDocument.UpdateVersionList { Key = sf.ToString() });
                _search
                    .Setup(x => x.UpdateLatest(
                        It.IsAny<SearchFilters>(),
                        It.IsAny<string[]>(),
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<PackageDetailsCatalogLeaf>()))
                    .Returns<SearchFilters, string[], string, string, PackageDetailsCatalogLeaf>(
                        (sf, vs, nv, fv, l) => new SearchDocument.UpdateLatest { Key = sf.ToString() });

                _hijack
                    .Setup(x => x.Keyed(It.IsAny<string>(), It.IsAny<string>()))
                    .Returns<string, string>(
                        (i, v) => new KeyedDocument { Key = v });
                _hijack
                    .Setup(x => x.Latest(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<HijackDocumentChanges>()))
                    .Returns<string, string, HijackDocumentChanges>(
                        (i, v, c) => new HijackDocument.Latest { Key = v });
                _hijack
                    .Setup(x => x.Full(It.IsAny<string>(), It.IsAny<HijackDocumentChanges>(), It.IsAny<PackageDetailsCatalogLeaf>()))
                    .Returns<string, HijackDocumentChanges, PackageDetailsCatalogLeaf>(
                        (v, c, l) => new HijackDocument.Full { Key = v });

                _fetcher
                    .Setup(x => x.GetLatestLeavesAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<IReadOnlyList<NuGetVersion>>>()))
                    .ReturnsAsync(() => _latestCatalogLeaves);

                _target = new CatalogIndexActionBuilder(
                    _fetcher.Object,
                    _search.Object,
                    _hijack.Object,
                    _logger);
            }

            protected CatalogCommitItem GenerateCatalogCommitItem(string version)
            {
                return new CatalogCommitItem(
                    new Uri("https://example/uri"),
                    "29e5c582-c1ef-4a5c-a053-d86c7381466b",
                    new DateTime(2018, 11, 1),
                    new List<string> { Schema.DataTypes.PackageDetails.AbsoluteUri },
                    new List<Uri> { Schema.DataTypes.PackageDetails },
                    new PackageIdentity(_packageId, NuGetVersion.Parse(version)));
            }
        }
    }
}
