using System.Net;
using Gannet.Core.Services;
using Gannet.Providers.Http;

namespace Gannet.Providers.Tests.Http;

public class HttpErrorsTests
{
    private static HttpResponseMessage Response(HttpStatusCode status) => new(status) { ReasonPhrase = status.ToString() };

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, typeof(AuthRequiredException))]
    [InlineData(HttpStatusCode.Forbidden, typeof(AuthRequiredException))]
    [InlineData(HttpStatusCode.PaymentRequired, typeof(QuotaExhaustedException))]
    [InlineData(HttpStatusCode.TooManyRequests, typeof(RateLimitedException))]
    [InlineData(HttpStatusCode.BadGateway, typeof(ServiceFailedException))]
    public async Task Api_status_codes_map_to_typed_errors(HttpStatusCode status, Type expected)
    {
        var error = await Assert.ThrowsAnyAsync<ServiceException>(() => HttpErrors.EnsureSuccessAsync(Response(status), "Brave", website: false, CancellationToken.None));
        Assert.IsType(expected, error);
        Assert.Equal("Brave", error.Service);
    }

    [Fact]
    public async Task Retry_after_is_read()
    {
        var response = Response(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
        var error = await Assert.ThrowsAsync<RateLimitedException>(() => HttpErrors.EnsureSuccessAsync(response, "Tavily", false, CancellationToken.None));
        Assert.Equal(TimeSpan.FromSeconds(30), error.RetryAfter);
        Assert.Equal("rate-limited", error.ErrorKind);
    }

    [Fact]
    public async Task Cloudflare_challenge_header_is_a_challenge_even_on_200()
    {
        var response = Response(HttpStatusCode.OK);
        response.Headers.Add("cf-mitigated", "challenge");
        await Assert.ThrowsAsync<ChallengeDetectedException>(() => HttpErrors.EnsureSuccessAsync(response, "Built-in reader", true, CancellationToken.None));
    }

    [Fact]
    public async Task Website_403_is_a_challenge_not_an_auth_problem()
    {
        await Assert.ThrowsAsync<ChallengeDetectedException>(() => HttpErrors.EnsureSuccessAsync(Response(HttpStatusCode.Forbidden), "Built-in reader", website: true, CancellationToken.None));
    }

    [Fact]
    public async Task Messages_never_include_the_response_body()
    {
        var response = Response(HttpStatusCode.BadRequest);
        response.Content = new StringContent("secret-key-echo sk-12345 and result text");
        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => HttpErrors.EnsureSuccessAsync(response, "Exa", false, CancellationToken.None));
        Assert.DoesNotContain("sk-12345", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Messages_never_include_the_reason_phrase()
    {
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError) { ReasonPhrase = "Bad key sk-LEAK123" };
        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => HttpErrors.EnsureSuccessAsync(response, "Exa", false, CancellationToken.None));
        Assert.DoesNotContain("sk-LEAK123", error.Message, StringComparison.Ordinal);
        Assert.Contains("500", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Retry_after_date_in_the_future_is_a_positive_delay()
    {
        var response = Response(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(5));
        var error = await Assert.ThrowsAsync<RateLimitedException>(() => HttpErrors.EnsureSuccessAsync(response, "Tavily", false, CancellationToken.None));
        Assert.InRange(error.RetryAfter!.Value, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task Retry_after_date_in_the_past_is_zero()
    {
        var response = Response(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(-5));
        var error = await Assert.ThrowsAsync<RateLimitedException>(() => HttpErrors.EnsureSuccessAsync(response, "Tavily", false, CancellationToken.None));
        Assert.Equal(TimeSpan.Zero, error.RetryAfter);
    }

    [Fact]
    public async Task Missing_retry_after_is_null()
    {
        var error = await Assert.ThrowsAsync<RateLimitedException>(() => HttpErrors.EnsureSuccessAsync(Response(HttpStatusCode.TooManyRequests), "Tavily", false, CancellationToken.None));
        Assert.Null(error.RetryAfter);
    }

    [Fact]
    public void Failures_are_a_faulted_task_not_a_synchronous_throw()
    {
        var task = HttpErrors.EnsureSuccessAsync(Response(HttpStatusCode.BadGateway), "Brave", false, CancellationToken.None);
        Assert.True(task.IsFaulted);
        Assert.IsType<ServiceFailedException>(task.Exception!.InnerException);
    }

    [Fact]
    public async Task A_plain_success_passes()
    {
        await HttpErrors.EnsureSuccessAsync(Response(HttpStatusCode.OK), "Brave", false, CancellationToken.None);
        await HttpErrors.EnsureSuccessAsync(Response(HttpStatusCode.NoContent), "Brave", true, CancellationToken.None);
    }

    [Fact]
    public async Task A_cf_mitigated_header_that_is_not_a_challenge_passes()
    {
        var response = Response(HttpStatusCode.OK);
        response.Headers.Add("cf-mitigated", "something-else");
        await HttpErrors.EnsureSuccessAsync(response, "Reader", true, CancellationToken.None);
    }
}
