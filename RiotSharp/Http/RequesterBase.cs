using RiotSharp.Misc;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace RiotSharp.Http
{
    public abstract class RequesterBase
    {
        protected string rootDomain;
        protected const string platformDomain = ".api.riotgames.com";
        private readonly HttpTracingClient httpClient;

        public string ApiKey { get; private set; }

        protected RequesterBase(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentNullException(nameof(apiKey));
            ApiKey = apiKey;
            httpClient = new HttpTracingClient();
            httpClient.DefaultRequestHeaders.Add("X-Riot-Token", ApiKey);
        }

        #region Protected Methods

        /// <summary>
        /// Send a get request synchronously.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        /// <exception cref="RiotSharpException">Thrown if an Http error occurs. Contains the Http error code and error message.</exception>
        protected HttpResponseMessage Get(HttpRequestMessage request)
        {
            var response = httpClient.GetAsync(request.RequestUri).Result;
            if (!response.IsSuccessStatusCode)
            {
                HandleRequestFailure(response);
            }
            return response;
        }

        /// <summary>
        /// Send a get request asynchronously.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        /// <exception cref="RiotSharpException">Thrown if an Http error occurs. Contains the Http error code and error message.</exception>
        protected async Task<HttpResponseMessage> GetAsync(HttpRequestMessage request)
        {
            var response = await httpClient.GetAsync(request.RequestUri);
            if (!response.IsSuccessStatusCode)
            {
                HandleRequestFailure(response);
            }
            return response;
        }


        /// <summary>
        /// Send a put request synchronously.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        /// <exception cref="RiotSharpException">Thrown if an Http error occurs. Contains the Http error code and error message.</exception>
        protected HttpResponseMessage Put(HttpRequestMessage request)
        {
            var response = httpClient.PutAsync(request.RequestUri, request.Content).Result;
            if (!response.IsSuccessStatusCode)
            {
                HandleRequestFailure(response);
            }
            return response;
        }

        /// <summary>
        /// Send a put request asynchronously.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        /// <exception cref="RiotSharpException">Thrown if an Http error occurs. Contains the Http error code and error message.</exception>
        protected async Task<HttpResponseMessage> PutAsync(HttpRequestMessage request)
        {
            var response = await httpClient.PutAsync(request.RequestUri, request.Content);
            if (!response.IsSuccessStatusCode)
            {
                HandleRequestFailure(response);
            }
            return response;
        }

        /// <summary>
        /// Send a post request synchronously.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        /// <exception cref="RiotSharpException">Thrown if an Http error occurs. Contains the Http error code and error message.</exception>
        protected HttpResponseMessage Post(HttpRequestMessage request)
        {
            var response = httpClient.PostAsync(request.RequestUri, request.Content).Result;
            if (!response.IsSuccessStatusCode)
            {
                HandleRequestFailure(response);
            }
            return response;
        }

        /// <summary>
        /// Send a post request asynchronously.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        /// <exception cref="RiotSharpException">Thrown if an Http error occurs. Contains the Http error code and error message.</exception>
        protected async Task<HttpResponseMessage> PostAsync(HttpRequestMessage request)
        {
            var response = await httpClient.PostAsync(request.RequestUri, request.Content);
            if (!response.IsSuccessStatusCode)
            {
                HandleRequestFailure(response);
            }
            return response;
        }

        protected HttpRequestMessage PrepareRequest(string relativeUrl, List<string> addedArguments,
            bool useHttps, HttpMethod httpMethod)
        {
            var scheme = useHttps ? "https" : "http";
            var url = addedArguments == null ?
                $"{scheme}://{rootDomain}{relativeUrl}" :
                $"{scheme}://{rootDomain}{relativeUrl}?{BuildArgumentsString(addedArguments)}";

            var request = new HttpRequestMessage(httpMethod, url);

            return request;
        }

        protected string BuildArgumentsString(List<string> arguments)
        {
            return string.Join("&", arguments.Where(arg => arg != string.Empty));
        }

        protected void HandleRequestFailure(HttpResponseMessage response)
        {
            Trace.TraceWarning($"Request {response.RequestMessage.RequestUri} failed {response.StatusCode}: {response.ReasonPhrase} with headers {string.Join(";", response.Headers)}");

            switch (response.StatusCode)
            {
                case HttpStatusCode.ServiceUnavailable:
                    throw new RiotSharpException("503, Service unavailable", response.StatusCode);
                case HttpStatusCode.InternalServerError:
                    throw new RiotSharpException("500, Internal server error", response.StatusCode);
                case HttpStatusCode.Unauthorized:
                    throw new RiotSharpException("401, Unauthorized", response.StatusCode);
                case HttpStatusCode.BadRequest:
                    throw new RiotSharpException("400, Bad request", response.StatusCode);
                case HttpStatusCode.NotFound:
                    throw new RiotSharpException("404, Resource not found", response.StatusCode);
                case HttpStatusCode.Forbidden:
                    throw new RiotSharpException("403, Forbidden", response.StatusCode);
                case (HttpStatusCode)429:
                    throw new RiotSharpException("429, Rate Limit Exceeded", response.StatusCode);
                default:
                    throw new RiotSharpException("Unexpected failure", response.StatusCode);
            }
        }

        protected string GetResponseContent(HttpResponseMessage response)
        {
            var result = string.Empty;

            using (var content = response.Content)
            { 
                result = content.ReadAsStringAsync().Result;
            }
            return result;
        }

        protected async Task<string> GetResponseContentAsync(HttpResponseMessage response)
        {
            Task<string> result = null;
            using (response)
            {
                using (var content = response.Content)
                {
                    result = content.ReadAsStringAsync();
                }
            }
            return await result;
        }

        protected string GetPlatformDomain(Region region)
        {
            return GetPlatform(region) + platformDomain;
        }

        private string GetPlatform(Region region)
        {
            switch (region)
            {
                case Region.br:
                    return "br1";
                case Region.eune:
                    return "eun1";
                case Region.euw:
                    return "euw1";
                case Region.jp:
                    return "jp1";
                case Region.kr:
                    return "kr";
                case Region.lan:
                    return "la1";
                case Region.las:
                    return "la2";
                case Region.na:
                    return "na1";
                case Region.oce:
                    return "oc1";
                case Region.tr:
                    return "tr1";
                case Region.ru:
                    return "ru";
                case Region.global:
                    return "global";
                default:
                    throw new NotImplementedException();
            }
        }

        #endregion
    }
}
