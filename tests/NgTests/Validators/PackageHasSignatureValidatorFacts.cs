﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NgTests.Infrastructure;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Services.Metadata.Catalog;
using NuGet.Services.Metadata.Catalog.Helpers;
using NuGet.Services.Metadata.Catalog.Monitoring;
using NuGet.Services.Metadata.Catalog.Monitoring.Validation.Test.Catalog;
using NuGet.Services.Metadata.Catalog.Monitoring.Validation.Test.Exceptions;
using NuGet.Versioning;
using Xunit;

namespace NgTests
{
    public class PackageHasSignatureValidatorFacts
    {
        public class ShouldRunValidator : FactsBase
        {
            [Fact]
            public void SkipsIfNoEntries()
            {
                var context = CreateValidationContext(catalogEntries: new CatalogIndexEntry[0]);

                Assert.False(_target.ShouldRunValidator(context));
            }

            [Fact]
            public void SkipsIfLatestEntryIsDelete()
            {
                var uri = new Uri($"https://nuget.test/{PackageId}");
                var context = CreateValidationContext(
                    catalogEntries: new[]
                    {
                        new CatalogIndexEntry(
                            uri,
                            type: DetailsCatalogEntry,
                            commitId: Guid.NewGuid().ToString(),
                            commitTs: DateTime.MinValue,
                            id: PackageId,
                            version: PackageNuGetVersion),
                        new CatalogIndexEntry(
                            uri,
                            type: DeleteCatalogEntry,
                            commitId: Guid.NewGuid().ToString(),
                            commitTs: DateTime.MinValue.AddDays(1),
                            id: PackageId,
                            version: PackageNuGetVersion),
                    });

                Assert.False(_target.ShouldRunValidator(context));
            }

            [Fact]
            public void RunsIfLatestEntryIsntDelete()
            {
                var uri = new Uri($"https://nuget.test/{PackageId}");
                var context = CreateValidationContext(
                    catalogEntries: new[]
                    {
                        new CatalogIndexEntry(
                            uri,
                            type: DeleteCatalogEntry,
                            commitId: Guid.NewGuid().ToString(),
                            commitTs: DateTime.MinValue,
                            id: PackageId,
                            version: PackageNuGetVersion),
                        new CatalogIndexEntry(
                            uri,
                            type: DetailsCatalogEntry,
                            commitId: Guid.NewGuid().ToString(),
                            commitTs: DateTime.MinValue.AddDays(1),
                            id: PackageId,
                            version: PackageNuGetVersion),
                    });

                Assert.True(_target.ShouldRunValidator(context));
            }
        }

        public class RunValidatorAsync : FactsBase
        {
            [Fact]
            public async Task ReturnsGracefullyIfLatestLeafHasSignatureFile()
            {
                // Arrange
                var context = CreateValidationContext(
                    catalogEntries: new[]
                    {
                        new CatalogIndexEntry(
                            uri: new Uri("http://localhost/a.json"),
                            type: DetailsCatalogEntry,
                            commitId: Guid.NewGuid().ToString(),
                            commitTs: DateTime.MinValue,
                            id: PackageId,
                            version: PackageNuGetVersion),
                        new CatalogIndexEntry(
                            uri: new Uri("http://localhost/b.json"),
                            type: DetailsCatalogEntry,
                            commitId: Guid.NewGuid().ToString(),
                            commitTs: DateTime.MinValue.AddDays(1),
                            id: PackageId,
                            version: PackageNuGetVersion),
                    });

                AddCatalogLeaf("/a.json", new CatalogLeaf
                {
                    PackageEntries = new[]
                    {
                        new PackageEntry { FullName = "hello.txt" }
                    }
                });

                AddCatalogLeaf("/b.json", new CatalogLeaf
                {
                    PackageEntries = new[]
                    {
                        new PackageEntry { FullName = "hello.txt" },
                        new PackageEntry { FullName = ".signature.p7s" }
                    }
                });

                // Act & Assert
                await _target.RunValidatorAsync(context);
            }

