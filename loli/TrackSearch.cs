namespace Lolimusic;

public static class TrackSearch
{
 public static bool Matches(Track track,string? query)=>$"{track.Title} {track.Artist} {track.Album}".Contains(query?.Trim()??"",StringComparison.OrdinalIgnoreCase);
}
