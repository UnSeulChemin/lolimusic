namespace Lolimusic;

public static class LibraryImport
{
 public static IEnumerable<string> Files(IEnumerable<string> paths,ISet<string> extensions){
  var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  foreach(var input in paths){var candidates=Directory.Exists(input)?Directory.EnumerateFiles(input,"*",new EnumerationOptions{RecurseSubdirectories=true,IgnoreInaccessible=true}):File.Exists(input)?new[]{input}:Enumerable.Empty<string>();
   foreach(var candidate in candidates){var path=Path.GetFullPath(candidate);if(extensions.Contains(Path.GetExtension(path))&&!path.Contains(Path.DirectorySeparatorChar+".cache"+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&seen.Add(path))yield return path;}
  }
 }
}
