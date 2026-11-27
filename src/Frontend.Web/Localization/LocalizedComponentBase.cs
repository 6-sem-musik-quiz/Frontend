using Frontend.Domain.Localization;

using Microsoft.AspNetCore.Components;

namespace Frontend.Web.Localization;

// The base of every component under Components/ (set in Components/_Imports.razor). T("da", "en") picks
// the text for the current language, and a language switch re-renders every live component in place,
// so nothing is reloaded and a running game or lobby keeps its state.
public abstract class LocalizedComponentBase : ComponentBase
{
    private bool _registered;

    protected static string T(string danish, string english) => Lang.T(danish, english);

    protected static bool IsEnglish => Lang.IsEnglish;

    public override Task SetParametersAsync(ParameterView parameters)
    {
        if (!_registered)
        {
            _registered = true;
            LanguageRefresh.Register(this);
        }
        return base.SetParametersAsync(parameters);
    }

    internal void RefreshForLanguage() => _ = InvokeAsync(StateHasChanged);
}

// Keeps weak references to the live components, so a language switch can re-render them without each
// one having to unsubscribe. A disposed component is ignored by the renderer and collected by the GC.
internal static class LanguageRefresh
{
    private static readonly List<WeakReference<LocalizedComponentBase>> Components = [];

    static LanguageRefresh() => Lang.Changed += RefreshAll;

    public static void Register(LocalizedComponentBase component)
    {
        lock (Components)
        {
            if (Components.Count % 256 == 255)
                Components.RemoveAll(r => !r.TryGetTarget(out _));
            Components.Add(new WeakReference<LocalizedComponentBase>(component));
        }
    }

    private static void RefreshAll()
    {
        List<LocalizedComponentBase> live;
        lock (Components)
        {
            Components.RemoveAll(r => !r.TryGetTarget(out _));
            live = Components.Select(r => r.TryGetTarget(out var c) ? c : null).OfType<LocalizedComponentBase>().ToList();
        }
        foreach (var component in live)
            component.RefreshForLanguage();
    }
}
