﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Services.AzureSearch;
using NuGet.Services.AzureSearch.Catalog2AzureSearch;

namespace NuGet.Jobs
{
    public class Job : JsonConfigurationJob
    {
        private const string ConfigurationSectionName = "Catalog2AzureSearch";

        public override async Task Run()
        {
            ServicePointManager.DefaultConnectionLimit = 64;
            ServicePointManager.MaxServicePointIdleTime = 10000;

            await _serviceProvider
                .GetRequiredService<Catalog2AzureSearchCommand>()
                .ExecuteAsync();
        }

        protected override void ConfigureAutofacServices(ContainerBuilder containerBuilder)
        {
            containerBuilder.AddAzureSearch();
        }

        protected override void ConfigureJobServices(IServiceCollection services, IConfigurationRoot configurationRoot)
        {
            services.AddAzureSearch();

            services.Configure<Catalog2AzureSearchConfiguration>(configurationRoot.GetSection(ConfigurationSectionName));
            services.Configure<AzureSearchConfiguration>(configurationRoot.GetSection(ConfigurationSectionName));
            services.AddTransient<Catalog2AzureSearchCommand>();
        }
    }
}
