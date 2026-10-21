using System.Net;
using System.Text;
using System.Text.Json;
using Gannet.Core.Services;
using Gannet.Providers.Http;

namespace Gannet.Providers.Tests.Http;

public class JsonReadTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static HttpResponseMessage Body(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };

    [Fact]
    public void Str_reads_a_string_and_is_empty_otherwise()
    {
        var element = Parse("""{"a":"x","n":3,"z":null}""");
        Assert.Equal("x", element.Str("a"));
        Assert.Equal(string.Empty, element.Str("n"));
        Assert.Equal(string.Empty, element.Str("z"));
        Assert.Equal(string.Empty, element.Str("missing"));
        Assert.Equal(string.Empty, Parse("[1]").Str("a"));
        Assert.Equal(string.Empty, default(JsonElement).Str("a"));
    }

    [Fact]
    public void Prop_returns_the_property_or_an_undefined_element()
    {
        var element = Parse("""{"a":{"b":"x"}}""");
        Assert.Equal("x", element.Prop("a").Prop("b").GetString());
        Assert.Equal(JsonValueKind.Undefined, element.Prop("missing").ValueKind);
        Assert.Equal(JsonValueKind.Undefined, Parse("[1]").Prop("a").ValueKind);
        Assert.Equal(JsonValueKind.Undefined, default(JsonElement).Prop("a").ValueKind);
    }

    [Fact]
    public void Items_enumerates_arrays_and_is_empty_for_anything_else()
    {
        Assert.Equal(2, Parse("[1,2]").Items().Count());
        Assert.Empty(Parse("""{"a":1}""").Items());
        Assert.Empty(Parse("\"text\"").Items());
        Assert.Empty(default(JsonElement).Items());
    }

    [Theory]
    [InlineData("https://example.com/a", true)]
    [InlineData("http://example.com/a", true)]
    [InlineData("ftp://example.com/a", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("/relative", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("https://user:pw@example.com/a", false)]
    [InlineData("https://user@example.com/a", false)]
    public void WebUri_accepts_only_plain_web_urls(string? text, bool accepted) =>
        Assert.Equal(accepted, JsonRead.WebUri(text) is not null);

    [Fact]
    public void Date_parses_or_returns_null()
    {
        Assert.Null(JsonRead.Date(null));
        Assert.Null(JsonRead.Date("not a date"));
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero), JsonRead.Date("2026-10-21"));
    }

    [Fact]
    public async Task ParseAsync_reads_json()
    {
        using var document = await JsonRead.ParseAsync(Body("""{"a":"x"}"""), "Brave", CancellationToken.None);
        Assert.Equal("x", document.RootElement.Str("a"));
    }

    [Fact]
    public async Task ParseAsync_reads_json_null_without_failing()
    {
        using var document = await JsonRead.ParseAsync(Body("null"), "Brave", CancellationToken.None);
        Assert.Equal(JsonValueKind.Null, document.RootElement.ValueKind);
        Assert.Equal(string.Empty, document.RootElement.Str("a"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>not json</html>")]
    [InlineData("{\"a\":")]
    public async Task ParseAsync_turns_bad_bodies_into_a_service_error(string text)
    {
        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => JsonRead.ParseAsync(Body(text), "Brave", CancellationToken.None));
        Assert.DoesNotContain("html", error.Message, StringComparison.Ordinal);
        Assert.Equal("Brave", error.Service);
    }

    [Fact]
    public async Task ParseAsync_refuses_a_body_over_the_cap()
    {
        var body = "{\"a\":\"" + new string('x', 200) + "\"}";
        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => JsonRead.ParseAsync(Body(body), "Brave", 100, CancellationToken.None));
        Assert.Contains("too large", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("xxxx", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ParseAsync_refuses_a_body_over_the_cap_when_the_length_is_unknown()
    {
        var body = "{\"a\":\"" + new string('x', 200) + "\"}";
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new UnknownLengthStream(Encoding.UTF8.GetBytes(body))) };
        await Assert.ThrowsAsync<ServiceFailedException>(() => JsonRead.ParseAsync(response, "Brave", 100, CancellationToken.None));
    }

    [Fact]
    public async Task ParseAsync_refuses_on_content_length_without_reading_the_body()
    {
        var content = new StreamContent(new ThrowingStream());
        content.Headers.ContentLength = 1000;
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => JsonRead.ParseAsync(response, "Brave", 100, CancellationToken.None));
        Assert.Contains("too large", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ParseAsync_accepts_a_body_exactly_at_the_cap()
    {
        var body = "{\"a\":\"x\"}";
        using var document = await JsonRead.ParseAsync(Body(body), "Brave", Encoding.UTF8.GetByteCount(body), CancellationToken.None);
        Assert.Equal("x", document.RootElement.Str("a"));
    }

    [Fact]
    public void The_default_cap_is_sixteen_megabytes() => Assert.Equal(16 * 1024 * 1024, JsonRead.MaxBodyBytes);

    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new InvalidOperationException("The body was touched.");

        public override long Position
        {
            get => throw new InvalidOperationException("The body was touched.");
            set => throw new InvalidOperationException("The body was touched.");
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("The body was read.");

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The body was read.");

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class UnknownLengthStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;

        public override long Length => throw new NotSupportedException();
    }
}
