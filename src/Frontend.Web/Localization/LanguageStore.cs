using Frontend.Domain.Localization;

using Microsoft.JSInterop;

namespace Frontend.Web.Localization;

// Remembers the chosen language in localStorage and keeps <html lang> in step (screen readers, and the
// loading screen's text before Blazor starts, see index.html). Danish until the player picks English.
public sealed class LanguageStore(IJSRuntime js)
{
    public const string Key = "slopster.lang";

    // Called once in Program.cs, before the first render.
    public async Task LoadAsync()
    {
        try
        {
            Lang.Set(Lang.FromCode(await js.InvokeAsync<string?>("localStorage.getItem", Key)));
        }
        catch (JSException)
        {
            // Storage blocked: Danish for this visit.
        }
    }

    public async Task SetAsync(Language language)
    {
        Lang.Set(language);
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", Key, Lang.Code);
            await js.InvokeVoidAsync("document.documentElement.setAttribute", "lang", Lang.Code);
        }
        catch (JSException)
        {
            // Storage blocked: the language still applies for this visit.
        }
    }
}
