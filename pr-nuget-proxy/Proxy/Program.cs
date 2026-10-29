using PrNuGetProxy;

var builder = WebApplication.CreateBuilder(args);
if (builder.Configuration["config"] is { } config)
{
    builder.Configuration.AddJsonFile(Path.GetFullPath(config), optional: false, reloadOnChange: false)
        .AddEnvironmentVariables()
        .AddCommandLine(args);
}
builder.Services.AddProxy(builder.Configuration);
var app = builder.Build();
var options = app.Services.GetRequiredService<ProxyOptions>();
Directory.CreateDirectory(options.CacheDirectory);
// Keep two proxy instances from publishing into the same cache.
using var cacheLock = new FileStream(Path.Combine(options.CacheDirectory, ".lock"), FileMode.OpenOrCreate,
    FileAccess.ReadWrite, FileShare.None);
app.UseExceptionHandler();
app.MapProxy();
await app.RunAsync();