            [Fact]
            public async Task ThrowsIfLatestLeafIsMissingASignatureFile()
            {
                // Arrange
                var malformedUri = new Uri("http://localhost/b.json");

                var context = CreateValidationContext(
                    catalogEntries: new[]
                    {
                        new CatalogIndexEntry(
                            uri: new Uri("http://localhost/a.json"),
                            type: DetailsCatalogEntry,
                            commitId: Guid.NewGuid().ToString(),
                            commitTs: DateTime.MinValue,
                            id: PackageId,
                            version: PackageNuGetVersion),
                        new CatalogIndexEntry(
                            uri: malformedUri,
                            type: DetailsCatalogEntry,
                            commitId: Guid.NewGuid().ToString(),
                            commitTs: DateTime.MinValue.AddDays(1),
                            id: PackageId,
                            version: PackageNuGetVersion),
                    });

                AddCatalogLeaf("/a.json", new CatalogLeaf
                {
                    PackageEntries = new[]
                    {
                        new PackageEntry { FullName = ".signature.p7s" }
                    }
                });

                AddCatalogLeaf("/b.json", new CatalogLeaf
                {
                    PackageEntries = new[]
                    {
                        new PackageEntry { FullName = "hello.txt" }
                    }
                });

                // Act & Assert
                var e = await Assert.ThrowsAsync<MissingPackageSignatureFileException>(() => _target.RunValidatorAsync(context));

                Assert.Same(malformedUri, e.CatalogEntry);
            }

            [Fact]
            public async Task ThrowsIfLeafPackageEntriesIsMissing()
            {
                // Arrange
                var malformedUri = new Uri("http://localhost/a.json");

                var context = CreateValidationContext(
                    catalogEntries: new[]
                    {
                        new CatalogIndexEntry(
                            uri: malformedUri,
                            type: DetailsCatalogEntry,
                            commitId: Guid.NewGuid().ToString(),
                            commitTs: DateTime.MinValue,
                            id: PackageId,
                            version: PackageNuGetVersion),
                    });

                AddCatalogLeaf("/a.json", "{ 'this': 'is missing the packageEntries field' }");

                // Act & Assert
                var e = await Assert.ThrowsAsync<InvalidOperationException>(() => _target.RunValidatorAsync(context));

                Assert.Equal("Catalog leaf is missing the 'packageEntries' property", e.Message);
            }

            [Fact]
            public async Task ThrowsIfLeafPackageEntriesIsMalformed()
            {
                // Arrange
                var malformedUri = new Uri("http://localhost/a.json");

                var context = CreateValidationContext(
                    catalogEntries: new[]
                    {
                        new CatalogIndexEntry(
                            uri: malformedUri,
                            type: DetailsCatalogEntry,
                            commitId: Guid.NewGuid().ToString(),
                            commitTs: DateTime.MinValue,
                            id: PackageId,
                            version: PackageNuGetVersion),
                    });

                AddCatalogLeaf("/a.json", "{ 'packageEntries': 'malformed'}");

                // Act & Assert
                var e = await Assert.ThrowsAsync<InvalidOperationException>(() => _target.RunValidatorAsync(context));

                Assert.Equal("Catalog leaf's 'packageEntries' property is malformed", e.Message);
            }
        }

        public class FactsBase
        {
            public const string DeleteCatalogEntry = "nuget:PackageDelete";
            public const string DetailsCatalogEntry = "nuget:PackageDetails";

            public const string PackageId = "TestPackage";
            public const string PackageVersion = "1.0.0";

            public static readonly NuGetVersion PackageNuGetVersion = NuGetVersion.Parse(PackageVersion);

            private readonly MockServerHttpClientHandler _mockServer;
            protected readonly PackageHasSignatureValidator _target;

            public FactsBase()
            {
                var feedToSource = new Mock<IDictionary<FeedType, SourceRepository>>();
                var logger = Mock.Of<ILogger<PackageHasSignatureValidator>>();

                feedToSource.Setup(x => x[It.IsAny<FeedType>()]).Returns(new Mock<SourceRepository>().Object);

                _mockServer = new MockServerHttpClientHandler();
                _target = new PackageHasSignatureValidator(feedToSource.Object, logger);
            }

            protected ValidationContext CreateValidationContext(IEnumerable<CatalogIndexEntry> catalogEntries = null)
            {
                catalogEntries = catalogEntries ?? new CatalogIndexEntry[0];

                var httpClient = new CollectorHttpClient(_mockServer);

                return new ValidationContext(
                    new PackageIdentity(PackageId, PackageNuGetVersion),
                    catalogEntries,
                    new DeletionAuditEntry[0],
                    httpClient,
                    CancellationToken.None);
            }

            protected void AddCatalogLeaf(string path, CatalogLeaf leaf)
            {
                var jsonSettings = new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver()
                };

                AddCatalogLeaf(path, JsonConvert.SerializeObject(leaf, jsonSettings));
            }

            protected void AddCatalogLeaf(string path, string leafContent)
            {
                _mockServer.SetAction(path, request =>
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(leafContent)
                    });
                });
            }

            public class CatalogLeaf
            {
                public IEnumerable<PackageEntry> PackageEntries { get; set; }
            }
        }
    }
}
