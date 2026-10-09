namespace Lolimusic;

public sealed class LibraryIndex
{
 public record Entry(long Size,DateTime Modified,string Identity,Track Track);
 public record SavedEntry(string Path,long Size,DateTime Modified,string Identity,string Title,string Artist,string Album,string? CoverHash);
 readonly string? directory;
 readonly Dictionary<string,Entry> entries=new(StringComparer.OrdinalIgnoreCase);
 public LibraryIndex(string? cacheDirectory=null){directory=cacheDirectory;if(directory==null)return;try{
  var saved=System.Text.Json.JsonSerializer.Deserialize<List<SavedEntry>>(File.ReadAllText(Path.Combine(directory,"index.json")));
  foreach(var item in saved??[]){if(item==null||string.IsNullOrWhiteSpace(item.Path)||string.IsNullOrWhiteSpace(item.Identity)||item.Title==null||item.Artist==null||item.Album==null)continue;if(item.CoverHash!=null&&!System.Text.RegularExpressions.Regex.IsMatch(item.CoverHash,"^[A-F0-9]{64}$"))continue;
   var coverPath=item.CoverHash==null?null:Path.Combine(directory,item.CoverHash+".cover");if(coverPath!=null&&!File.Exists(coverPath))continue;
   entries[item.Path]=new(item.Size,item.Modified,item.Identity,new(item.Path,item.Title,item.Artist,item.Album,null){CoverPath=coverPath,CoverHash=item.CoverHash});
  }
 }catch(IOException){}catch(UnauthorizedAccessException){}catch(System.Text.Json.JsonException){} }
 public record Result(List<Track> Tracks,Dictionary<string,string> Ids,int Unreadable,int Parsed);
 public Result Scan(IEnumerable<string> paths,bool force=false){
  var found=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var tracks=new List<Track>();var ids=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);int unreadable=0,parsed=0;
  foreach(var path in paths){if(!found.Add(path))continue;try{
   var info=new FileInfo(path);var id=LibraryReferences.Identity(path);ids[path]=id;
   if(!force&&entries.TryGetValue(path,out var old)&&old.Size==info.Length&&old.Modified==info.LastWriteTimeUtc&&old.Identity==id&&(old.Track.CoverPath==null||File.Exists(old.Track.CoverPath))){tracks.Add(old.Track);continue;}
   using var file=TagLib.File.Create(path);var tag=file.Tag;
   var bytes=tag.Pictures.FirstOrDefault()?.Data.Data;string? coverPath=null,hash=null;
   if(directory!=null&&bytes is {Length:>0})try{Directory.CreateDirectory(directory);hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));coverPath=Path.Combine(directory,hash+".cover");if(!File.Exists(coverPath))File.WriteAllBytes(coverPath,bytes);bytes=null;}catch(IOException){coverPath=null;hash=null;}catch(UnauthorizedAccessException){coverPath=null;hash=null;}
   var track=new Track(path,string.IsNullOrWhiteSpace(tag.Title)?Path.GetFileNameWithoutExtension(path):tag.Title,string.IsNullOrWhiteSpace(tag.FirstPerformer)?"Artiste inconnu":tag.FirstPerformer,string.IsNullOrWhiteSpace(tag.Album)?"Sans album":tag.Album,bytes){CoverPath=coverPath,CoverHash=hash};
   entries[path]=new(info.Length,info.LastWriteTimeUtc,id,track);tracks.Add(track);parsed++;
  }catch{unreadable++;entries.Remove(path);}}
  var stalePaths=entries.Keys.Where(p=>!found.Contains(p)).ToList();foreach(var stale in stalePaths)entries.Remove(stale);
  if(directory!=null&&(parsed>0||stalePaths.Count>0||unreadable>0))try{Directory.CreateDirectory(directory);
   var saved=entries.Values.Select(e=>new SavedEntry(e.Track.Path,e.Size,e.Modified,e.Identity,e.Track.Title,e.Track.Artist,e.Track.Album,e.Track.CoverHash)).ToList();
   // Entries whose artwork could not be cached must be reparsed next time.
   saved.RemoveAll(e=>entries[e.Path].Track.CoverPath==null&&entries[e.Path].Track.Cover!=null);
   var target=Path.Combine(directory,"index.json");File.WriteAllText(target+".tmp",System.Text.Json.JsonSerializer.Serialize(saved));File.Move(target+".tmp",target,true);
  }catch(IOException){}catch(UnauthorizedAccessException){}
  return new(tracks.OrderBy(t=>t.Album).ThenBy(t=>t.Title).ToList(),ids,unreadable,parsed);
 }
}
