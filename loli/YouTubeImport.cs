using System.Diagnostics;
using System.Text.Json;
namespace Lolimusic;
public static class YouTubeImport
{
 public static async Task<string> Download(string input,string musicRoot,CancellationToken cancel,bool netease=false){
  var service=netease?"NetEase":"YouTube";
  var url=(netease?NetEaseLinks.Normalize(input):YouTubeLinks.Normalize(input))??throw new ArgumentException("Colle un lien "+service+" valide.");
  var tools=Path.Combine(AppContext.BaseDirectory,"tools");var executable=Path.Combine(tools,"yt-dlp.exe");if(!File.Exists(executable))throw new IOException("Outil YouTube manquant : tools/yt-dlp.exe");
  var stage=Path.Combine(musicRoot,".cache",netease?"netease-imports":"youtube-imports",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
  var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};
  foreach(var arg in new[]{"--ignore-config","--no-playlist","--no-progress","--socket-timeout","20","--retries","2","--js-runtimes","deno:"+Path.Combine(tools,"deno.exe"),"--ffmpeg-location",tools,"-f",netease?"bestaudio":"bestaudio[ext=m4a]/bestaudio","--extract-audio","--audio-format",netease?"best":"m4a","--embed-metadata","--embed-thumbnail","--convert-thumbnails","jpg","--write-info-json","-o",Path.Combine(stage,"%(id)s.%(ext)s"),"--",url})start.ArgumentList.Add(arg);
  try{
   using var process=Process.Start(start)??throw new IOException("Impossible de lancer l’import YouTube.");var errors=process.StandardError.ReadToEndAsync();var output=process.StandardOutput.ReadToEndAsync();
   using var kill=cancel.Register(()=>{try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){} });await process.WaitForExitAsync(cancel);await output;var message=await errors;
   if(process.ExitCode!=0)throw new IOException(message.Length>1800?message[^1800..]:message);
   var audio=Directory.GetFiles(stage).Where(p=>new[]{".mp3",".flac",".m4a"}.Contains(Path.GetExtension(p))).SingleOrDefault()??throw new IOException("Aucun fichier audio récupéré.");
   using var info=JsonDocument.Parse(File.ReadAllText(Directory.GetFiles(stage,"*.info.json").Single()));var data=info.RootElement;
   string? Value(string name)=>data.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():null;
   if(netease&&data.TryGetProperty("is_preview",out var preview)&&preview.ValueKind==JsonValueKind.True)throw new IOException("NetEase propose seulement un extrait. Importe le fichier téléchargé avec NetEase."); var title=Value("track")??Value("title")??service;using(var file=TagLib.File.Create(audio)){if(file.Properties.Duration<=TimeSpan.Zero)throw new IOException("Audio "+service+" illisible.");if(netease&&data.TryGetProperty("duration",out var duration)&&duration.TryGetDouble(out var expected)&&expected>0&&file.Properties.Duration.TotalSeconds<expected-Math.Max(5,expected*0.05))throw new IOException("NetEase fournit un extrait incomplet. Importe le fichier .ncm téléchargé dans NetEase.");file.Tag.Title=title;var artist=Value("artist")??Value("uploader");if(artist!=null)file.Tag.Performers=[artist];else if(data.TryGetProperty("creators",out var creators)&&creators.ValueKind==JsonValueKind.Array)file.Tag.Performers=creators.EnumerateArray().Where(v=>v.ValueKind==JsonValueKind.String).Select(v=>v.GetString()!).ToArray();var album=Value("album");if(album!=null)file.Tag.Album=album;file.Save();}
   var safe=new string(title.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c).Take(110).ToArray()).TrimEnd('.',' ');if(safe.Length==0)safe="YouTube";
   var id=netease?url.Split("id=")[1]:new Uri(url).Query.Split('=')[1];var target=Path.Combine(musicRoot,safe+"_"+id+Path.GetExtension(audio));cancel.ThrowIfCancellationRequested();if(!File.Exists(target))File.Move(audio,target);return target;
  }finally{foreach(var file in Directory.EnumerateFiles(stage))try{File.Delete(file);}catch(IOException){}catch(UnauthorizedAccessException){}try{Directory.Delete(stage);}catch(IOException){}}
 }
}
