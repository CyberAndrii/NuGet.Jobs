﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NgTests.Infrastructure;
using NuGet.Services.Metadata.Catalog.Dnx;
using NuGet.Services.Metadata.Catalog.Helpers;
using NuGet.Versioning;
using Xunit;

namespace NgTests
{
    public class DnxMakerTests
    {
        private const string _packageId = "testid";
        private const string _nupkgData = "nupkg data";
        private const string _nuspecData = "nuspec data";

        [Fact]
        public void Constructor_WhenStorageFactoryIsNull_Throws()
        {
            var exception = Assert.Throws<ArgumentNullException>(() => new DnxMaker(storageFactory: null));

            Assert.Equal("storageFactory", exception.ParamName);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void GetRelativeAddressNupkg_WhenIdIsNullOrEmpty_Throws(string id)
        {
            var exception = Assert.Throws<ArgumentException>(
                () => DnxMaker.GetRelativeAddressNupkg(id, version: "1.0.0"));

            Assert.Equal("id", exception.ParamName);
            Assert.StartsWith("The argument must not be null or empty.", exception.Message);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void GetRelativeAddressNupkg_WhenVersionIsNullOrEmpty_Throws(string version)
        {
            var exception = Assert.Throws<ArgumentException>(
                () => DnxMaker.GetRelativeAddressNupkg(id: "a", version: version));

            Assert.Equal("version", exception.ParamName);
            Assert.StartsWith("The argument must not be null or empty.", exception.Message);
        }

        [Fact]
        public async Task HasPackageInIndexAsync_WhenStorageIsNull_Throws()
        {
            var maker = CreateDnxMaker();

            var exception = await Assert.ThrowsAsync<ArgumentNullException>(
                () => maker.HasPackageInIndexAsync(
                    storage: null,
                    id: "a",
                    version: "b",
                    cancellationToken: CancellationToken.None));

            Assert.Equal("storage", exception.ParamName);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task HasPackageInIndexAsync_WhenIdIsNullOrEmpty_Throws(string id)
        {
            var maker = CreateDnxMaker();

            var exception = await Assert.ThrowsAsync<ArgumentException>(
                () => maker.HasPackageInIndexAsync(
                    new MemoryStorage(),
                    id: id,
                    version: "a",
                    cancellationToken: CancellationToken.None));

            Assert.Equal("id", exception.ParamName);
            Assert.StartsWith("The argument must not be null or empty.", exception.Message);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task HasPackageInIndexAsync_WhenVersionIsNullOrEmpty_Throws(string version)
        {
            var maker = CreateDnxMaker();

            var exception = await Assert.ThrowsAsync<ArgumentException>(
                () => maker.HasPackageInIndexAsync(
                    new MemoryStorage(),
                    id: "a",
                    version: version,
                    cancellationToken: CancellationToken.None));

            Assert.Equal("version", exception.ParamName);
            Assert.StartsWith("The argument must not be null or empty.", exception.Message);
        }

        [Fact]
        public async Task HasPackageInIndexAsync_WhenCancelled_Throws()
        {
            var maker = CreateDnxMaker();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => maker.HasPackageInIndexAsync(
                    new MemoryStorage(),
                    id: "a",
                    version: "b",
                    cancellationToken: new CancellationToken(canceled: true)));
        }

        [Fact]
        public async Task HasPackageInIndexAsync_WhenPackageIdAndVersionDoNotExist_ReturnsFalse()
        {
            var catalogToDnxStorage = new MemoryStorage();
            var catalogToDnxStorageFactory = new TestStorageFactory(name => catalogToDnxStorage.WithName(name));
            var maker = new DnxMaker(catalogToDnxStorageFactory);
            var storageForPackage = (MemoryStorage)catalogToDnxStorageFactory.Create(_packageId);

            var hasPackageInIndex = await maker.HasPackageInIndexAsync(storageForPackage, _packageId, "1.0.0", CancellationToken.None);

            Assert.False(hasPackageInIndex);
        }

        [Fact]
        public async Task HasPackageInIndexAsync_WhenPackageIdExistsButVersionDoesNotExist_ReturnsFalse()
        {
            var catalogToDnxStorage = new MemoryStorage();
            var catalogToDnxStorageFactory = new TestStorageFactory(name => catalogToDnxStorage.WithName(name));
            var maker = new DnxMaker(catalogToDnxStorageFactory);

            await maker.UpdatePackageVersionIndexAsync(_packageId, v => v.Add(NuGetVersion.Parse("1.0.0")), CancellationToken.None);

            var storageForPackage = (MemoryStorage)catalogToDnxStorageFactory.Create(_packageId);

            var hasPackageInIndex = await maker.HasPackageInIndexAsync(storageForPackage, _packageId, "2.0.0", CancellationToken.None);

            Assert.False(hasPackageInIndex);
        }

        [Fact]
        public async Task HasPackageInIndexAsync_WhenPackageIdAndVersionExist_ReturnsTrue()
        {
            var catalogToDnxStorage = new MemoryStorage();
            var catalogToDnxStorageFactory = new TestStorageFactory(name => catalogToDnxStorage.WithName(name));
            var maker = new DnxMaker(catalogToDnxStorageFactory);

            const string version = "1.0.0";

            await maker.UpdatePackageVersionIndexAsync(_packageId, v => v.Add(NuGetVersion.Parse(version)), CancellationToken.None);

            var storageForPackage = (MemoryStorage)catalogToDnxStorageFactory.Create(_packageId);

            var hasPackageInIndex = await maker.HasPackageInIndexAsync(storageForPackage, _packageId, version, CancellationToken.None);

            Assert.True(hasPackageInIndex);
        }

        [Fact]
        public async Task AddPackageAsync_WhenNupkgStreamIsNull_Throws()
        {
            var maker = CreateDnxMaker();

            var exception = await Assert.ThrowsAsync<ArgumentNullException>(
                () => maker.AddPackageAsync(
                    nupkgStream: null,
                    nuspec: "a",
                    id: "b",
                    version: "c",
                    cancellationToken: CancellationToken.None));

            Assert.Equal("nupkgStream", exception.ParamName);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task AddPackageAsync_WhenNuspecIsNullOrEmpty_Throws(string nuspec)
        {
            var maker = CreateDnxMaker();

            var exception = await Assert.ThrowsAsync<ArgumentException>(
                () => maker.AddPackageAsync(
                    Stream.Null,
                    nuspec,
                    id: "a",
                    version: "b",
                    cancellationToken: CancellationToken.None));

            Assert.Equal("nuspec", exception.ParamName);
            Assert.StartsWith("The argument must not be null or empty.", exception.Message);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task AddPackageAsync_WhenIdIsNullOrEmpty_Throws(string id)
        {
            var maker = CreateDnxMaker();

            var exception = await Assert.ThrowsAsync<ArgumentException>(
                () => maker.AddPackageAsync(
                    Stream.Null,
                    nuspec: "a",
                    id: id,
                    version: "b",
                    cancellationToken: CancellationToken.None));

            Assert.Equal("id", exception.ParamName);
            Assert.StartsWith("The argument must not be null or empty.", exception.Message);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task AddPackageAsync_WhenVersionIsNullOrEmpty_Throws(string version)
        {
            var maker = CreateDnxMaker();

            var exception = await Assert.ThrowsAsync<ArgumentException>(
                () => maker.AddPackageAsync(
                    Stream.Null,
                    nuspec: "a",
                    id: "b",
                    version: version,
                    cancellationToken: CancellationToken.None));

            Assert.Equal("version", exception.ParamName);
            Assert.StartsWith("The argument must not be null or empty.", exception.Message);
        }

        [Fact]
        public async Task AddPackageAsync_WhenCancelled_Throws()
        {
            var maker = CreateDnxMaker();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => maker.AddPackageAsync(
                    Stream.Null,
                    nuspec: "a",
                    id: "b",
                    version: "c",
                    cancellationToken: new CancellationToken(canceled: true)));
        }

        [Theory]
        [MemberData(nameof(PackageVersions))]
        public async Task AddPackageAsync_WithValidVersion_PopulatesStorageWithNupkgAndNuspec(string version)
        {
            var catalogToDnxStorage = new MemoryStorage();
            var catalogToDnxStorageFactory = new TestStorageFactory(name => catalogToDnxStorage.WithName(name));
            var maker = new DnxMaker(catalogToDnxStorageFactory);
            var normalizedVersion = NuGetVersionUtility.NormalizeVersion(version);

            using (var nupkgStream = CreateFakePackageStream(_nupkgData))
            {
                var dnxEntry = await maker.AddPackageAsync(nupkgStream, _nuspecData, _packageId, version, CancellationToken.None);

                var expectedNuspec = new Uri($"{catalogToDnxStorage.BaseAddress}{_packageId}/{normalizedVersion}/{_packageId}.nuspec");
                var expectedNupkg = new Uri($"{catalogToDnxStorage.BaseAddress}{_packageId}/{normalizedVersion}/{_packageId}.{normalizedVersion}.nupkg");
                var storageForPackage = (MemoryStorage)catalogToDnxStorageFactory.Create(_packageId);

                Assert.Equal(expectedNuspec, dnxEntry.Nuspec);
                Assert.Equal(expectedNupkg, dnxEntry.Nupkg);
                Assert.Equal(2, catalogToDnxStorage.Content.Count);
                Assert.Equal(2, storageForPackage.Content.Count);

                Verify(catalogToDnxStorage, expectedNupkg, _nupkgData);
                Verify(catalogToDnxStorage, expectedNuspec, _nuspecData);
                Verify(storageForPackage, expectedNupkg, _nupkgData);
                Verify(storageForPackage, expectedNuspec, _nuspecData);
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task DeletePackageAsync_WhenIdIsNullOrEmpty_Throws(string id)
        {
            var maker = CreateDnxMaker();

            var exception = await Assert.ThrowsAsync<ArgumentException>(
                () => maker.DeletePackageAsync(
                    id: id,
                    version: "a",
                    cancellationToken: CancellationToken.None));

            Assert.Equal("id", exception.ParamName);
            Assert.StartsWith("The argument must not be null or empty.", exception.Message);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task DeletePackageAsync_WhenVersionIsNullOrEmpty_Throws(string version)
        {
            var maker = CreateDnxMaker();

            var exception = await Assert.ThrowsAsync<ArgumentException>(
                () => maker.DeletePackageAsync(
                    id: "a",
                    version: version,
                    cancellationToken: CancellationToken.None));

            Assert.Equal("version", exception.ParamName);
            Assert.StartsWith("The argument must not be null or empty.", exception.Message);
        }

        [Fact]
        public async Task DeletePackageAsync_WhenCancelled_Throws()
        {
            var maker = CreateDnxMaker();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => maker.DeletePackageAsync(
                    id: "a",
                    version: "b",
                    cancellationToken: new CancellationToken(canceled: true)));
        }

        [Theory]
        [MemberData(nameof(PackageVersions))]
        public async Task DeletePackageAsync_WithValidVersion_RemovesNupkgAndNuspecFromStorage(string version)
        {
            var catalogToDnxStorage = new MemoryStorage();
            var catalogToDnxStorageFactory = new TestStorageFactory(name => catalogToDnxStorage.WithName(name));
            var maker = new DnxMaker(catalogToDnxStorageFactory);

            using (var nupkgStream = CreateFakePackageStream(_nupkgData))
            {
                var dnxEntry = await maker.AddPackageAsync(nupkgStream, _nuspecData, _packageId, version, CancellationToken.None);

                var storageForPackage = (MemoryStorage)catalogToDnxStorageFactory.Create(_packageId);

                Assert.Equal(2, catalogToDnxStorage.Content.Count);
                Assert.Equal(2, storageForPackage.Content.Count);

                await maker.DeletePackageAsync(_packageId, version, CancellationToken.None);

                Assert.Equal(0, catalogToDnxStorage.Content.Count);
                Assert.Equal(0, storageForPackage.Content.Count);
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task UpdatePackageVersionIndexAsync_WhenIdIsNullOrEmpty_Throws(string id)
        {
            var maker = CreateDnxMaker();

            var exception = await Assert.ThrowsAsync<ArgumentException>(
                () => maker.UpdatePackageVersionIndexAsync(
                    id: id,
                    updateAction: _ => { },
                    cancellationToken: CancellationToken.None));

            Assert.Equal("id", exception.ParamName);
            Assert.StartsWith("The argument must not be null or empty.", exception.Message);
        }

        [Fact]
        public async Task UpdatePackageVersionIndexAsync_WhenVersionIsNullOrEmpty_Throws()
        {
            var maker = CreateDnxMaker();

            var exception = await Assert.ThrowsAsync<ArgumentNullException>(
                () => maker.UpdatePackageVersionIndexAsync(
                    id: "a",
                    updateAction: null,
                    cancellationToken: CancellationToken.None));

            Assert.Equal("updateAction", exception.ParamName);
        }

        [Fact]
        public async Task UpdatePackageVersionIndexAsync_WhenCancelled_Throws()
        {
            var maker = CreateDnxMaker();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => maker.UpdatePackageVersionIndexAsync(
                    id: "a",
                    updateAction: _ => { },
                    cancellationToken: new CancellationToken(canceled: true)));
        }

        [Theory]
        [MemberData(nameof(PackageVersions))]
        public async Task UpdatePackageVersionIndexAsync_WithValidVersion_CreatesIndex(string version)
        {
            var catalogToDnxStorage = new MemoryStorage();
            var catalogToDnxStorageFactory = new TestStorageFactory(name => catalogToDnxStorage.WithName(name));
            var maker = new DnxMaker(catalogToDnxStorageFactory);
            var normalizedVersion = NuGetVersionUtility.NormalizeVersion(version);

            await maker.UpdatePackageVersionIndexAsync(_packageId, v => v.Add(NuGetVersion.Parse(version)), CancellationToken.None);

            var storageForPackage = (MemoryStorage)catalogToDnxStorageFactory.Create(_packageId);
            var indexJsonUri = new Uri(storageForPackage.BaseAddress, "index.json");
            var indexJson = await storageForPackage.LoadAsync(indexJsonUri, CancellationToken.None);
            var indexObject = JObject.Parse(indexJson.GetContentString());
            var versions = indexObject["versions"].ToObject<string[]>();
            var expectedContent = GetExpectedIndexJsonContent(normalizedVersion);

            Assert.Equal(1, catalogToDnxStorage.Content.Count);
            Assert.Equal(1, storageForPackage.Content.Count);

            Verify(catalogToDnxStorage, indexJsonUri, expectedContent);
            Verify(storageForPackage, indexJsonUri, expectedContent);

            Assert.Equal(new[] { normalizedVersion }, versions);
        }

        [Fact]
        public async Task UpdatePackageVersionIndexAsync_WhenLastVersionRemoved_RemovesIndex()
        {
            var version = NuGetVersion.Parse("1.0.0");
            var catalogToDnxStorage = new MemoryStorage();
            var catalogToDnxStorageFactory = new TestStorageFactory(name => catalogToDnxStorage.WithName(name));
            var maker = new DnxMaker(catalogToDnxStorageFactory);

            await maker.UpdatePackageVersionIndexAsync(_packageId, v => v.Add(version), CancellationToken.None);

            var storageForPackage = (MemoryStorage)catalogToDnxStorageFactory.Create(_packageId);
            var indexJsonUri = new Uri(storageForPackage.BaseAddress, "index.json");
            var indexJson = await storageForPackage.LoadAsync(indexJsonUri, CancellationToken.None);

            Assert.NotNull(indexJson);
            Assert.Equal(1, catalogToDnxStorage.Content.Count);
            Assert.Equal(1, storageForPackage.Content.Count);

            await maker.UpdatePackageVersionIndexAsync(_packageId, v => v.Remove(version), CancellationToken.None);

            indexJson = await storageForPackage.LoadAsync(indexJsonUri, CancellationToken.None);

            Assert.Null(indexJson);
            Assert.Equal(0, catalogToDnxStorage.Content.Count);
            Assert.Equal(0, storageForPackage.Content.Count);
        }

        [Fact]
        public async Task UpdatePackageVersionIndexAsync_WithNoVersions_DoesNotCreateIndex()
        {
            var catalogToDnxStorage = new MemoryStorage();
            var catalogToDnxStorageFactory = new TestStorageFactory(name => catalogToDnxStorage.WithName(name));
            var maker = new DnxMaker(catalogToDnxStorageFactory);

            await maker.UpdatePackageVersionIndexAsync(_packageId, v => { }, CancellationToken.None);

            var storageForPackage = (MemoryStorage)catalogToDnxStorageFactory.Create(_packageId);
            var indexJsonUri = new Uri(storageForPackage.BaseAddress, "index.json");
            var indexJson = await storageForPackage.LoadAsync(indexJsonUri, CancellationToken.None);

            Assert.Null(indexJson);
            Assert.Equal(0, catalogToDnxStorage.Content.Count);
            Assert.Equal(0, storageForPackage.Content.Count);
        }

        [Fact]
        public async Task UpdatePackageVersionIndexAsync_WithMultipleVersions_SortsVersion()
        {
            var unorderedVersions = new[]
            {
                NuGetVersion.Parse("3.0.0"),
                NuGetVersion.Parse("1.1.0"),
                NuGetVersion.Parse("1.0.0"),
                NuGetVersion.Parse("1.0.1"),
                NuGetVersion.Parse("2.0.0"),
                NuGetVersion.Parse("1.0.2")
            };
            var catalogToDnxStorage = new MemoryStorage();
            var catalogToDnxStorageFactory = new TestStorageFactory(name => catalogToDnxStorage.WithName(name));
            var maker = new DnxMaker(catalogToDnxStorageFactory);

            await maker.UpdatePackageVersionIndexAsync(_packageId, v => v.UnionWith(unorderedVersions), CancellationToken.None);

            var storageForPackage = (MemoryStorage)catalogToDnxStorageFactory.Create(_packageId);
            var indexJsonUri = new Uri(storageForPackage.BaseAddress, "index.json");
            var indexJson = await storageForPackage.LoadAsync(indexJsonUri, CancellationToken.None);
            var indexObject = JObject.Parse(indexJson.GetContentString());
            var versions = indexObject["versions"].ToObject<string[]>();

            Assert.Equal(1, catalogToDnxStorage.Content.Count);
            Assert.Equal(1, storageForPackage.Content.Count);
            Assert.Collection(
                versions,
                version => Assert.Equal(unorderedVersions[2].ToNormalizedString(), version),
                version => Assert.Equal(unorderedVersions[3].ToNormalizedString(), version),
                version => Assert.Equal(unorderedVersions[5].ToNormalizedString(), version),
                version => Assert.Equal(unorderedVersions[1].ToNormalizedString(), version),
                version => Assert.Equal(unorderedVersions[4].ToNormalizedString(), version),
                version => Assert.Equal(unorderedVersions[0].ToNormalizedString(), version));
        }

        public static IEnumerable<object[]> PackageVersions
        {
            get
            {
                // normalized versions
                yield return new object[] { "1.2.0" };
                yield return new object[] { "0.1.2" };
                yield return new object[] { "1.2.3.4" };
                yield return new object[] { "1.2.3-beta1" };
                yield return new object[] { "1.2.3-beta.1" };

                // non-normalized versions
                yield return new object[] { "1.2" };
                yield return new object[] { "1.2.3.0" };
                yield return new object[] { "1.02.3" };
            }
        }

        private static DnxMaker CreateDnxMaker()
        {
            var catalogToDnxStorage = new MemoryStorage();
            var catalogToDnxStorageFactory = new TestStorageFactory(name => catalogToDnxStorage.WithName(name));

            return new DnxMaker(catalogToDnxStorageFactory);
        }

        private static MemoryStream CreateFakePackageStream(string content)
        {
            var stream = new MemoryStream();

            using (var writer = new StreamWriter(stream, new UTF8Encoding(), bufferSize: 4096, leaveOpen: true))
            {
                writer.Write(content);
                writer.Flush();
            }

            stream.Position = 0;

            return stream;
        }

        private static string GetExpectedIndexJsonContent(string version)
        {
            return $"{{\r\n  \"versions\": [\r\n    \"{version}\"\r\n  ]\r\n}}";
        }

        private static void Verify(MemoryStorage storage, Uri uri, string expectedContent)
        {
            Assert.True(storage.Content.ContainsKey(uri));
            Assert.True(storage.ContentBytes.TryGetValue(uri, out var bytes));
            Assert.Equal(Encoding.UTF8.GetBytes(expectedContent), bytes);

            var isExpected = storage.BaseAddress != new Uri("http://tempuri.org/");

            Assert.Equal(isExpected, storage.ListMock.TryGetValue(uri, out var list));

            if (isExpected)
            {
                Assert.Equal(uri, list.Uri);

                var utc = DateTime.UtcNow;
                Assert.NotNull(list.LastModifiedUtc);
                Assert.InRange(list.LastModifiedUtc.Value, utc.AddMinutes(-1), utc);
            }
        }
    }
}