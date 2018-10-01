﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NuGetGallery;
using NuGetGallery.Services;
using Xunit;

namespace NuGet.Services.Validation.Orchestrator.Tests
{
    public class SymbolsMessageServiceFacts
    {
        [Fact]
        public void ConstructorThrowsWhenCoreMessageServiceIsNull()
        {
            var ex = Assert.Throws<ArgumentNullException>(() => new SymbolsPackageMessageService(null, EmailConfigurationAccessorMock.Object, LoggerMock.Object));
            Assert.Equal("coreMessageService", ex.ParamName);
        }

        [Fact]
        public void ConstructorThrowsWhenEmailConfigurationAccessorIsNull()
        {
            var ex = Assert.Throws<ArgumentNullException>(() => new SymbolsPackageMessageService(CoreMessageServiceMock.Object, null, LoggerMock.Object));
            Assert.Equal("emailConfigurationAccessor", ex.ParamName);
        }

        [Fact]
        public void ConstructorThrowsWhenLoggerIsNull()
        {
            var ex = Assert.Throws<ArgumentNullException>(() => new SymbolsPackageMessageService(CoreMessageServiceMock.Object, EmailConfigurationAccessorMock.Object, null));
            Assert.Equal("logger", ex.ParamName);
        }

        [Fact]
        public void SendPackagePublishedEmailMethodCallsCoreMessageService()
        {
            var expectedPackageUrl = string.Format(EmailConfiguration.PackageUrlTemplate, SymbolPackage.Package.PackageRegistration.Id, SymbolPackage.Package.NormalizedVersion);
            var expectedSupportUrl = string.Format(EmailConfiguration.PackageSupportTemplate, SymbolPackage.Package.PackageRegistration.Id, SymbolPackage.Package.NormalizedVersion);

            var service = new SymbolsPackageMessageService(CoreMessageServiceMock.Object, EmailConfigurationAccessorMock.Object, LoggerMock.Object);

            var ex = Record.Exception(() => service.SendPublishedMessageAsync(SymbolPackage).Wait());
            Assert.Null(ex);

            CoreMessageServiceMock
                .Verify(cms => cms.SendSymbolPackageAddedNoticeAsync(SymbolPackage, expectedPackageUrl, expectedSupportUrl, ValidSettingsUrl, It.IsAny<IEnumerable<string>>()), Times.Once());
            CoreMessageServiceMock
                .Verify(cms => cms.SendSymbolPackageAddedNoticeAsync(It.IsAny<SymbolPackage>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>()), Times.Once());
        }

        [Fact]
        public async Task SendPackagePublishedEmailThrowsWhenPackageIsNull()
        {
            var service = new SymbolsPackageMessageService(CoreMessageServiceMock.Object, EmailConfigurationAccessorMock.Object, LoggerMock.Object);
            var ex = await Assert.ThrowsAsync<ArgumentNullException>(() => service.SendPublishedMessageAsync(null));
            Assert.Equal("symbolPackage", ex.ParamName);
        }

        [Fact]
        public void SendPackageValidationFailedMessageCallsCoreMessageService()
        {
            var expectedPackageUrl = string.Format(EmailConfiguration.PackageUrlTemplate, SymbolPackage.Package.PackageRegistration.Id, SymbolPackage.Package.NormalizedVersion);
            var expectedSupportUrl = string.Format(EmailConfiguration.PackageSupportTemplate, SymbolPackage.Package.PackageRegistration.Id, SymbolPackage.Package.NormalizedVersion);

            var service = new SymbolsPackageMessageService(CoreMessageServiceMock.Object, EmailConfigurationAccessorMock.Object, LoggerMock.Object);

            var ex = Record.Exception(() => service.SendValidationFailedMessageAsync(SymbolPackage, ValidationSet).Wait());
            Assert.Null(ex);

            CoreMessageServiceMock
                .Verify(cms => cms.SendSymbolPackageValidationFailedNoticeAsync(SymbolPackage, ValidationSet, expectedPackageUrl, expectedSupportUrl, EmailConfiguration.AnnouncementsUrl, EmailConfiguration.TwitterUrl), Times.Once());
            CoreMessageServiceMock
                .Verify(cms => cms.SendSymbolPackageValidationFailedNoticeAsync(It.IsAny<SymbolPackage>(), It.IsAny<PackageValidationSet>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once());
        }

        [Fact]
        public async Task SendPackageValidationFailedMessageThrowsWhenPackageIsNull()
        {
            var service = new SymbolsPackageMessageService(CoreMessageServiceMock.Object, EmailConfigurationAccessorMock.Object, LoggerMock.Object);
            var ex = await Assert.ThrowsAsync<ArgumentNullException>(() => service.SendValidationFailedMessageAsync(null, new PackageValidationSet()));
            Assert.Equal("symbolPackage", ex.ParamName);
        }

        [Fact]
        public async Task SendPackageValidationFailedMessageThrowsWhenValidationSetIsNull()
        {
            var service = new SymbolsPackageMessageService(CoreMessageServiceMock.Object, EmailConfigurationAccessorMock.Object, LoggerMock.Object);
            var ex = await Assert.ThrowsAsync<ArgumentNullException>(() => service.SendValidationFailedMessageAsync(new SymbolPackage(), null));
            Assert.Equal("validationSet", ex.ParamName);
        }

        private const string ValidSettingsUrl = "https://example.com";
        public SymbolsMessageServiceFacts()
        {
            EmailConfiguration = new EmailConfiguration
            {
                PackageUrlTemplate = "https://example.com/package/{0}/{1}",
                PackageSupportTemplate = "https://example.com/packageSupport/{0}/{1}",
                EmailSettingsUrl = ValidSettingsUrl,
                AnnouncementsUrl = "https://announcements.com",
                TwitterUrl = "https://twitter.com/nuget"
            };

            EmailConfigurationAccessorMock
                .SetupGet(eca => eca.Value)
                .Returns(EmailConfiguration);

            var package = new Package
            {
                PackageRegistration = new PackageRegistration { Id = "package" },
                Version = "1.2.3.4",
                NormalizedVersion = "1.2.3"
            };

            SymbolPackage = new SymbolPackage()
            {
                Package = package
            };

            ValidationSet = new PackageValidationSet();
        }

        private Mock<ICoreMessageService> CoreMessageServiceMock { get; set; } = new Mock<ICoreMessageService>();
        private Mock<IOptionsSnapshot<EmailConfiguration>> EmailConfigurationAccessorMock { get; set; } = new Mock<IOptionsSnapshot<EmailConfiguration>>();
        private Mock<ILogger<SymbolsPackageMessageService>> LoggerMock { get; set; } = new Mock<ILogger<SymbolsPackageMessageService>>();
        private SymbolPackage SymbolPackage { get; set; }
        private PackageValidationSet ValidationSet { get; set; }
        private EmailConfiguration EmailConfiguration { get; set; }
    }
}
