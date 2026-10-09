using System.Diagnostics;
namespace Lolimusic;
public static class DiscordCoverEncoder
{
 public static async Task<byte[]> Encode(byte[] source){
  var ffmpeg=Path.Combine(AppContext.BaseDirectory,"tools","ffmpeg.exe");
  var start=new ProcessStartInfo(ffmpeg){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
  foreach(var arg in new[]{"-v","error","-i","pipe:0","-vf","scale=384:384:force_original_aspect_ratio=decrease","-frames:v","1","-q:v","4","-f","image2pipe","-c:v","mjpeg","pipe:1"})start.ArgumentList.Add(arg);
  using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
  using var process=Process.Start(start)??throw new IOException("Préparation de la pochette impossible.");
  using var kill=timeout.Token.Register(()=>{try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}});
  var errors=process.StandardError.ReadToEndAsync();using var output=new MemoryStream();var read=process.StandardOutput.BaseStream.CopyToAsync(output,timeout.Token);
  try{await process.StandardInput.BaseStream.WriteAsync(source,timeout.Token);}finally{process.StandardInput.Close();}
  await process.WaitForExitAsync(timeout.Token);await read;var message=await errors;
  if(process.ExitCode!=0||output.Length==0)throw new IOException("Préparation de la pochette impossible : "+message);
  return output.ToArray();
 }
}
