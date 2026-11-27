using System.Globalization;

using Frontend.Domain.Localization;

namespace Frontend.Web.Components.Statistics;

// SCRUM-116: how the front page shows numbers from StatisticService, in Danish or English.
// The text is built by hand: Blazor WebAssembly loads ICU culture data by the browser's
// language, so "da-DK" month names and separators are not guaranteed in an English browser.
public static class StatsFormat
{
    private static readonly string[] DanishMonths =
        ["januar", "februar", "marts", "april", "maj", "juni", "juli", "august", "september", "oktober", "november", "december"];

    private static readonly string[] EnglishMonths =
        ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

    // A fraction 0-1 as a percentage with one decimal: 0.755 -> "75,5 %" (Danish) or "75.5%" (English).
    public static string Percent(double fraction)
    {
        var number = Lang.Number(Math.Round(fraction * 100, 1, MidpointRounding.AwayFromZero));
        return Lang.IsEnglish ? number + "%" : number + " %";
    }

    // Seconds as hours and minutes: 12300 -> "3 t 25 min" / "3 h 25 min", 600 -> "10 min", 0 -> "0 min".
    public static string PlayTime(long seconds)
    {
        var minutes = Math.Max(0, seconds) / 60;
        var hours = minutes / 60;
        minutes %= 60;
        return hours > 0 ? $"{hours} {Lang.T("t", "h")} {minutes} min" : $"{minutes} min";
    }

    // "Uge 48 · 23.–29. november", or across a month "Uge 49 · 30. november – 6. december".
    // English: "Week 48 · 23–29 November" / "Week 49 · 30 November – 6 December".
    // The dates are the ones in the offset the service sent (Danish time).
    public static string WeekTitle(DateTimeOffset start, DateTimeOffset end)
    {
        var week = ISOWeek.GetWeekOfYear(start.DateTime);
        var range = start.Month == end.Month
            ? $"{start.Day}{Lang.T(".", "")}–{Date(end)}"
            : $"{Date(start)} – {Date(end)}";
        return $"{Lang.T("Uge", "Week")} {week} · {range}";
    }

    private static string Date(DateTimeOffset d) =>
        Lang.IsEnglish ? $"{d.Day} {EnglishMonths[d.Month - 1]}" : $"{d.Day}. {DanishMonths[d.Month - 1]}";
}
