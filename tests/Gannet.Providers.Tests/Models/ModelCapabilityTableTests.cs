using Gannet.Core.Models;
using Gannet.Providers.Models;

namespace Gannet.Providers.Tests.Models;

public class ModelCapabilityTableTests
{
    // Context windows from each vendor's model pages, read on 22 Oct 2026 (see the Task 19 report for the URLs).
    [Theory]
    [InlineData("claude-fable-5-1", 1_000_000)]
    [InlineData("claude-opus-5-5", 1_000_000)]
    [InlineData("claude-sonnet-5-5", 1_000_000)]
    [InlineData("claude-haiku-5-5", 1_000_000)]
    [InlineData("claude-opus-4-8", 1_000_000)]
    [InlineData("claude-opus-4-7", 1_000_000)]
    [InlineData("claude-opus-4-6", 1_000_000)]
    [InlineData("claude-sonnet-4-6", 1_000_000)]
    [InlineData("claude-opus-4-5", 200_000)]
    [InlineData("claude-haiku-4-5", 200_000)]
    [InlineData("claude-some-future-thing", 200_000)]
    [InlineData("gpt-6-astra", 1_050_000)]
    [InlineData("gpt-6.1-sol", 1_050_000)]
    [InlineData("gpt-5.5", 1_050_000)]
    [InlineData("gpt-5.4", 1_050_000)]
    [InlineData("gpt-5.4-mini", 400_000)]
    [InlineData("gpt-5.2", 400_000)]
    [InlineData("gpt-5-mini", 400_000)]
    [InlineData("gpt-4.1", 1_047_576)]
    [InlineData("gpt-4.1-mini", 1_047_576)]
    [InlineData("gpt-5.4-nano", 400_000)]
    [InlineData("gpt-5-chat-latest", 128_000)]
    [InlineData("gpt-4-turbo", 128_000)]
    [InlineData("gpt-4o", 128_000)]
    [InlineData("gpt-4o-mini", 128_000)]
    [InlineData("gpt-oss-120b", 131_072)]
    [InlineData("o1", 200_000)]
    [InlineData("o3", 200_000)]
    [InlineData("o4-mini", 200_000)]
    [InlineData("gemini-2.5-pro", 1_048_576)]
    [InlineData("gemini-3.6-flash", 1_048_576)]
    [InlineData("gemini-4-argon", 1_048_576)]
    [InlineData("deepseek-v4-pro", 1_000_000)]
    [InlineData("deepseek-flash", 1_000_000)]
    [InlineData("deepseek-v4-flash", 1_000_000)]
    [InlineData("deepseek-v4-flash-vision-exp", 1_000_000)]
    [InlineData("GEMINI-3.1-PRO-PREVIEW", 1_048_576)]
    public void Known_families_have_their_documented_window(string model, int window) =>
        Assert.Equal(new ModelCapabilities(window, true), ModelCapabilityTable.Lookup(model));

    [Theory]
    [InlineData("qwen3:4b")]
    [InlineData("something-local")]
    [InlineData("deepseek-chat")]
    [InlineData("deepseek-flash-2")]
    [InlineData("deepseek/deepseek-v4-pro")]
    [InlineData("")]
    public void Unknown_models_are_not_in_the_table(string model) => Assert.Null(ModelCapabilityTable.Lookup(model));

    [Fact]
    public void Gpt_4_matches_only_exactly_and_the_page_lists_no_function_calling()
    {
        Assert.Equal(new ModelCapabilities(8_192, false), ModelCapabilityTable.Lookup("gpt-4"));
        Assert.Equal(new ModelCapabilities(8_192, false), ModelCapabilityTable.Lookup("GPT-4"));
        Assert.Equal(128_000, ModelCapabilityTable.Lookup("gpt-4-0613")!.ContextWindow);
    }

    [Theory]
    [InlineData("claude-fable-5-1", false)]
    [InlineData("claude-opus-5-5", false)]
    [InlineData("claude-sonnet-5-5", false)]
    [InlineData("claude-haiku-5-5", false)]
    [InlineData("claude-opus-4-8", false)]
    [InlineData("claude-opus-4-7", false)]
    [InlineData("claude-opus-4-6", false)]
    [InlineData("claude-sonnet-4-6", false)]
    [InlineData("claude-opus-4-5", true)]
    [InlineData("claude-haiku-4-5", true)]
    [InlineData("claude-sonnet-4-5", true)]
    [InlineData("claude-3-7-sonnet", true)]
    [InlineData("gpt-5.5", false)]
    [InlineData("gemini-3.6-flash", false)]
    [InlineData("something-local", false)]
    public void Claude_families_that_predate_adaptive_thinking_are_marked_extended(string model, bool extended) =>
        Assert.Equal(extended, ModelCapabilityTable.UsesExtendedThinking(model));
}
