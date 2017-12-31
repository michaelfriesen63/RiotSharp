using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RiotSharp.Http
{
    internal class HttpClientLogger : DelegatingHandler
    {
        public HttpClientLogger()
        {
            InnerHandler = new HttpClientHandler();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Trace.TraceInformation($"HTTP request {request.Method} {request.RequestUri}");

            HttpResponseMessage response = await base.SendAsync(request, cancellationToken);

            Trace.TraceInformation($"HTTP response {(int)response.StatusCode} {response.ReasonPhrase} for URI {request.RequestUri}");

            return response;
        }
    }
}
