using RiotSharp.Http.Interfaces;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using RiotSharp.Misc;
using System;
using System.Threading;

namespace RiotSharp.Http
{
    /// <summary>
    /// A requester with a rate limiter
    /// </summary>
    public class RateLimitedRequester : RequesterBase, IRateLimitedRequester
    {
        public readonly IDictionary<TimeSpan, int> RateLimits;

        private ReaderWriterLockSlim rateLimitLock;

        public RateLimitedRequester(string apiKey, IDictionary<TimeSpan, int> rateLimits) : base(apiKey)
        {
            RateLimits = rateLimits;
            rateLimitLock = new ReaderWriterLockSlim();
        }

        private readonly Dictionary<Region, RateLimiter> rateLimiters = new Dictionary<Region, RateLimiter>();

        #region Public Methods

        public string CreateGetRequest(string relativeUrl, Region region, List<string> addedArguments = null,
            bool useHttps = true)
        {
            rootDomain = GetPlatformDomain(region);

            var request = PrepareRequest(relativeUrl, addedArguments, useHttps, HttpMethod.Get);

            return GetRateLimiter(region).HandleRateLimit(() =>
            {
                using (var response = Get(request))
                {
                    return GetResponseContent(response);
                }
            });
        }

        public async Task<string> CreateGetRequestAsync(string relativeUrl, Region region, List<string> addedArguments = null, 
            bool useHttps = true)
        {
            rootDomain = GetPlatformDomain(region);

            var request = PrepareRequest(relativeUrl, addedArguments, useHttps, HttpMethod.Get);
            
            return await GetRateLimiter(region).HandleRateLimitAsync(async () =>
            {
                using (var response = await GetAsync(request))
                {
                    return await GetResponseContentAsync(response);
                }
            });
        }

        public string CreatePostRequest(string relativeUrl, Region region, string body,
            List<string> addedArguments = null, bool useHttps = true)
        {
            rootDomain = GetPlatformDomain(region);

            var request = PrepareRequest(relativeUrl, addedArguments, useHttps, HttpMethod.Post);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            return GetRateLimiter(region).HandleRateLimit(() =>
            {
                using (var response = Post(request))
                {
                    return GetResponseContent(response);
                }
            });
        }

        public async Task<string> CreatePostRequestAsync(string relativeUrl, Region region, string body,
            List<string> addedArguments = null, bool useHttps = true)
        {
            rootDomain = GetPlatformDomain(region);

            var request = PrepareRequest(relativeUrl, addedArguments, useHttps, HttpMethod.Post);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            return await GetRateLimiter(region).HandleRateLimitAsync(async () =>
            {
                using (var response = await PostAsync(request))
                {
                    return await GetResponseContentAsync(response);
                }
            });
        }

        public bool CreatePutRequest(string relativeUrl, Region region, string body, List<string> addedArguments = null,
            bool useHttps = true)
        {
            rootDomain = GetPlatformDomain(region);

            var request = PrepareRequest(relativeUrl, addedArguments, useHttps, HttpMethod.Put);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            return GetRateLimiter(region).HandleRateLimit(() =>
            {
                using (var response = Put(request))
                {
                    return (int)response.StatusCode >= 200 && (int)response.StatusCode < 300;
                }
            });
        }

        public async Task<bool> CreatePutRequestAsync(string relativeUrl, Region region, string body,
            List<string> addedArguments = null, bool useHttps = true)
        {
            rootDomain = GetPlatformDomain(region);

            var request = PrepareRequest(relativeUrl, addedArguments, useHttps, HttpMethod.Put);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            return await GetRateLimiter(region).HandleRateLimitAsync(async () =>
            {
                using (var response = await PutAsync(request))
                {
                    return (int)response.StatusCode >= 200 && (int)response.StatusCode < 300;
                }
            });
        }

        #endregion

        /// <summary>
        /// Returns the respective region's RateLimiter, creating it if needed.
        /// </summary>
        /// <param name="region"></param>
        /// <returns></returns>
        private RateLimiter GetRateLimiter(Region region)
        {
            rateLimitLock.EnterReadLock();
            try
            {
                if (rateLimiters.TryGetValue(region, out RateLimiter rateLimiter))
                {
                    return rateLimiter;
                }
            }
            finally
            {
                rateLimitLock.ExitReadLock();
            }

            rateLimitLock.EnterUpgradeableReadLock();
            try
            {
                if (!rateLimiters.TryGetValue(region, out RateLimiter rateLimiter))
                {
                    rateLimiter = new RateLimiter(RateLimits);

                    rateLimitLock.EnterWriteLock();
                    try
                    {
                        rateLimiters[region] = rateLimiter;
                    }
                    finally
                    {
                        rateLimitLock.ExitWriteLock();
                    }
                }

                return rateLimiter;
            }
            finally
            {
                rateLimitLock.ExitUpgradeableReadLock();
            }
        }
    }
}
