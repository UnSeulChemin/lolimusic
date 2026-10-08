using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Lolimusic;

// The Windows file identifier survives renaming, folder moves and tag edits.
public static class LibraryReferences
{
 [StructLayout(LayoutKind.Sequential)]
 struct FileInformation {
  public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
  public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
 }
 [DllImport("kernel32.dll", SetLastError=true)]
 static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
 public static string Identity(string path) {
  using var handle=File.OpenHandle(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
  if(!GetFileInformationByHandle(handle,out var info))throw new IOException("Impossible d'identifier le fichier.");
  return $"{info.Volume:X8}:{info.IndexHigh:X8}{info.IndexLow:X8}";
 }
 public static Dictionary<string,string> Reconcile(Settings settings, Dictionary<string,string> found) {
  var mappings=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
  var present=new HashSet<string>(found.Keys,StringComparer.OrdinalIgnoreCase);
  var identities=found.GroupBy(p=>p.Value).ToDictionary(g=>g.Key,g=>g.Select(p=>p.Key).ToList());
  var references=settings.Playlists.SelectMany(p=>p.Paths).Concat(settings.Favorites).Concat(settings.DiscordCovers.Keys).Concat(settings.FileIdentities.Keys).Concat(settings.LastQueue).Concat(settings.LastTrack==null?[]:new[]{settings.LastTrack}).Distinct(StringComparer.OrdinalIgnoreCase);
  foreach(var old in references.Where(p=>!present.Contains(p))) {
   if(settings.FileIdentities.TryGetValue(old,out var id)&&identities.TryGetValue(id,out var candidates)&&candidates.Count==1)mappings[old]=candidates[0];
   else if(!settings.FileIdentities.ContainsKey(old)) {
    var names=found.Keys.Where(p=>System.IO.Path.GetFileName(p).Equals(System.IO.Path.GetFileName(old),StringComparison.OrdinalIgnoreCase)).ToList();
    if(names.Count==1)mappings[old]=names[0];
   }
  }
  Apply(settings,mappings);
  // Keep identities of absent files: reconnecting a drive must not erase playlists.
  foreach(var entry in found)settings.FileIdentities[entry.Key]=entry.Value;
  return mappings;
 }
 public static void Apply(Settings settings, IReadOnlyDictionary<string,string> mappings) {
  string Map(string path)=>mappings.TryGetValue(path,out var target)?target:path;
  foreach(var playlist in settings.Playlists)playlist.Paths=playlist.Paths.Select(Map).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
  settings.Favorites=settings.Favorites.Select(Map).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
  settings.LastQueue=settings.LastQueue.Select(Map).Distinct(StringComparer.OrdinalIgnoreCase).ToList();if(settings.LastTrack!=null)settings.LastTrack=Map(settings.LastTrack);
  foreach(var old in mappings.Keys.ToList()) {
   if(settings.DiscordCovers.Remove(old,out var url))settings.DiscordCovers[Map(old)]=url;
   if(settings.FileIdentities.Remove(old,out var id))settings.FileIdentities[Map(old)]=id;
  }
 }
}
