namespace Lolimusic;
public static class YouTubeLinks
{
 public static string? Normalize(string? input){
  if(!Uri.TryCreate(input?.Trim(),UriKind.Absolute,out var uri)||uri.Scheme!="https")return null;
  string? id=null;var host=uri.Host.ToLowerInvariant();
  if(host=="i.ytimg.com"){var parts=uri.AbsolutePath.Trim('/').Split('/');if(parts.Length==3&&parts[0] is "vi" or "vi_webp")id=parts[1];}
  else if(host is "youtu.be" or "youtube.com" or "www.youtube.com" or "m.youtube.com" or "music.youtube.com")try{var thumbnail=new Uri(MusicWindow.CoverUrl(uri.AbsoluteUri));id=thumbnail.AbsolutePath.Split('/')[2];}catch(ArgumentException){return null;}
  return id!=null&&System.Text.RegularExpressions.Regex.IsMatch(id,"^[A-Za-z0-9_-]{11}$")?$"https://www.youtube.com/watch?v={id}":null;
 }
 public static void Set(Settings settings,string path,string? link,bool netease){var chosen=netease?settings.NetEaseLinks:settings.YouTubeLinks;var other=netease?settings.YouTubeLinks:settings.NetEaseLinks;if(link==null)chosen.Remove(path);else{chosen[path]=link;other.Remove(path);}}
 public static DiscordRPC.Button[]? Buttons(Settings settings,Track song){var netease=NetEaseLinks.Normalize(settings.NetEaseLinks.GetValueOrDefault(song.Path));if(netease!=null)return [new(){Label="Écouter sur NetEase",Url=netease}];var youtube=Normalize(settings.YouTubeLinks.GetValueOrDefault(song.Path));return youtube==null?null:[new(){Label="Écouter sur YouTube",Url=youtube}];}
}
