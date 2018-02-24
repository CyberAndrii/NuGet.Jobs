﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Lucene.Net.Store;
using Moq;
using Ng;
using Ng.Jobs;
using NgTests.Data;
using NgTests.Infrastructure;
using NuGet.IndexingTests.TestSupport;
using NuGet.Services.Metadata.Catalog;
using Xunit;

namespace NgTests
{
    public class SearchIndexFromCatalogCollectorTests
    {
        [Theory]
        [InlineData("/data/2015.10.12.10.08.54/unlistedpackage.1.0.0.json", null)]
        [InlineData("/data/2015.10.12.10.08.55/listedpackage.1.0.1.json", "2015-10-12T10:08:54.1506742Z")]
        [InlineData("/data/2015.10.12.10.08.55/anotherpackage.1.0.0.json", "2015-10-12T10:08:54.1506742Z")]
        public async Task DoesNotSkipPackagesWhenExceptionOccurs(string catalogUri, string expectedCursorBeforeRetry)
        {
            // Arrange
            var storage = new MemoryStorage();
            var storageFactory = new TestStorageFactory(name => storage.WithName(name));

            MockServerHttpClientHandler mockServer;
            mockServer = new MockServerHttpClientHandler();
            mockServer.SetAction("/", request => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

            var catalogStorage = Catalogs.CreateTestCatalogWithCommitThenTwoPackageCommit();
            await mockServer.AddStorage(catalogStorage);

            // Make the first request for a catalog leaf node fail. This will cause the registration collector
            // to fail the first time but pass the second time.
            FailFirstRequest(mockServer, catalogUri);

            expectedCursorBeforeRetry = expectedCursorBeforeRetry ?? MemoryCursor.MinValue.ToString("O");

            ReadWriteCursor front = new DurableCursor(
                storage.ResolveUri("cursor.json"),
                storage,
                MemoryCursor.MinValue);
            ReadCursor back = MemoryCursor.CreateMax();

            using (var testDirectory = TestDirectory.Create())
            {
                var luceneDirectory = new SimpleFSDirectory(new DirectoryInfo(testDirectory));
                using (var indexWriter = Catalog2LuceneJob.CreateIndexWriter(luceneDirectory))
                {
                    var target = new SearchIndexFromCatalogCollector(
                        new Uri("http://tempuri.org/index.json"),
                        indexWriter,
                        commitEachBatch: true,
                        baseAddress: null,
                        telemetryService: new Mock<ITelemetryService>().Object,
                        logger: new TestLogger(),
                        handlerFunc: () => mockServer);

                    // Act
                    await Assert.ThrowsAsync<Exception>(() => target.Run(front, back, CancellationToken.None));
                    var cursorBeforeRetry = front.Value;
                    await target.Run(front, back, CancellationToken.None);
                    var cursorAfterRetry = front.Value;

                    // Assert
                    var reader = indexWriter.GetReader();
                    var documents = Enumerable
                        .Range(0, reader.NumDeletedDocs + reader.NumDocs())
                        .Where(i => !reader.IsDeleted(i))
                        .Select(i => reader.Document(i))
                        .ToList();
                    Assert.Equal(4, documents.Count);

                    var documentsByType = documents
                        .ToLookup(doc => doc
                            .fields_ForNUnit
                            .FirstOrDefault(f => f.Name == "@type")?
                            .StringValue);
                    var commitDocuments = documentsByType[Schema.DataTypes.CatalogInfastructure.AbsoluteUri.ToString()].ToList();
                    var packageDocuments = documentsByType[null].ToList();
                    Assert.Equal(1, commitDocuments.Count);
                    Assert.Equal(3, packageDocuments.Count);

                    Assert.Equal(
                        "UnlistedPackage",
                        packageDocuments[0].fields_ForNUnit.FirstOrDefault(x => x.Name == Constants.LucenePropertyId)?.StringValue);
                    Assert.Equal(
                        "ListedPackage",
                        packageDocuments[1].fields_ForNUnit.FirstOrDefault(x => x.Name == Constants.LucenePropertyId)?.StringValue);
                    Assert.Equal(
                        "AnotherPackage",
                        packageDocuments[2].fields_ForNUnit.FirstOrDefault(x => x.Name == Constants.LucenePropertyId)?.StringValue);

                    Assert.Equal(DateTime.Parse(expectedCursorBeforeRetry).ToUniversalTime(), cursorBeforeRetry);
                    Assert.Equal(DateTime.Parse("2015-10-12T10:08:55.3335317Z").ToUniversalTime(), cursorAfterRetry);
                }
            }
        }

        private void FailFirstRequest(MockServerHttpClientHandler mockServer, string relativeUri)
        {
            var originalAction = mockServer.Actions[relativeUri];
            var hasFailed = false;
            Func<HttpRequestMessage, Task<HttpResponseMessage>> failFirst = request =>
            {
                if (!hasFailed)
                {
                    hasFailed = true;
                    throw new HttpRequestException("Simulated HTTP failure.");
                }

                return originalAction(request);
            };
            mockServer.SetAction(relativeUri, failFirst);
        }
    }
}
