using Frontend.Domain.DTOs.Games;

namespace Frontend.Web.Components.Game;

// My result when the round ends (from RoundEndedDto), shown in the hand: a right answer turns the
// placed "?" into the song and it stays; a wrong one is removed; no answer leaves the "?" unplaced.
public record RoundReveal(CardDto Song, bool Answered, bool Correct);

// SCRUM-152: one opponent at the card table. Everything here is public: Cards and History cover
// ended rounds only, and the lock stamp says when, never where.
public record RivalView(
    int UserId,
    string Name,
    IReadOnlyList<CardDto> Cards,          // sorted by year; after RoundEnded including a right answer
    IReadOnlyList<bool> History,           // one per ended round: right or wrong (no answer = wrong)
    RivalStamp? Stamp,                     // set once they have answered the running round
    RivalReveal? Reveal);                  // set between RoundEnded and the next round

// Order is 1 for the first to answer. AfterMs is null after a reload (the server sends the order only).
public record RivalStamp(int Order, int? AfterMs);

// How the rival's answer went: a right one is already in Cards (shown as new);
// a wrong one is shown at Slot for a moment and falls out of the hand.
public record RivalReveal(CardDto Song, bool Correct, int? Slot);
