using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace RiotSharp.Http
{
    internal class RateLimitException : RiotSharpException
    {
        public RateLimitException(string message, HttpStatusCode statusCode, TimeSpan retryAfter)
            : base(message, statusCode)
        {
            RetryAfter = retryAfter;
        }

        public TimeSpan RetryAfter { get; protected set; }
    }
}
