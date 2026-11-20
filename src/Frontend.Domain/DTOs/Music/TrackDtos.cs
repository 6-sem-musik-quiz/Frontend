namespace Frontend.Domain.DTOs.Music;

// FORELØBIG: spejler MusicService-opgaven (SCRUM-35 foreslår GET /api/tracks/random).
// Der ligger endnu ingen godkendt MusicService.openapi.yaml i Platform/docs/contracts.
public record TrackDto(Guid Id, string Title, string Artist, int? ReleaseYear, string? PreviewUrl);

public record CreateTrackRequest(string Title, string Artist, int? ReleaseYear, string? PreviewUrl);
