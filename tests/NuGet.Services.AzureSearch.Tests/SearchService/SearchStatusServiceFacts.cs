﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Search.Models;
using Microsoft.Extensions.Options;
using Moq;
using NuGet.Services.AzureSearch.Support;
using NuGet.Services.AzureSearch.Wrappers;
using Xunit;
using Xunit.Abstractions;

namespace NuGet.Services.AzureSearch.SearchService
{
    public class SearchStatusServiceFacts
    {
        public class GetStatusAsync : BaseFacts
        {
            public GetStatusAsync(ITestOutputHelper output) : base(output)
            {
            }

            [Fact]
            public async Task ReturnsFullStatus()
            {
                var before = DateTimeOffset.UtcNow;
                var status = await _target.GetStatusAsync(_assembly);

                Assert.True(status.Success);
                Assert.InRange(status.Duration, TimeSpan.FromMilliseconds(1), TimeSpan.MaxValue);

                Assert.Equal("This is a fake build date for testing.", status.Server.AssemblyBuildDateUtc);
                Assert.Equal("This is a fake commit ID for testing.", status.Server.AssemblyCommitId);
                Assert.Equal("1.0.0-fakefortesting", status.Server.AssemblyInformationalVersion);
                Assert.Equal(_config.DeploymentLabel, status.Server.DeploymentLabel);
                Assert.Equal("Fake website instance ID.", status.Server.InstanceId);
                Assert.Equal(Environment.MachineName, status.Server.MachineName);
                Assert.InRange(status.Server.ProcessDuration, TimeSpan.FromMilliseconds(1), TimeSpan.MaxValue);
                Assert.NotEqual(0, status.Server.ProcessId);
                Assert.InRange(status.Server.ProcessStartTime, DateTimeOffset.MinValue, before);

                Assert.Equal(23, status.SearchIndex.DocumentCount);
                Assert.Equal("search-index", status.SearchIndex.Name);
                Assert.InRange(status.SearchIndex.WarmQueryDuration, TimeSpan.FromMilliseconds(1), TimeSpan.MaxValue);

                Assert.Equal(42, status.HijackIndex.DocumentCount);
                Assert.Equal("hijack-index", status.HijackIndex.Name);
                Assert.InRange(status.HijackIndex.WarmQueryDuration, TimeSpan.FromMilliseconds(1), TimeSpan.MaxValue);

                Assert.Same(_auxiliaryFilesMetadata, status.AuxiliaryFiles);

                _searchDocuments.Verify(x => x.CountAsync(), Times.Once);
                _searchDocuments.Verify(x => x.SearchAsync("*", It.IsAny<SearchParameters>()), Times.Once);
                _hijackDocuments.Verify(x => x.CountAsync(), Times.Once);
                _hijackDocuments.Verify(x => x.SearchAsync("*", It.IsAny<SearchParameters>()), Times.Once);
            }

            [Fact]
            public async Task HandlesFailedServerStatus()
            {
                _options.Setup(x => x.Value).Throws(new InvalidOperationException("Can't get the deployment label."));

                var status = await _target.GetStatusAsync(_assembly);

                Assert.False(status.Success);
                Assert.InRange(status.Duration, TimeSpan.FromMilliseconds(1), TimeSpan.MaxValue);
                Assert.Null(status.Server);
                Assert.NotNull(status.SearchIndex);
                Assert.NotNull(status.HijackIndex);
                Assert.NotNull(status.AuxiliaryFiles);
            }

            [Fact]
            public async Task HandlesFailedSearchIndexStatus()
            {
                _searchDocuments
                    .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<SearchParameters>()))
                    .ThrowsAsync(new InvalidOperationException("Could not hit the search index."));

                var status = await _target.GetStatusAsync(_assembly);

                Assert.False(status.Success);
                Assert.InRange(status.Duration, TimeSpan.FromMilliseconds(1), TimeSpan.MaxValue);
                Assert.NotNull(status.Server);
                Assert.Null(status.SearchIndex);
                Assert.NotNull(status.HijackIndex);
                Assert.NotNull(status.AuxiliaryFiles);
            }

