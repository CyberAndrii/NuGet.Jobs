﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;

namespace Gallery.Maintenance
{
    public class RenameExpiredDeletedAccountsTask : UpdateEntityTask<ExpiredDeletedAccount>
    {
        public RenameExpiredDeletedAccountsTask(ILogger<RenameExpiredDeletedAccountsTask> logger)
            : base(logger)
        {
        }

        protected override string GetSelectQuery() => @"
SELECT u.[Key], u.[Username], d.[DeletedOn]
FROM [dbo].[Users] u
INNER JOIN [dbo].[AccountDeletes] d ON u.[Key] = d.[DeletedAccountKey]
WHERE GETUTCDATE() > DATEADD(year, 1, d.[DeletedOn]) AND [WasUsernameReleased] = 0
";

        protected override string GetUpdateQuery() => @"
UPDATE [dbo].[Users]
SET [Username] = NEWID()
WHERE [Key] IN ({0})

UPDATE [dbo].[AccountDeletes]
SET [WasUsernameReleased] = 1
WHERE [DeletedAccountKey] IN ({0})
";

        protected override int GetKey(ExpiredDeletedAccount deletedAccount)
        {
            _logger.LogInformation(
                "Found expired deleted account key deleted on='{deletedOn}'",
                deletedAccount.DeletedOn);

            return deletedAccount.Key;
        }

        /// <summary>
        /// The User row and the AccountDeletes row for each deleted account will both be updated.
        /// </summary>
        protected override int GetUpdatedRowsPerKey() => 2;
    }
}