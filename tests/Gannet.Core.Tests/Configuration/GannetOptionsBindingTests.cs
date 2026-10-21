using Gannet.Core.Configuration;
using Microsoft.Extensions.Configuration;

namespace Gannet.Core.Tests.Configuration;

public class GannetOptionsBindingTests
{
    private static GannetOptions Bind(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();
        return configuration.GetSection(GannetOptions.Section).Get<GannetOptions>() ?? new GannetOptions();
    }

    [Fact]
    public void Unset_readers_fall_back_to_builtin()
    {
        var options = Bind(("Gannet:Defaults:Main", "local"));
        Assert.Equal(["builtin"], options.Defaults.EffectiveReaders);
    }

    [Fact]
    public void Configured_readers_replace_the_default_and_keep_their_order()
    {
        var options = Bind(("Gannet:Defaults:Readers:0", "jina"));
        Assert.Equal(["jina"], options.Defaults.EffectiveReaders);

        var two = Bind(("Gannet:Defaults:Readers:0", "jina"), ("Gannet:Defaults:Readers:1", "builtin"));
        Assert.Equal(["jina", "builtin"], two.Defaults.EffectiveReaders);
    }

    [Fact]
    public void Budget_init_setters_bind()
    {
        var options = Bind(("Gannet:Budget:MaxSearches", "9"), ("Gannet:Budget:MinCredibility", "55"));
        Assert.Equal(9, options.Budget.MaxSearches);
        Assert.Equal(55, options.Budget.MinCredibility);
        Assert.Equal(12, options.Budget.MaxPages);
    }

    [Fact]
    public void Model_concurrency_defaults_to_four_and_can_be_overridden()
    {
        Assert.Equal(4, Bind().Runs.ModelConcurrency);
        Assert.Equal(8, Bind(("Gannet:Runs:ModelConcurrency", "8")).Runs.ModelConcurrency);
    }

    [Fact]
    public void Model_keys_are_case_insensitive()
    {
        var options = Bind(("Gannet:Models:Local:Provider", "Ollama"), ("Gannet:Models:Local:Model", "qwen3"));
        Assert.True(options.Models.TryGetValue("local", out var model));
        Assert.Equal("qwen3", model.Model);
        Assert.True(options.Models.ContainsKey("LOCAL"));
    }

    [Fact]
    public void Readers_refuse_private_addresses_unless_allowed()
    {
        Assert.False(Bind().Readers.AllowPrivateAddresses);
        Assert.True(Bind(("Gannet:Readers:AllowPrivateAddresses", "true")).Readers.AllowPrivateAddresses);
    }
}
