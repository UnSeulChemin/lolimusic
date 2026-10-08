using System.Text.Json;

namespace Lolimusic;

public static class PlaylistArchive
{
 public sealed class Archive{public int Version{get;set;}=1;public List<Playlist> Playlists{get;set;}=[];}
 public static Task Write(Stream stream,IEnumerable<Playlist> playlists,string root){
  root=Path.GetFullPath(root)+Path.DirectorySeparatorChar;
  var document=new Archive{Playlists=playlists.Select(p=>new Playlist{Name=p.Name,Paths=p.Paths.Select(path=>Path.GetFullPath(path).StartsWith(root,StringComparison.OrdinalIgnoreCase)?Path.GetRelativePath(root,path):path).ToList()}).ToList()};
  return JsonSerializer.SerializeAsync(stream,document,new JsonSerializerOptions{WriteIndented=true});
 }
 public static async Task<List<Playlist>> Read(Stream stream,string root){
  if(stream.CanSeek&&stream.Length>10*1024*1024)throw new InvalidDataException("Sauvegarde trop volumineuse.");
  var document=await JsonSerializer.DeserializeAsync<Archive>(stream)??throw new InvalidDataException("Sauvegarde vide.");
  if(document.Version!=1||document.Playlists==null)throw new InvalidDataException("Format de sauvegarde incompatible.");
  var list=new List<Playlist>();var boundary=Path.GetFullPath(root)+Path.DirectorySeparatorChar;
  foreach(var playlist in document.Playlists){if(playlist==null||string.IsNullOrWhiteSpace(playlist.Name)||playlist.Paths==null)throw new InvalidDataException("Playlist invalide.");
   var paths=new List<string>();foreach(var path in playlist.Paths){if(string.IsNullOrWhiteSpace(path))throw new InvalidDataException("Chemin vide.");var full=Path.GetFullPath(Path.IsPathRooted(path)?path:Path.Combine(root,path));if(!Path.IsPathRooted(path)&&!full.StartsWith(boundary,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Chemin relatif en dehors de music.");paths.Add(full);}
   list.Add(new Playlist{Name=playlist.Name.Trim(),Paths=paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList()});
  }return list;
 }
}
