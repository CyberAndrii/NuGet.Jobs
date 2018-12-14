﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NuGet.Services.AzureSearch.Support;
using NuGetGallery;
using Xunit;
using Xunit.Abstractions;

namespace NuGet.Services.AzureSearch
{
    public class HijackDocumentBuilderFacts
    {
        public class Keyed : BaseFacts
        {
            public Keyed(ITestOutputHelper output) : base(output)
            {
            }

            [Fact]
            public async Task SetsExpectedProperties()
            {
                var document = _target.Keyed(Data.PackageId, Data.NormalizedVersion);

                var json = await SerializationUtilities.SerializeToJsonAsync(document);
                Assert.Equal(@"{
  ""value"": [
    {
      ""@search.action"": ""upload"",
      ""key"": ""windowsazure_storage_7_1_2-alpha-d2luZG93c2F6dXJlLnN0b3JhZ2UvNy4xLjItYWxwaGE1""
    }
  ]
}", json);
            }
        }

        public class LatestFromCatalog : BaseFacts
        {
            public LatestFromCatalog(ITestOutputHelper output) : base(output)
            {
            }

            [Fact]
            public async Task SetsExpectedProperties()
            {
                var document = _target.LatestFromCatalog(
                    Data.PackageId,
                    Data.NormalizedVersion,
                    Data.CommitTimestamp,
                    Data.CommitId,
                    _changes);

                SetDocumentLastUpdated(document);
                var json = await SerializationUtilities.SerializeToJsonAsync(document);
                Assert.Equal(@"{
  ""value"": [
    {
      ""@search.action"": ""upload"",
      ""isLatestStableSemVer1"": false,
      ""isLatestSemVer1"": true,
      ""isLatestStableSemVer2"": false,
      ""isLatestSemVer2"": true,
      ""lastUpdatedDocument"": ""2018-12-14T09:30:00+00:00"",
      ""lastDocumentType"": ""NuGet.Services.AzureSearch.HijackDocument+Latest"",
      ""lastUpdatedFromCatalog"": true,
      ""lastCommitTimestamp"": ""2018-12-13T12:30:00+00:00"",
      ""lastCommitId"": ""6b9b24dd-7aec-48ae-afc1-2a117e3d50d1"",
      ""key"": ""windowsazure_storage_7_1_2-alpha-d2luZG93c2F6dXJlLnN0b3JhZ2UvNy4xLjItYWxwaGE1""
    }
  ]
}", json);
            }
        }

        public class FullFromDb : BaseFacts
        {
            public FullFromDb(ITestOutputHelper output) : base(output)
            {
            }

            [Fact]
            public void LeavesNullTagsAsNull()
            {
                var package = Data.PackageEntity;
                package.Tags = null;

                var document = _target.FullFromDb(Data.PackageId, _changes, package);

                Assert.Null(document.Tags);
            }

            [Fact]
            public async Task SerializesNullSemVerLevel()
            {
                var package = Data.PackageEntity;
                package.SemVerLevelKey = SemVerLevelKey.Unknown;

                var document = _target.FullFromDb(Data.PackageId, _changes, package);

                var json = await SerializationUtilities.SerializeToJsonAsync(document);
                Assert.Contains("\"semVerLevel\": null,", json);
            }