            [Fact]
            public async Task HandlesFailedHijackIndexStatus()
            {
                _hijackDocuments
                    .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<SearchParameters>()))
                    .ThrowsAsync(new InvalidOperationException("Could not hit the hijack index."));

                var status = await _target.GetStatusAsync(_assembly);

                Assert.False(status.Success);
                Assert.InRange(status.Duration, TimeSpan.FromMilliseconds(1), TimeSpan.MaxValue);
                Assert.NotNull(status.Server);
                Assert.NotNull(status.SearchIndex);
                Assert.Null(status.HijackIndex);
                Assert.NotNull(status.AuxiliaryFiles);
            }

            [Fact]
            public async Task HandlesFailedAuxiliaryData()
            {
                _auxiliaryDataCache
                    .Setup(x => x.InitializeAsync())
                    .Throws(new InvalidOperationException("Could not initialize the auxiliary data."));

                var status = await _target.GetStatusAsync(_assembly);

                Assert.False(status.Success);
                Assert.InRange(status.Duration, TimeSpan.FromMilliseconds(1), TimeSpan.MaxValue);
                Assert.NotNull(status.Server);
                Assert.NotNull(status.SearchIndex);
                Assert.NotNull(status.HijackIndex);
                Assert.Null(status.AuxiliaryFiles);
            }
        }

        public abstract class BaseFacts
        {
            protected readonly Mock<ISearchIndexClientWrapper> _searchIndex;
            protected readonly Mock<IDocumentsOperationsWrapper> _searchDocuments;
            protected readonly Mock<ISearchIndexClientWrapper> _hijackIndex;
            protected readonly Mock<IDocumentsOperationsWrapper> _hijackDocuments;
            protected readonly Mock<IAuxiliaryDataCache> _auxiliaryDataCache;
            protected readonly Mock<IAuxiliaryData> _auxiliaryData;
            protected readonly SearchServiceConfiguration _config;
            protected readonly Mock<IOptionsSnapshot<SearchServiceConfiguration>> _options;
            protected readonly RecordingLogger<SearchStatusService> _logger;
            protected readonly AuxiliaryFilesMetadata _auxiliaryFilesMetadata;
            protected readonly Assembly _assembly;
            protected readonly SearchStatusService _target;

            public BaseFacts(ITestOutputHelper output)
            {
                _searchIndex = new Mock<ISearchIndexClientWrapper>();
                _searchDocuments = new Mock<IDocumentsOperationsWrapper>();
                _hijackIndex = new Mock<ISearchIndexClientWrapper>();
                _hijackDocuments = new Mock<IDocumentsOperationsWrapper>();
                _auxiliaryDataCache = new Mock<IAuxiliaryDataCache>();
                _auxiliaryData = new Mock<IAuxiliaryData>();
                _options = new Mock<IOptionsSnapshot<SearchServiceConfiguration>>();
                _logger = output.GetLogger<SearchStatusService>();

                _auxiliaryFilesMetadata = new AuxiliaryFilesMetadata(
                    new AuxiliaryFileMetadata(
                        DateTimeOffset.MinValue,
                        DateTimeOffset.MinValue,
                        TimeSpan.Zero,
                        0,
                        string.Empty),
                    new AuxiliaryFileMetadata(
                        DateTimeOffset.MinValue,
                        DateTimeOffset.MinValue,
                        TimeSpan.Zero,
                        0,
                        string.Empty));
                _assembly = typeof(BaseFacts).Assembly;
                _config = new SearchServiceConfiguration();
                _config.DeploymentLabel = "Fake deployment label.";
                Environment.SetEnvironmentVariable("WEBSITE_INSTANCE_ID", "Fake website instance ID.");

                _searchIndex.Setup(x => x.IndexName).Returns("search-index");
                _hijackIndex.Setup(x => x.IndexName).Returns("hijack-index");
                _searchIndex.Setup(x => x.Documents).Returns(() => _searchDocuments.Object);
                _hijackIndex.Setup(x => x.Documents).Returns(() => _hijackDocuments.Object);
                _searchDocuments.Setup(x => x.CountAsync()).ReturnsAsync(23);
                _hijackDocuments.Setup(x => x.CountAsync()).ReturnsAsync(42);
                _searchDocuments
                    .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<SearchParameters>()))
                    .ReturnsAsync(new DocumentSearchResult())
                    .Callback(() => Thread.Sleep(TimeSpan.FromMilliseconds(1)));
                _hijackDocuments
                    .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<SearchParameters>()))
                    .ReturnsAsync(new DocumentSearchResult())
                    .Callback(() => Thread.Sleep(TimeSpan.FromMilliseconds(1)));
                _options.Setup(x => x.Value).Returns(() => _config);
                _auxiliaryDataCache.Setup(x => x.Get()).Returns(() => _auxiliaryData.Object);
                _auxiliaryData.Setup(x => x.Metadata).Returns(() => _auxiliaryFilesMetadata);

                _target = new SearchStatusService(
                    _searchIndex.Object,
                    _hijackIndex.Object,
                    _auxiliaryDataCache.Object,
                    _options.Object,
                    _logger);
            }
        }
    }
}
