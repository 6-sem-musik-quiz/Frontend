using Frontend.Domain.DTOs.Games;

namespace Frontend.Web.Components.Game;

// What the game sidebar shows per player: only the number of cards and whether they have
// answered this round, never their cards (design guide: "Andre spillere: kun antal kort og ✓").
public record GamePlayerView(int UserId, string UserName, int Cards, bool Answered);

// My result when the round ends (from RoundEndedDto), shown in the hand: a right answer turns the
// placed "?" into the song and it stays; a wrong one is removed; no answer leaves the "?" unplaced.
public record RoundReveal(CardDto Song, bool Answered, bool Correct);
