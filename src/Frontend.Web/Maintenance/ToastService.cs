namespace Frontend.Web.Maintenance;

// SCRUM-176: short messages in the corner (ToastHost in MainLayout), e.g. "Lobbyer er under
// vedligeholdelse." when a switched-off feature is clicked. Each toast disappears after ShowFor;
// the same text is not shown twice at once.
public sealed class ToastService(TimeProvider time)
{
    public static readonly TimeSpan ShowFor = TimeSpan.FromSeconds(5);

    public sealed record Toast(int Id, string Text);

    private readonly List<Toast> _toasts = [];
    private int _nextId;

    public event Action? Changed;

    public IReadOnlyList<Toast> Toasts => _toasts;

    public void Show(string text)
    {
        if (_toasts.Any(t => t.Text == text))
            return;
        var toast = new Toast(++_nextId, text);
        _toasts.Add(toast);
        Changed?.Invoke();

        ITimer? timer = null;
        timer = time.CreateTimer(_ =>
        {
            Dismiss(toast.Id);
            timer?.Dispose();
        }, null, ShowFor, Timeout.InfiniteTimeSpan);
    }

    public void Dismiss(int id)
    {
        if (_toasts.RemoveAll(t => t.Id == id) > 0)
            Changed?.Invoke();
    }
}
