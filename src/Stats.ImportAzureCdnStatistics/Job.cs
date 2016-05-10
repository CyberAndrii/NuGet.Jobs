﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.RetryPolicies;
using NuGet.Jobs;
using NuGet.Services.Logging;
using Stats.AzureCdnLogs.Common;

namespace Stats.ImportAzureCdnStatistics
{
    public class Job
        : JobBase
    {
        private string _azureCdnAccountNumber;
        private string _cloudStorageContainerName;
        private AzureCdnPlatform _azureCdnPlatform;
        private SqlConnectionStringBuilder _targetDatabase;
        private CloudStorageAccount _cloudStorageAccount;
        private ILoggerFactory _loggerFactory;
        private ILogger _logger;

        public override bool Init(IDictionary<string, string> jobArgsDictionary)
        {
            try
            {
                var instrumentationKey = JobConfigurationManager.TryGetArgument(jobArgsDictionary, JobArgumentNames.InstrumentationKey);
                ApplicationInsights.Initialize(instrumentationKey);

                _loggerFactory = LoggingSetup.CreateLoggerFactory();
                _logger = _loggerFactory.CreateLogger<Job>();

                var azureCdnPlatform = JobConfigurationManager.GetArgument(jobArgsDictionary, JobArgumentNames.AzureCdnPlatform);
                var cloudStorageAccountConnectionString = JobConfigurationManager.GetArgument(jobArgsDictionary, JobArgumentNames.AzureCdnCloudStorageAccount);
                var databaseConnectionString = JobConfigurationManager.GetArgument(jobArgsDictionary, JobArgumentNames.StatisticsDatabase);
                _cloudStorageAccount = ValidateAzureCloudStorageAccount(cloudStorageAccountConnectionString);

                _targetDatabase = new SqlConnectionStringBuilder(databaseConnectionString);
                _azureCdnAccountNumber = JobConfigurationManager.GetArgument(jobArgsDictionary, JobArgumentNames.AzureCdnAccountNumber);
                _azureCdnPlatform = ValidateAzureCdnPlatform(azureCdnPlatform);
                _cloudStorageContainerName = ValidateAzureContainerName(JobConfigurationManager.GetArgument(jobArgsDictionary, JobArgumentNames.AzureCdnCloudStorageContainerName));
            }
            catch (Exception exception)
            {
                _logger.LogCritical("Failed to initialize job!", exception);

                return false;
            }

            return true;
        }

        public override async Task<bool> Run()
        {
            try
            {
                // construct a cloud blob client for the configured storage account
                var cloudBlobClient = _cloudStorageAccount.CreateCloudBlobClient();
                cloudBlobClient.DefaultRequestOptions.RetryPolicy = new ExponentialRetry(TimeSpan.FromSeconds(10), 5);

                // Get the source blob container (containing compressed log files)
                // and construct a log source (fetching raw logs from the source blob container)
                var sourceBlobContainer = cloudBlobClient.GetContainerReference(_cloudStorageContainerName);
                var blobLeaseManager = new LogFileProvider(sourceBlobContainer, _loggerFactory);

                // Get the target blob container (for archiving decompressed log files)
                var targetBlobContainer = cloudBlobClient.GetContainerReference(_cloudStorageContainerName + "-archive");
                await targetBlobContainer.CreateIfNotExistsAsync();

                // Get the dead-letter table (corrupted or failed blobs will end up there)
                var deadLetterBlobContainer = cloudBlobClient.GetContainerReference(_cloudStorageContainerName + "-deadletter");
                await deadLetterBlobContainer.CreateIfNotExistsAsync();

                // Create a parser
                var logProcessor = new LogFileProcessor(targetBlobContainer, deadLetterBlobContainer, _targetDatabase, _loggerFactory);

                // Get the next to-be-processed raw log file using the cdn raw log file name prefix
                var prefix = string.Format(CultureInfo.InvariantCulture, "{0}_{1}_", _azureCdnPlatform.GetRawLogFilePrefix(), _azureCdnAccountNumber);

                // Get next raw log file to be processed
                var leasedLogFiles = await blobLeaseManager.LeaseNextLogFilesToBeProcessedAsync(prefix);
                foreach (var leasedLogFile in leasedLogFiles)
                {
                    var packageTranslator = new PackageTranslator("packagetranslations.json");
                    var packageStatisticsParser = new PackageStatisticsParser(packageTranslator);
                    await logProcessor.ProcessLogFileAsync(leasedLogFile, packageStatisticsParser);
                    leasedLogFile.Dispose();
                }
            }
            catch (Exception exception)
            {
                _logger.LogCritical("Job run failed!", exception);

                return false;
            }

            return true;
        }

        private static CloudStorageAccount ValidateAzureCloudStorageAccount(string cloudStorageAccount)
        {
            if (string.IsNullOrEmpty(cloudStorageAccount))
            {
                throw new ArgumentException("Job parameter for Azure CDN Cloud Storage Account is not defined.");
            }

            CloudStorageAccount account;
            if (CloudStorageAccount.TryParse(cloudStorageAccount, out account))
            {
                return account;
            }
            throw new ArgumentException("Job parameter for Azure CDN Cloud Storage Account is invalid.");
        }

        private static AzureCdnPlatform ValidateAzureCdnPlatform(string azureCdnPlatform)
        {
            if (string.IsNullOrEmpty(azureCdnPlatform))
            {
                throw new ArgumentException("Job parameter for Azure CDN Platform is not defined.");
            }

            AzureCdnPlatform value;
            if (Enum.TryParse(azureCdnPlatform, true, out value))
            {
                return value;
            }
            throw new ArgumentException("Job parameter for Azure CDN Platform is invalid. Allowed values are: HttpLargeObject, HttpSmallObject, ApplicationDeliveryNetwork, FlashMediaStreaming.");
        }

        private static string ValidateAzureContainerName(string containerName)
        {
            if (string.IsNullOrWhiteSpace(containerName))
            {
                throw new ArgumentException("Job parameter for Azure Storage Container Name is not defined.");
            }
            return containerName;
        }
    }
}