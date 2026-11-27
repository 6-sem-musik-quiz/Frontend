using System.Globalization;

namespace Frontend.Domain.Localization;

public enum Language
{
    Danish,
    English
}

// The language the UI is shown in (Danish or English), switched with the flags in the top bar.
// Static on purpose: Blazor WebAssembly runs one user per browser tab, and plain C# helpers
// (error texts, number formats, validation messages) need it without DI. Each text is written
// as a Danish/English pair at the place it is used: T("Forside", "Home").
public static class Lang
{
    public static Language Current { get; private set; } = Language.Danish;

    public static bool IsEnglish => Current == Language.English;

    // "da" or "en", e.g. for <html lang>.
    public static string Code => IsEnglish ? "en" : "da";

    // Raised after the language has changed, so the UI can re-render.
    public static event Action? Changed;

    public static string T(string danish, string english) => IsEnglish ? english : danish;

    public static void Set(Language language)
    {
        if (language == Current)
            return;
        Current = language;
        Changed?.Invoke();
    }

    public static Language FromCode(string? code) =>
        code?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true ? Language.English : Language.Danish;

    // A number with the decimal separator of the language: 4.2 -> "4,2" in Danish, "4.2" in English.
    // Built by hand: Blazor WebAssembly loads culture data by the browser's language, so "da-DK" is not
    // guaranteed in an English browser.
    public static string Number(double value, string format = "0.0")
    {
        var text = value.ToString(format, CultureInfo.InvariantCulture);
        return IsEnglish ? text : text.Replace('.', ',');
    }
}
