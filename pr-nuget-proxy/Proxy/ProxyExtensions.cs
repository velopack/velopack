using Microsoft.Extensions.Options;

namespace PrNuGetProxy;

public static class ProxyExtensions
{
    public static IServiceCollection AddProxy(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ProxyOptions>()
            .Bind(configuration.GetSection("Proxy"))
            .Validate(options => options.IsValid(), "Invalid proxy configuration.")
            .ValidateOnStart();
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<ProxyOptions>>().Value);
        services.AddMemoryCache();
        services.AddSingleton<IGitHubClient, GitHubClient>();
        services.AddSingleton<ArtifactCache>();
        services.AddSingleton<PackageCatalog>();
        services.AddSingleton<PackageCache>();
        services.AddProblemDetails();
        return services;
    }

    public static void MapProxy(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<ProxyOptions>();
        app.MapGet("/health", () => Results.Ok(new { status = "ok", repository = options.Repository }));
        app.MapGet("/builds", async (PackageCatalog catalog, CancellationToken ct) => Results.Ok(await catalog.GetBuildsAsync(ct)));
        app.MapMethods("/v3/index.json", ["GET", "HEAD"], () => Results.Json(new
        {
            version = "3.0.0",
            resources = new object[] {
                FeedEndpoints.Resource(options.BaseUrl + "/flat/", "PackageBaseAddress/3.0.0"),
                FeedEndpoints.Resource(options.BaseUrl + "/registration/", "RegistrationsBaseUrl/3.6.0"),
                FeedEndpoints.Resource(options.BaseUrl + "/query", "SearchQueryService/3.0.0-beta"),
                FeedEndpoints.Resource(options.BaseUrl + "/query", "SearchQueryService/3.5.0"),
                FeedEndpoints.Resource(options.BaseUrl + "/autocomplete", "SearchAutocompleteService/3.0.0-beta"),
                FeedEndpoints.Resource(options.BaseUrl + "/autocomplete", "SearchAutocompleteService/3.5.0")
            }
        }));
        FeedEndpoints.Map(app);
    }
}
