﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Protocol.Core.Types;
using NuGet.Protocol.Core.v3.Data;
using NuGet.Versioning;

namespace NuGet.Protocol.Core.v3
{
    /// <summary>
    /// Retrieves and caches service index.json files
    /// ServiceIndexResourceV3 stores the json, all work is done in the provider
    /// </summary>
    public class ServiceIndexResourceV3Provider : ResourceProvider
    {
        private readonly ConcurrentDictionary<string, ServiceIndexResourceV3> _cache;

        public ServiceIndexResourceV3Provider()
            : base(typeof(ServiceIndexResourceV3),
                  nameof(ServiceIndexResourceV3Provider),
                  NuGetResourceProviderPositions.Last)
        {
            _cache = new ConcurrentDictionary<string, ServiceIndexResourceV3>();
        }

        // TODO: refresh the file when it gets old
        public override async Task<Tuple<bool, INuGetResource>> TryCreate(SourceRepository source, CancellationToken token)
        {
            ServiceIndexResourceV3 index = null;

            var url = source.PackageSource.Source;

            // the file type can easily rule out if we need to request the url
            if (url.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                // check the cache before downloading the file
                if (!_cache.TryGetValue(url, out index))
                {
                    var messageHandlerResource = await source.GetResourceAsync<HttpHandlerResource>(token);

                    var client = new DataClient(messageHandlerResource.MessageHandler);

                    var json = await client.GetJObjectAsync(new Uri(url), token);

                    if (json != null)
                    {
                        // Use SemVer instead of NuGetVersion, the service index should always be
                        // in strict SemVer format
                        SemanticVersion version = null;
                        var status = json.Value<string>("version");
                        if (status != null
                            && SemanticVersion.TryParse(status, out version))
                        {
                            if (version.Major == 3)
                            {
                                index = new ServiceIndexResourceV3(json, DateTime.UtcNow);
                            }
                        }
                    }
                }

                // cache the value even if it is null to avoid checking it again later
                _cache.TryAdd(url, index);
            }

            return new Tuple<bool, INuGetResource>(index != null, index);
        }
    }
}