            [Fact]
            public async Task SetsExpectedProperties()
            {
                var document = _target.FullFromDb(Data.PackageId, _changes, Data.PackageEntity);

                SetDocumentLastUpdated(document);
                var json = await SerializationUtilities.SerializeToJsonAsync(document);
                Assert.Equal(@"{
  ""value"": [
    {
      ""@search.action"": ""upload"",
      ""isLatestStableSemVer1"": false,
      ""isLatestSemVer1"": true,
      ""isLatestStableSemVer2"": false,
      ""isLatestSemVer2"": true,
      ""semVerLevel"": 2,
      ""authors"": ""Microsoft"",
      ""copyright"": ""© Microsoft Corporation. All rights reserved."",
      ""created"": ""2017-01-01T00:00:00+00:00"",
      ""description"": ""Description."",
      ""fileSize"": 3039254,
      ""flattenedDependencies"": ""Microsoft.Data.OData:5.6.4:net40-client|Newtonsoft.Json:6.0.8:net40-client"",
      ""hash"": ""oMs9XKzRTsbnIpITcqZ5XAv1h2z6oyJ33+Z/PJx36iVikge/8wm5AORqAv7soKND3v5/0QWW9PQ0ktQuQu9aQQ=="",
      ""hashAlgorithm"": ""SHA512"",
      ""iconUrl"": ""http://go.microsoft.com/fwlink/?LinkID=288890"",
      ""language"": ""en-US"",
      ""lastEdited"": ""2017-01-02T00:00:00+00:00"",
      ""licenseUrl"": ""http://go.microsoft.com/fwlink/?LinkId=331471"",
      ""minClientVersion"": ""2.12"",
      ""normalizedVersion"": ""7.1.2-alpha"",
      ""originalVersion"": ""7.1.2.0-alpha+git"",
      ""packageId"": ""WindowsAzure.Storage"",
      ""prerelease"": true,
      ""projectUrl"": ""https://github.com/Azure/azure-storage-net"",
      ""published"": ""2017-01-03T00:00:00+00:00"",
      ""releaseNotes"": ""Release notes."",
      ""requiresLicenseAcceptance"": true,
      ""sortableTitle"": ""windows azure storage"",
      ""summary"": ""Summary."",
      ""tags"": [
        ""Microsoft"",
        ""Azure"",
        ""Storage"",
        ""Table"",
        ""Blob"",
        ""File"",
        ""Queue"",
        ""Scalable"",
        ""windowsazureofficial""
      ],
      ""title"": ""Windows Azure Storage"",
      ""lastUpdatedDocument"": ""2018-12-14T09:30:00+00:00"",
      ""lastDocumentType"": ""NuGet.Services.AzureSearch.HijackDocument+Full"",
      ""lastUpdatedFromCatalog"": false,
      ""lastCommitTimestamp"": null,
      ""lastCommitId"": null,
      ""key"": ""windowsazure_storage_7_1_2-alpha-d2luZG93c2F6dXJlLnN0b3JhZ2UvNy4xLjItYWxwaGE1""
    }
  ]
}", json);
            }

            [Theory]
            [MemberData(nameof(MissingTitles))]
            public void UsesIdWhenMissingForSortableTitle(string title)
            {
                var package = Data.PackageEntity;
                package.Title = title;

                var document = _target.FullFromDb(Data.PackageId, _changes, package);

                Assert.Equal(Data.PackageId.ToLowerInvariant(), document.SortableTitle);
            }

            [Fact]
            public void Uses1900ForPublishedWhenUnlisted()
            {
                var package = Data.PackageEntity;
                package.Listed = false;

                var document = _target.FullFromDb(Data.PackageId, _changes, package);

                Assert.Equal(DateTimeOffset.Parse("1900-01-01Z"), document.Published);
            }

            [Fact]
            public void SplitsTags()
            {
                var package = Data.PackageEntity;
                package.Tags = "foo; BAR |     Baz";

                var document = _target.FullFromDb(Data.PackageId, _changes, package);

                Assert.Equal(new[] { "foo", "BAR", "Baz" }, document.Tags);
            }

            [Theory]
            [InlineData(null)]
            [InlineData(SemVerLevelKey.SemVer2)]
            public void UsesSemVerLevelToIndicateSemVer2(int? semVerLevelKey)
            {
                var package = Data.PackageEntity;
                package.SemVerLevelKey = semVerLevelKey;

                var document = _target.FullFromDb(Data.PackageId, _changes, package);

                Assert.Equal(semVerLevelKey, document.SemVerLevel);
            }

            /// <summary>
            /// The caller is expected to verify consistency.
            /// </summary>
            [Fact]
            public void DoesNotUseVersionToIndicateIsPrerelease()
            {
                var package = Data.PackageEntity;
                package.IsPrerelease = false;
                package.Version = "2.0.0-alpha";
                package.NormalizedVersion = "2.0.0-alpha";

                var document = _target.FullFromDb(Data.PackageId, _changes, package);

                Assert.False(document.Prerelease);
            }
        }

        public class FullFromCatalog : BaseFacts
        {
            public FullFromCatalog(ITestOutputHelper output) : base(output)
            {
            }

