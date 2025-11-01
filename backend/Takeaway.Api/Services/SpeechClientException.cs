using System;
using System.Net;

namespace Takeaway.Api.Services;

public class SpeechClientException : Exception
{
    public SpeechClientException(string message, HttpStatusCode? statusCode = null, string? responseBody = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode? StatusCode { get; }

    public string? ResponseBody { get; }
}
