using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using DiscordRPC;

namespace Lolimusic;

// One background worker owns the pipe; playback only replaces the desired activity.
internal sealed class DiscordConnection : IDisposable
{
 readonly string applicationId;
 readonly CancellationTokenSource stop=new();
 readonly object gate=new();
 string activity="null";
 long revision;
 NamedPipeClientStream? pipe;
 public event Action? Ready;
 public event Action<bool>? ActivityUpdated;
 public event Action<string>? Error;
 public DiscordConnection(string id)=>applicationId=id;
 public void Initialize()=>_ = Run();
 public void SetPresence(RichPresence presence){lock(gate){activity=Newtonsoft.Json.JsonConvert.SerializeObject(presence);revision++;}}
 public void ClearPresence(){lock(gate){activity="null";revision++;}}
 async Task Send(int operation,object body,CancellationToken token){
  var bytes=JsonSerializer.SerializeToUtf8Bytes(body);var header=new byte[8];BitConverter.GetBytes(operation).CopyTo(header,0);BitConverter.GetBytes(bytes.Length).CopyTo(header,4);
  await pipe!.WriteAsync(header,token);await pipe.WriteAsync(bytes,token);
 }
 async Task<JsonDocument> Receive(CancellationToken token){
  while(true){var header=new byte[8];await pipe!.ReadExactlyAsync(header,token);int operation=BitConverter.ToInt32(header),size=BitConverter.ToInt32(header,4);
   if(size<0||size>1024*1024)throw new IOException("Réponse Discord invalide.");var bytes=new byte[size];await pipe.ReadExactlyAsync(bytes,token);
   if(operation==2)throw new IOException("Discord a fermé la connexion.");
   if(operation==3){await Send(4,JsonSerializer.Deserialize<JsonElement>(bytes),token);continue;}
   if(operation!=1)continue;
   return JsonDocument.Parse(bytes);
  }
 }
 async Task Run(){
  while(!stop.IsCancellationRequested){
   try{
    for(int i=0;i<10;i++){
     stop.Token.ThrowIfCancellationRequested();pipe=new NamedPipeClientStream(".","discord-ipc-"+i,PipeDirection.InOut,PipeOptions.Asynchronous);
     try{await pipe.ConnectAsync(500,stop.Token);break;}catch(TimeoutException){pipe.Dispose();pipe=null;}
    }
    if(pipe==null)throw new IOException("Ouvre Discord sur ce PC.");
    await Send(0,new{v=1,client_id=applicationId},stop.Token);
    using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token)){timeout.CancelAfter(5000);using var response=await Receive(timeout.Token);if(!response.RootElement.TryGetProperty("evt",out var evt)||evt.GetString()!="READY")throw new IOException("Connexion Discord refusée.");}
    Ready?.Invoke();long sent=-1;DateTime last=DateTime.MinValue;
    while(!stop.IsCancellationRequested){
     string desired;long version;lock(gate){desired=activity;version=revision;}
     if(version!=sent||DateTime.UtcNow-last>TimeSpan.FromSeconds(15)){
      using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);timeout.CancelAfter(5000);var nonce=Guid.NewGuid().ToString("N");
      await Send(1,new{cmd="SET_ACTIVITY",args=new{pid=Environment.ProcessId,activity=JsonSerializer.Deserialize<JsonElement>(desired)},nonce},timeout.Token);
      while(true){using var reply=await Receive(timeout.Token);var root=reply.RootElement;
       if(!root.TryGetProperty("nonce",out var returned)||returned.GetString()!=nonce)continue;
       if(root.TryGetProperty("evt",out var evt)&&evt.GetString()=="ERROR")throw new IOException(root.GetProperty("data").GetProperty("message").GetString());
       bool missingImage=desired.Contains("large_image")&&(!root.TryGetProperty("data",out var data)||!data.TryGetProperty("assets",out var assets)||assets.ValueKind!=JsonValueKind.Object||!assets.TryGetProperty("large_image",out _));
       // Missing assets are a valid response: retry later without disrupting playback.
       ActivityUpdated?.Invoke(!missingImage);sent=missingImage?-1:version;last=DateTime.UtcNow;break;
      }
     }
     await Task.Delay(sent==-1?15000:1000,stop.Token);
    }
   }catch(Exception e){if(!stop.IsCancellationRequested)Error?.Invoke(e.Message);}
   finally{pipe?.Dispose();pipe=null;}
   try{await Task.Delay(3000,stop.Token);}catch(OperationCanceledException){break;}
  }
 }
 public void Dispose(){stop.Cancel();pipe?.Dispose();}
}
