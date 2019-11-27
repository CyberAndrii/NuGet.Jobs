﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace NuGet.Jobs.Monitoring.GitHubVulnerabilitiesLag
{
    /// <summary>
    /// https://developer.github.com/v4/object/securityadvisory/
    /// </summary>
    public class SecurityAdvisory : INode
    {
        public DateTimeOffset UpdatedAt { get; set; }
        public ConnectionResponseData<SecurityVulnerability> Vulnerabilities { get; set; }
    }
}