namespace Lolimusic;
public partial class MusicWindow
{
 readonly Dictionary<string,Task<string>> coverUploads=new();
 readonly SemaphoreSlim coverUploadSlot=new(1,1);
 bool preparingCovers,coverPreparationPending;
 string? CoverUploadKey(Track song){var hash=song.CoverHash;if(hash==null&&song.Cover is {Length:>0} bytes)hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));return hash==null?null:(prefs.NetEaseLinks.ContainsKey(song.Path)?hash+":jpeg-v1":hash);}
 Task<string> EnsureCoverUploaded(Track song,string key){
  if(prefs.UploadedCovers.TryGetValue(key,out var cached))return Task.FromResult(cached);
  if(coverUploads.TryGetValue(key,out var running))return running;
  var task=UploadPreparedCover(song,key);coverUploads[key]=task;return task;
 }
 async Task<string> UploadPreparedCover(Track song,string key){
  // Yield before registering the task so even immediate failures are deduplicated.
  await Task.Yield();await coverUploadSlot.WaitAsync();uploadingCovers.Add(key);
  try{
   if(closed||!prefs.AutoDiscordCovers)throw new OperationCanceledException();
   var jpeg=await DiscordCoverEncoder.Encode(song.Cover??throw new IOException("Pochette introuvable."));
   using var form=new System.Net.Http.MultipartFormDataContent();form.Add(new System.Net.Http.StringContent("fileupload"),"reqtype");
   var file=new System.Net.Http.ByteArrayContent(jpeg);file.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");form.Add(file,"fileToUpload","cover.jpg");
   using var response=await coverClient.PostAsync("https://catbox.moe/user/api.php",form);response.EnsureSuccessStatusCode();var url=(await response.Content.ReadAsStringAsync()).Trim();
   if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host!="files.catbox.moe")throw new IOException("Réponse invalide du service de pochettes.");
   if(!closed){prefs.UploadedCovers[key]=url;prefs.Save();if(current?.CoverHash==song.CoverHash)Presence();}
   return url;
  }finally{uploadingCovers.Remove(key);coverUploads.Remove(key);coverUploadSlot.Release();}
 }
 async void PrepareDiscordCovers(){
  if(closed||!prefs.AutoDiscordCovers)return;
  if(preparingCovers){coverPreparationPending=true;return;}preparingCovers=true;
  try{
   foreach(var song in tracks.ToArray()){
    if(closed||!prefs.AutoDiscordCovers)break;var key=CoverUploadKey(song);if(key==null||prefs.UploadedCovers.ContainsKey(key))continue;
    try{await EnsureCoverUploaded(song,key);}catch(OperationCanceledException){break;}catch(Exception e){if(!closed)discordStatus.Text="Préparation de pochette impossible : "+e.Message;}
   }
  }finally{preparingCovers=false;if(coverPreparationPending&&!closed){coverPreparationPending=false;PrepareDiscordCovers();}}
 }
}
