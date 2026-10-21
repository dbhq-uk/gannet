namespace Gannet.Core.Services;

/// <summary>A failure talking to an outside service. Messages are written by gannet and never include a response body.</summary>
public abstract class ServiceException(string service, string message) : Exception(message)
{
    public string Service { get; } = service;

    /// <summary>One of: rate-limited, challenge, auth-required, quota-exhausted, failed.</summary>
    public abstract string ErrorKind { get; }
}

public sealed class RateLimitedException(string service, TimeSpan? retryAfter, string message) : ServiceException(service, message)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;

    public override string ErrorKind => "rate-limited";
}

public sealed class ChallengeDetectedException(string service, string message) : ServiceException(service, message)
{
    public override string ErrorKind => "challenge";
}

public sealed class AuthRequiredException(string service, string message) : ServiceException(service, message)
{
    public override string ErrorKind => "auth-required";
}

public sealed class QuotaExhaustedException(string service, string message) : ServiceException(service, message)
{
    public override string ErrorKind => "quota-exhausted";
}

public sealed class ServiceFailedException(string service, string message) : ServiceException(service, message)
{
    public override string ErrorKind => "failed";
}

public sealed class UnknownServiceException(string kind, string name, IEnumerable<string> known)
    : ArgumentException($"There is no {kind} called '{name}'. Configured: {string.Join(", ", known)}.")
{
    public string Kind { get; } = kind;

    public string Name { get; } = name;
}