            [Fact]
            public async Task SetsExpectedProperties()
            {
                var document = _target.FullFromCatalog(Data.NormalizedVersion, _changes, Data.Leaf);

                SetDocumentLastUpdated(document);
                var json = await SerializationUtilities.SerializeToJsonAsync(document);
                Assert.Equal(@"{
  ""value"": [
    {
      ""@search.action"": ""upload"",
      ""isLatestStableSemVer1"": false,
      ""isLatestSemVer1"": true,
      ""isLatestStableSemVer2"": false,
      ""isLatestSemVer2"": true,
      ""semVerLevel"": 2,
      ""authors"": ""Microsoft"",
      ""copyright"": ""© Microsoft Corporation. All rights reserved."",
      ""created"": ""2017-01-01T00:00:00+00:00"",
      ""description"": ""Description."",
      ""fileSize"": 3039254,
      ""flattenedDependencies"": ""Microsoft.Data.OData:5.6.4:net40-client|Newtonsoft.Json:6.0.8:net40-client"",
      ""hash"": ""oMs9XKzRTsbnIpITcqZ5XAv1h2z6oyJ33+Z/PJx36iVikge/8wm5AORqAv7soKND3v5/0QWW9PQ0ktQuQu9aQQ=="",
      ""hashAlgorithm"": ""SHA512"",
      ""iconUrl"": ""http://go.microsoft.com/fwlink/?LinkID=288890"",
      ""language"": ""en-US"",
      ""lastEdited"": ""2017-01-02T00:00:00+00:00"",
      ""licenseUrl"": ""http://go.microsoft.com/fwlink/?LinkId=331471"",
      ""minClientVersion"": ""2.12"",
      ""normalizedVersion"": ""7.1.2-alpha"",
      ""originalVersion"": ""7.1.2.0-alpha+git"",
      ""packageId"": ""WindowsAzure.Storage"",
      ""prerelease"": true,
      ""projectUrl"": ""https://github.com/Azure/azure-storage-net"",
      ""published"": ""2017-01-03T00:00:00+00:00"",
      ""releaseNotes"": ""Release notes."",
      ""requiresLicenseAcceptance"": true,
      ""sortableTitle"": ""windows azure storage"",
      ""summary"": ""Summary."",
      ""tags"": [
        ""Microsoft"",
        ""Azure"",
        ""Storage"",
        ""Table"",
        ""Blob"",
        ""File"",
        ""Queue"",
        ""Scalable"",
        ""windowsazureofficial""
      ],
      ""title"": ""Windows Azure Storage"",
      ""lastUpdatedDocument"": ""2018-12-14T09:30:00+00:00"",
      ""lastDocumentType"": ""NuGet.Services.AzureSearch.HijackDocument+Full"",
      ""lastUpdatedFromCatalog"": true,
      ""lastCommitTimestamp"": ""2018-12-13T12:30:00+00:00"",
      ""lastCommitId"": ""6b9b24dd-7aec-48ae-afc1-2a117e3d50d1"",
      ""key"": ""windowsazure_storage_7_1_2-alpha-d2luZG93c2F6dXJlLnN0b3JhZ2UvNy4xLjItYWxwaGE1""
    }
  ]
}", json);
            }

            [Theory]
            [MemberData(nameof(MissingTitles))]
            public void UsesIdWhenMissingForSortableTitle(string title)
            {
                var leaf = Data.Leaf;
                leaf.Title = title;

                var document = _target.FullFromCatalog(Data.NormalizedVersion, _changes, leaf);

                Assert.Equal(Data.PackageId.ToLowerInvariant(), document.SortableTitle);
            }

            [Fact]
            public void LeavesNullRequiresLicenseAcceptanceAsNull()
            {
                var leaf = Data.Leaf;
                leaf.RequireLicenseAgreement = null;

                var document = _target.FullFromCatalog(Data.NormalizedVersion, _changes, leaf);

                Assert.Null(document.RequiresLicenseAcceptance);
            }

            [Fact]
            public void LeavesNullVerbatimVersionAsNull()
            {
                var leaf = Data.Leaf;
                leaf.VerbatimVersion = null;

                var document = _target.FullFromCatalog(Data.NormalizedVersion, _changes, leaf);

                Assert.Null(document.OriginalVersion);
            }
        }

        public abstract class BaseFacts
        {
            protected readonly ITestOutputHelper _output;
            protected readonly HijackDocumentChanges _changes;
            protected readonly HijackDocumentBuilder _target;


            public static IEnumerable<object[]> MissingTitles = new[]
            {
                new object[] { null },
                new object[] { string.Empty },
                new object[] { " " },
                new object[] { " \t"},
            };

            public void SetDocumentLastUpdated(ICommittedDocument document)
            {
                Data.SetDocumentLastUpdated(document, _output);
            }

            public BaseFacts(ITestOutputHelper output)
            {
                _output = output;
                _changes = new HijackDocumentChanges(
                    delete: false,
                    updateMetadata: true,
                    latestStableSemVer1: false,
                    latestSemVer1: true,
                    latestStableSemVer2: false,
                    latestSemVer2: true);

                _target = new HijackDocumentBuilder();
            }
        }
    }
}
