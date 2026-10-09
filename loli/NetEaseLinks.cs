namespace Lolimusic;
public static class NetEaseLinks
{
 public static string? Normalize(string? input){
  if(!Uri.TryCreate(input?.Trim(),UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host.ToLowerInvariant() is not ("music.163.com" or "y.music.163.com"))return null;
  if(uri.Fragment.StartsWith("#/"))uri=new Uri("https://music.163.com/"+uri.Fragment[2..]);
  if(uri.AbsolutePath.TrimEnd('/') is not ("/song" or "/m/song"))return null;
  foreach(var part in uri.Query.TrimStart('?').Split('&')){var pair=part.Split('=',2);if(pair.Length==2&&pair[0]=="id"&&ulong.TryParse(pair[1],out var id)&&id>0)return $"https://music.163.com/#/song?id={id}";}
  return null;
 }
}
