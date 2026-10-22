using Gannet.Core.Configuration;
using Gannet.Core.Credibility;
using Gannet.Core.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Gannet.Core;

public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddGannetCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<GannetOptions>(configuration.GetSection(GannetOptions.Section));
        services.AddSingleton(TimeProvider.System);

        services.AddSingleton(sp =>
        {
            var file = sp.GetRequiredService<IOptions<GannetOptions>>().Value.CredibilityRulesFile;
            var rules = CredibilityRules.LoadDefault();
            if (!string.IsNullOrWhiteSpace(file))
            {
                rules = rules.MergeWith(CredibilityRules.Parse(File.ReadAllText(file)));
            }

            return new CredibilityScorer(rules);
        });

        services.AddSingleton<IBlobStore>(sp =>
        {
            var storage = sp.GetRequiredService<IOptions<GannetOptions>>().Value.Storage;
            var path = storage.Path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "gannet");
            return new LocalDiskBlobStore(path);
        });

        services.AddSingleton<IRunStore>(sp => new RunStore(sp.GetRequiredService<IBlobStore>(), sp.GetRequiredService<TimeProvider>()));
        return services;
    }
}
