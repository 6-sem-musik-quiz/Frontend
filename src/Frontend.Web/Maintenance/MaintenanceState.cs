using Frontend.Domain.DTOs.Maintenance;
using Frontend.Domain.Localization;
using Frontend.Services.Interfaces;

namespace Frontend.Web.Maintenance;

// SCRUM-174/176 ("design to be disabled"): which backend services an admin has switched off.
// Polls EdgeService every PollEvery (started by MainLayout), and learns at once from a maintenance 503
// (ApiCallHandler.MarkOff), so a switched-off feature greys out without waiting for the next poll.
// Components listen to Changed and re-render.
public sealed class MaintenanceState(IMaintenanceApiClient api, TimeProvider time, ILogger<MaintenanceState> logger) : IDisposable
{
    public static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(5);

    // What each service turns off, in the words players see (toast, banner, notices), in the current language.
    public static IReadOnlyDictionary<string, (string Feature, string Text)> Features =>
        Lang.IsEnglish ? EnglishFeatures : DanishFeatures;

    private static readonly IReadOnlyDictionary<string, (string Feature, string Text)> DanishFeatures =
        new Dictionary<string, (string, string)>
        {
            ["userservice"] = ("Login og oprettelse", "Login og oprettelse er under vedligeholdelse."),
            ["lobbyservice"] = ("Lobbyer", "Lobbyer er under vedligeholdelse."),
            ["gameservice"] = ("Nye spil", "Spillet er under vedligeholdelse. Du kan blive i lobbyen imens."),
            ["musicservice"] = ("Musikkataloget", "Musikkataloget er under vedligeholdelse."),
            ["statisticservice"] = ("Leaderboard og statistik", "Leaderboardet er under vedligeholdelse. Prøv igen senere."),
        };

    private static readonly IReadOnlyDictionary<string, (string Feature, string Text)> EnglishFeatures =
        new Dictionary<string, (string, string)>
        {
            ["userservice"] = ("Login and sign-up", "Login and sign-up are under maintenance."),
            ["lobbyservice"] = ("Lobbies", "Lobbies are under maintenance."),
            ["gameservice"] = ("New games", "The game is under maintenance. You can stay in the lobby meanwhile."),
            ["musicservice"] = ("The music catalogue", "The music catalogue is under maintenance."),
            ["statisticservice"] = ("Leaderboard and statistics", "The leaderboard is under maintenance. Try again later."),
        };

    public static string FallbackText =>
        Lang.T("Funktionen er under vedligeholdelse. Prøv igen senere.", "This feature is under maintenance. Try again later.");

    private Dictionary<string, MaintenanceStatusDto> _off = [];
    private ITimer? _timer;

    public event Action? Changed;

    // The switched-off services, in the order of Features.
    public IReadOnlyList<MaintenanceStatusDto> Off =>
        Features.Keys.Where(_off.ContainsKey).Select(k => _off[k]).ToList();

    public bool IsOff(string service) => _off.ContainsKey(service);

    // "Lobbyer er under vedligeholdelse." plus the admin's message, if any.
    public string TextFor(string service)
    {
        var text = Features.TryGetValue(service, out var f) ? f.Text : FallbackText;
        return _off.TryGetValue(service, out var status) && !string.IsNullOrWhiteSpace(status.Message)
            ? $"{text} {status.Message}"
            : text;
    }

    public void Start() =>
        _timer ??= time.CreateTimer(_ => _ = RefreshAsync(), null, TimeSpan.Zero, PollEvery);

    public async Task RefreshAsync()
    {
        try
        {
            Set((await api.GetAllAsync()).Where(s => s.Off));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // The edge could not be asked: keep what we knew. A switched-off service still answers 503.
            logger.LogDebug(e, "Could not read the maintenance flags.");
        }
    }

    // A service answered with a maintenance 503 before the next poll.
    public void MarkOff(string service, string? message)
    {
        if (string.IsNullOrEmpty(service) || _off.ContainsKey(service))
            return;
        var name = Features.TryGetValue(service, out var f) ? f.Feature : service;
        _off = new Dictionary<string, MaintenanceStatusDto>(_off)
        {
            [service] = new(service, name, true, message, null, null)
        };
        Changed?.Invoke();
    }

    private void Set(IEnumerable<MaintenanceStatusDto> off)
    {
        var next = off.ToDictionary(s => s.Service);
        var same = next.Count == _off.Count
                   && next.All(n => _off.TryGetValue(n.Key, out var o) && o.Message == n.Value.Message);
        if (same)
            return;
        _off = next;
        Changed?.Invoke();
    }

    public void Dispose() => _timer?.Dispose();
}
