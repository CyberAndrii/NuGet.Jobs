﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.Search.Models;
using NuGetGallery;

namespace NuGet.Services.AzureSearch.SearchService
{
    public class SearchParametersBuilder : ISearchParametersBuilder
    {
        public const int DefaultTake = 20;
        private const int MaximumTake = 1000;

        private static readonly string Ascending = " asc";
        private static readonly string Descending = " desc";
        private static readonly List<string> LastEditedDescending = new List<string> { IndexFields.LastEdited + Descending };
        private static readonly List<string> PublishedDescending = new List<string> { IndexFields.Published + Descending };
        private static readonly List<string> SortableTitleAscending = new List<string> { IndexFields.SortableTitle + Ascending };
        private static readonly List<string> SortableTitleDescending = new List<string> { IndexFields.SortableTitle + Descending };

        public SearchParameters GetSearchParametersForV2Search(V2SearchRequest request)
        {
            var searchParameters = NewSearchParameters();

            if (request.CountOnly)
            {
                searchParameters.Skip = 0;
                searchParameters.Top = 0;
                searchParameters.OrderBy = null;
            }
            else
            {
                ApplyPaging(searchParameters, request);
                searchParameters.OrderBy = GetOrderBy(request.SortBy);
            }

            if (request.IgnoreFilter)
            {
                // Note that the prerelease flag has no effect when IgnoreFilter is true.

                if (!request.IncludeSemVer2)
                {
                    searchParameters.Filter = $"{IndexFields.SemVerLevel} ne {SemVerLevelKey.SemVer2}";
                }
            }
            else
            {
                ApplySearchIndexFilter(searchParameters, request);
            }

            return searchParameters;
        }

        public SearchParameters GetSearchParametersForV3Search(V3SearchRequest request)
        {
            var searchParameters = NewSearchParameters();

            ApplyPaging(searchParameters, request);
            ApplySearchIndexFilter(searchParameters, request);

            return searchParameters;
        }

        private static SearchParameters NewSearchParameters()
        {
            return new SearchParameters
            {
                IncludeTotalResultCount = true,
                QueryType = QueryType.Full,
            };
        }

        private static void ApplyPaging(SearchParameters searchParameters, SearchRequest request)
        {
            searchParameters.Skip = request.Skip < 0 ? 0 : request.Skip;
            searchParameters.Top = request.Take < 0 || request.Take > MaximumTake ? DefaultTake : request.Take;
        }

        private static void ApplySearchIndexFilter(SearchParameters searchParameters, SearchRequest request)
        {
            var searchFilters = SearchFilters.Default;

            if (request.IncludePrerelease)
            {
                searchFilters |= SearchFilters.IncludePrerelease;
            }

            if (request.IncludeSemVer2)
            {
                searchFilters |= SearchFilters.IncludeSemVer2;
            }

            searchParameters.Filter = $"{IndexFields.Search.SearchFilters} eq '{DocumentUtilities.GetSearchFilterString(searchFilters)}'";
        }

        public string GetSearchTextForV2Search(V2SearchRequest request)
        {
            var query = request.Query;

            if (request.LuceneQuery)
            {
                // TODO: convert a leading "id:" to "packageid:"
            }

            return GetLuceneQuery(query);
        }

        public string GetSearchTextForV3Search(V3SearchRequest request)
        {
            return GetLuceneQuery(request.Query);
        }

        private static string GetLuceneQuery(string query)
        {
            // TODO: query parsing
            return query ?? "*";
        }

        private static IList<string> GetOrderBy(V2SortBy sortBy)
        {
            IList<string> orderBy;
            switch (sortBy)
            {
                case V2SortBy.Popularity:
                    orderBy = null;
                    break;
                case V2SortBy.LastEditedDescending:
                    orderBy = LastEditedDescending;
                    break;
                case V2SortBy.PublishedDescending:
                    orderBy = PublishedDescending;
                    break;
                case V2SortBy.SortableTitleAsc:
                    orderBy = SortableTitleAscending;
                    break;
                case V2SortBy.SortableTitleDesc:
                    orderBy = SortableTitleDescending;
                    break;
                default:
                    throw new ArgumentException($"The provided {nameof(V2SortBy)} is not supported.", nameof(sortBy));
            }

            return orderBy;
        }
    }
}
