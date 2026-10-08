using System.IO.Pipes;
using System.Text;

namespace Lolimusic;

internal static class InstanceBridge
{
 internal static string Name=>"lolimusic-"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName)))[..16];
 internal static void Send(string command){try{using var pipe=new NamedPipeClientStream(".",Name,PipeDirection.Out,PipeOptions.CurrentUserOnly);pipe.Connect(2000);using var writer=new StreamWriter(pipe){AutoFlush=true};writer.WriteLine(command);}catch{}}
 internal static async Task Listen(Action<string> command,CancellationToken stop){
  while(!stop.IsCancellationRequested)try{
   await using var pipe=new NamedPipeServerStream(Name,PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
   await pipe.WaitForConnectionAsync(stop);using var reader=new StreamReader(pipe);var text=await reader.ReadLineAsync(stop);if(text is "open" or "quit")command(text);
  }catch(OperationCanceledException){break;}catch(IOException){await Task.Delay(500,stop);}
 }
}
