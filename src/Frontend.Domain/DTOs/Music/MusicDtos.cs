namespace Frontend.Domain.DTOs.Music;

// Mirrors MusicService (SCRUM-92, 93, 94, 123). Through the edge: /api/musicservice/...

// A playlist (category) with the number of songs in it.
public record PlaylistDto(int Id, string Name, int SongCount);

// Body of POST /playlists and PUT /playlists/{id}.
public record PlaylistNameRequest(string Name);

public record ArtistDto(int Id, string Name);

// Body of POST /artists.
public record ArtistNameRequest(string Name);

// AudioUrl is the public CDN address of the whole mp3. Artists are sorted by name.
public record SongDto(
    int Id,
    string Title,
    IReadOnlyList<string> Artists,
    int Year,
    int DurationSeconds,
    string AudioUrl);

// Body of PUT /songs/{id}. Artists are ids, the same as for upload.
public record UpdateSongRequest(string Title, IReadOnlyList<int> ArtistIds, int Year);

// The text fields of the upload form (the file is sent next to them as multipart/form-data).
public record UploadSongRequest(string Title, IReadOnlyList<int> ArtistIds, int Year);
