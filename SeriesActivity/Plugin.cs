using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.SeriesActivity;

public sealed class Configuration : BasePluginConfiguration { }

public sealed class Plugin : BasePlugin<Configuration>, IHasWebPages
{
    public Plugin(IApplicationPaths paths, IXmlSerializer serializer) : base(paths, serializer) { }
    public override string Name => "Series Activity";
    public override string Description => "See which TV series people watch and which have gone quiet.";
    public override Guid Id => new("a40f58ee-e271-4778-b187-662705f9ef26");
    public IEnumerable<PluginPageInfo> GetPages() => [new()
    {
        Name = "seriesactivity",
        DisplayName = Name,
        EnableInMainMenu = true,
        MenuIcon = "tv",
        EmbeddedResourcePath = "Jellyfin.Plugin.SeriesActivity.Web.activity.html"
    }];
}

public sealed class Services : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost host)
    {
        services.AddSingleton<ActivityService>();
        services.AddSingleton<IHostedService>(s => s.GetRequiredService<ActivityService>());
    }
}
