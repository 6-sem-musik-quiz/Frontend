using Frontend.Services.Extensions;
using Frontend.Web.Auth;
using Frontend.Web.Components;
using Frontend.Web.Games;
using Frontend.Web.Lobbies;
using Frontend.Web.Music;
using Frontend.Web.Sound;

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
    ["Services:EdgeService"] = new Uri(apiBase, "api/edge/").ToString(), // SCRUM-175: maintenance flags
});

builder.Services.AddFrontendAuth();
builder.Services.AddServiceLayer(builder.Configuration);
builder.Services.AddScoped<ISongUploader, SongUploader>();
// One hub connection per lobby page; the page disposes it.
builder.Services.AddTransient<ILobbyHub, LobbyHub>();
builder.Services.AddTransient<IGameHubClient, GameHubClient>();
builder.Services.AddScoped<IGameAudio, GameAudio>();
builder.Services.AddScoped<ISoundEffects, SoundEffects>(); // sound effects, shared by all pages (SCRUM-161)
builder.Services.AddScoped<IBackgroundMusic, BackgroundMusic>(); // quiet menu music on the front page and in the lobby (SCRUM-167)
builder.Services.AddSingleton(TimeProvider.System); // the game page's timers; tests use a fake
// SCRUM-174/176: which services an admin has switched off, and the toasts that say so.
builder.Services.AddSingleton<Frontend.Web.Maintenance.MaintenanceState>();
builder.Services.AddSingleton<Frontend.Web.Maintenance.ToastService>();

await builder.Build().RunAsync();
