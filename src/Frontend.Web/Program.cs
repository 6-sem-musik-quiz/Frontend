using Frontend.Services.Extensions;
using Frontend.Web.Auth;
using Frontend.Web.Components;
using Frontend.Web.Music;

using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<Routes>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// All API calls go to edgeservice (YARP) under /api/<service>/ on the same host as the page.
// In the cluster there is no CORS: Cloudflare Tunnel sends /api/* to edgeservice and the rest
// to this app's nginx. ApiBaseUrl in wwwroot/appsettings*.json can point somewhere else.
var apiBase = new Uri(builder.Configuration["ApiBaseUrl"] is { Length: > 0 } configured
    ? configured
    : builder.HostEnvironment.BaseAddress);
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Services:UserService"] = new Uri(apiBase, "api/userservice/").ToString(),
    ["Services:MusicService"] = new Uri(apiBase, "api/musicservice/").ToString(),
    ["Services:StatisticService"] = new Uri(apiBase, "api/statisticservice/").ToString(),
    ["Services:LobbyService"] = new Uri(apiBase, "api/lobbyservice/").ToString(),
    ["Services:GameService"] = new Uri(apiBase, "api/gameservice/").ToString(),
});

builder.Services.AddFrontendAuth();
builder.Services.AddServiceLayer(builder.Configuration);
builder.Services.AddScoped<ISongUploader, SongUploader>();

await builder.Build().RunAsync();
