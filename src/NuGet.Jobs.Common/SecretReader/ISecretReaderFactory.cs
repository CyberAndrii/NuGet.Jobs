﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using NuGet.Services.KeyVault;

namespace NuGet.Jobs
{
    public interface ISecretReaderFactory
    {
        ISecretReader CreateSecterReader(IDictionary<string, string> settings);

        ISecretInjector CreateSecretInjector(ISecretReader secretReader);
    }
}
