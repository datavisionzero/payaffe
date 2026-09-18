using System.Net;

namespace Payaffe.Sdk;

public sealed class PayaffeApiException : Exception
{
    internal PayaffeApiException(
        HttpStatusCode statusCode,
        PayaffeErrorCode code,
        string? correlationId,
        IReadOnlyDictionary<string, IReadOnlyList<string>> validationErrors,
        TimeSpan? retryAfter,
        string? title)
        : base(CreateMessage(statusCode, code, correlationId, title))
    {
        StatusCode = statusCode;
        Code = code;
        CorrelationId = correlationId;
        ValidationErrors = validationErrors;
        RetryAfter = retryAfter;
    }

    public HttpStatusCode StatusCode { get; }

    public PayaffeErrorCode Code { get; }

    public string? CorrelationId { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> ValidationErrors { get; }

    public TimeSpan? RetryAfter { get; }

    private static string CreateMessage(
        HttpStatusCode statusCode,
        PayaffeErrorCode code,
        string? correlationId,
        string? title)
    {
        string message = string.IsNullOrWhiteSpace(title)
            ? $"Payaffe returned HTTP {(int)statusCode} with code '{code}'."
            : $"Payaffe returned HTTP {(int)statusCode} with code '{code}': {title}";

        return string.IsNullOrWhiteSpace(correlationId)
            ? message
            : $"{message} Correlation ID: {correlationId}.";
    }
}
