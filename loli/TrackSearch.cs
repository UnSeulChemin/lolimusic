namespace Lolimusic;

public static class TrackSearch
{
 public static bool Contains(string text,string? query)=>System.Globalization.CultureInfo.InvariantCulture.CompareInfo.IndexOf(text,query?.Trim()??"",System.Globalization.CompareOptions.IgnoreCase|System.Globalization.CompareOptions.IgnoreNonSpace)>=0;
 public static bool Matches(Track track,string? query)=>Contains($"{track.Title} {track.Artist} {track.Album}",query);
}
