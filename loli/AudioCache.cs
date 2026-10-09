using System.Text.RegularExpressions;

namespace Lolimusic;

public static class AudioCache
{
 static readonly object gate=new();
 public const long Limit=2L*1024*1024*1024;
 public static void Touch(string path){try{File.SetLastAccessTimeUtc(path,DateTime.UtcNow);}catch(IOException){}catch(UnauthorizedAccessException){}}
 public static void Trim(string directory,string? keep=null,long limit=Limit,DateTime? now=null){
  lock(gate)try{TrimCore(directory,keep,limit,now);}catch(IOException){}catch(UnauthorizedAccessException){}
 }
 static void TrimCore(string directory,string? keep,long limit,DateTime? now){
  if(!Directory.Exists(directory))return;var time=now??DateTime.UtcNow;
  var files=Directory.EnumerateFiles(directory,"*.wav",SearchOption.TopDirectoryOnly).Where(p=>Regex.IsMatch(Path.GetFileName(p),"^[A-Fa-f0-9]{64}\\.wav$")).Select(p=>new FileInfo(p)).ToList();long total=files.Sum(f=>f.Length);
  foreach(var file in files.OrderBy(f=>f.LastAccessTimeUtc)){
   if(file.FullName.Equals(keep,StringComparison.OrdinalIgnoreCase)||time-file.LastWriteTimeUtc<TimeSpan.FromMinutes(1))continue;
   if(total<=limit&&time-file.LastAccessTimeUtc<TimeSpan.FromDays(30))continue;
   try{var bytes=file.Length;file.Delete();total-=bytes;}catch(IOException){}catch(UnauthorizedAccessException){}
  }
 }
}
