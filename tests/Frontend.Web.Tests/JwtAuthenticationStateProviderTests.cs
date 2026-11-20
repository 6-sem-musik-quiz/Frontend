using System.Text;
using System.Text.Json;

using Frontend.Web.Auth;

using Microsoft.Extensions.Logging.Abstractions;

namespace Frontend.Web.Tests;

public class JwtAuthenticationStateProviderTests
{
    private readonly ManualTimeProvider _time = new(DateTimeOffset.Parse("2026-11-20T12:00:00Z"));
    private readonly InMemoryTokenStorage _storage = new();
    private readonly UserSession _session = new();

    private JwtAuthenticationStateProvider CreateSut() =>
        new(_session, _storage, _time, NullLogger<JwtAuthenticationStateProvider>.Instance);

    [Fact]
    public async Task ReadsUniqueNameAndRole_FromStoredToken()
    {
        var token = TestTokens.Create("bob", "Admin", _time.GetUtcNow().AddHours(1));
        _storage.Stored = new StoredSession(token, "bob", "Admin");

        var state = await CreateSut().GetAuthenticationStateAsync();

        Assert.True(state.User.Identity?.IsAuthenticated);
        Assert.Equal("bob", state.User.Identity?.Name);
        Assert.True(state.User.IsInRole("Admin"));
    }

    [Fact]
    public async Task ExpiredToken_LogsUserOutAndClearsStorage()
    {
        var token = TestTokens.Create("bob", "Player", _time.GetUtcNow().AddMinutes(-1));
        _storage.Stored = new StoredSession(token, "bob", "Player");

        var state = await CreateSut().GetAuthenticationStateAsync();

        Assert.False(state.User.Identity?.IsAuthenticated);
        Assert.Null(_storage.Stored);
        Assert.Null(_session.Current);
    }

    [Fact]
    public async Task SignIn_StoresTokenAndUser_AndNotifies()
    {
        var sut = CreateSut();
        var notified = false;
        sut.AuthenticationStateChanged += _ => notified = true;

        await sut.SignInAsync(TestTokens.Create("alice", "Player", _time.GetUtcNow().AddHours(1)));

        Assert.True(notified);
        Assert.Equal("alice", _storage.Stored?.UserName);
        Assert.Equal("Player", _storage.Stored?.Role);
        Assert.Equal("alice", (await sut.GetAuthenticationStateAsync()).User.Identity?.Name);
    }

    [Fact]
    public async Task TokenExpiringDuringSession_SignsOut()
    {
        var sut = CreateSut();
        await sut.SignInAsync(TestTokens.Create("alice", "Player", _time.GetUtcNow().AddMinutes(5)));
        var signedOut = new TaskCompletionSource();
        sut.AuthenticationStateChanged += async s =>
        {
            if ((await s).User.Identity?.IsAuthenticated != true) signedOut.TrySetResult();
        };

        _time.Advance(TimeSpan.FromMinutes(6));

        await signedOut.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(_session.Current);
        Assert.Null(_storage.Stored);
    }

    private sealed class InMemoryTokenStorage : ITokenStorage
    {
        public StoredSession? Stored { get; set; }
        public Task<StoredSession?> GetAsync() => Task.FromResult(Stored);
        public Task SetAsync(StoredSession session) { Stored = session; return Task.CompletedTask; }
        public Task DeleteAsync() { Stored = null; return Task.CompletedTask; }
    }
}

internal static class TestTokens
{
    public static string Create(string userName, string role, DateTimeOffset expiresAt)
    {
        static string Encode(string json) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["unique_name"] = userName,
            ["role"] = role,
            ["exp"] = expiresAt.ToUnixTimeSeconds(),
        });
        return $"{Encode("""{"alg":"none","typ":"JWT"}""")}.{Encode(payload)}.sig";
    }
}

// Minimal manuel TimeProvider: timere fyrer, når tiden skrues frem med Advance.
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(callback, state, _now + dueTime);
        lock (_timers) _timers.Add(timer);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        _now += by;
        ManualTimer[] due;
        lock (_timers) due = _timers.Where(t => !t.Disposed && t.DueAt <= _now).ToArray();
        foreach (var timer in due) timer.Fire();
    }

    private sealed class ManualTimer(TimerCallback callback, object? state, DateTimeOffset dueAt) : ITimer
    {
        public DateTimeOffset DueAt { get; } = dueAt;
        public bool Disposed { get; private set; }

        public void Fire()
        {
            Disposed = true;
            callback(state);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period) => false;
        public void Dispose() => Disposed = true;
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
