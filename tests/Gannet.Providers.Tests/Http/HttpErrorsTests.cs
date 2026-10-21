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
}
