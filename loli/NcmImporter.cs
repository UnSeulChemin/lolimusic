using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lolimusic;

// NCM container layout verified against taurusxin/ncmdump's format implementation.
// Audio is extracted locally without transcoding; the original container is retained.
public static class NcmImporter
{
 public record Result(string Path,string? NetEaseUrl);
 static readonly object gate=new();
 static byte[] Decrypt(byte[] value,string hex){using var aes=Aes.Create();aes.Key=Convert.FromHexString(hex);return aes.DecryptEcb(value,PaddingMode.PKCS7);}
 static byte[] Block(BinaryReader reader,int limit){uint length=reader.ReadUInt32();if(length>limit||length>reader.BaseStream.Length-reader.BaseStream.Position)throw new InvalidDataException("Bloc NCM invalide.");return reader.ReadBytes((int)length);}
 public static Result Import(string source,string destinationDirectory){lock(gate){
  var root=System.IO.Path.GetFullPath(destinationDirectory);Directory.CreateDirectory(root);var info=new FileInfo(source);
  var fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(info.FullName+"|"+info.Length+"|"+info.LastWriteTimeUtc.Ticks)));
  var cache=System.IO.Path.Combine(root,".cache","ncm-imports");Directory.CreateDirectory(cache);var manifest=System.IO.Path.Combine(cache,fingerprint+".json");
  try{var saved=JsonSerializer.Deserialize<Result>(File.ReadAllText(manifest));if(saved!=null&&System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(saved.Path))!.Equals(root,StringComparison.OrdinalIgnoreCase))return saved;}catch(IOException){}catch(JsonException){}catch(ArgumentException){}
  using var stream=File.OpenRead(source);var contentHash=Convert.ToHexString(SHA256.HashData(stream));stream.Position=0;using var reader=new BinaryReader(stream);
  if(Encoding.ASCII.GetString(reader.ReadBytes(8))!="CTENFDAM")throw new InvalidDataException("Ce fichier ne contient pas un morceau NCM valide.");
  stream.Position+=2;var encryptedKey=Block(reader,1024*1024);for(int i=0;i<encryptedKey.Length;i++)encryptedKey[i]^=0x64;
  var key=Decrypt(encryptedKey,"687A4852416D736F356B496E62617857");if(key.Length<=17||Encoding.ASCII.GetString(key,0,17)!="neteasecloudmusic")throw new InvalidDataException("Clé NCM invalide.");
  var box=Enumerable.Range(0,256).Select(i=>(byte)i).ToArray();int previous=0;
  for(int i=0;i<256;i++){previous=(previous+box[i]+key[17+i%(key.Length-17)])&255;(box[i],box[previous])=(box[previous],box[i]);}
  var mask=new byte[256];for(int i=0;i<256;i++){int j=(i+1)&255;mask[i]=box[(box[j]+box[(box[j]+j)&255])&255];}
  var encodedMeta=Block(reader,8*1024*1024);JsonDocument? metadata=null;
  if(encodedMeta.Length>0){for(int i=0;i<encodedMeta.Length;i++)encodedMeta[i]^=0x63;if(encodedMeta.Length<=22)throw new InvalidDataException("Métadonnées NCM invalides.");var decoded=Decrypt(Convert.FromBase64String(Encoding.UTF8.GetString(encodedMeta,22,encodedMeta.Length-22)),"2331346C6A6B5F215C5D2630553C2728");if(decoded.Length<6)throw new InvalidDataException("Métadonnées NCM incomplètes.");metadata=JsonDocument.Parse(decoded.AsMemory(6));}
  using(metadata){
   stream.Position+=5;uint reserved=reader.ReadUInt32();var cover=Block(reader,32*1024*1024);if(reserved<cover.Length||reserved-cover.Length>stream.Length-stream.Position)throw new InvalidDataException("Pochette NCM invalide.");stream.Position+=reserved-cover.Length;
   var buffer=new byte[64*1024];int count=stream.Read(buffer);if(count<4)throw new InvalidDataException("Audio NCM manquant.");long position=0;
   void Decode(int length){for(int i=0;i<length;i++)buffer[i]^=mask[(int)((position+i)&255)];position+=length;}
   Decode(count);string extension=buffer.AsSpan(0,4).SequenceEqual("fLaC"u8)?".flac":buffer.AsSpan(0,3).SequenceEqual("ID3"u8)||(buffer[0]==255&&(buffer[1]&224)==224)?".mp3":throw new InvalidDataException("Audio NCM non reconnu (MP3/FLAC attendus).");
   var target=System.IO.Path.Combine(root,System.IO.Path.GetFileNameWithoutExtension(source)+"_"+contentHash[..12]+extension);var temporary=System.IO.Path.Combine(cache,Guid.NewGuid().ToString("N")+extension);
   try{
    using(var output=File.Create(temporary)){output.Write(buffer,0,count);while((count=stream.Read(buffer))>0){Decode(count);output.Write(buffer,0,count);}}
    string? url=null;using(var audio=TagLib.File.Create(temporary)){
     if(audio.Properties.Duration<=TimeSpan.Zero)throw new InvalidDataException("Le morceau NCM ne contient pas de son lisible.");
     if(metadata!=null){var meta=metadata.RootElement;
      if(meta.TryGetProperty("musicName",out var title)&&title.ValueKind==JsonValueKind.String)audio.Tag.Title=title.GetString();
      if(meta.TryGetProperty("album",out var album)&&album.ValueKind==JsonValueKind.String)audio.Tag.Album=album.GetString();
      if(meta.TryGetProperty("artist",out var artists)&&artists.ValueKind==JsonValueKind.Array)audio.Tag.Performers=artists.EnumerateArray().Where(a=>a.ValueKind==JsonValueKind.Array&&a.GetArrayLength()>0&&a[0].ValueKind==JsonValueKind.String).Select(a=>a[0].GetString()!).ToArray();
      if(meta.TryGetProperty("musicId",out var id)&&ulong.TryParse(id.ToString(),out var songId)&&songId>0)url=$"https://music.163.com/#/song?id={songId}";
     }
     if(cover.Length>0)audio.Tag.Pictures=[new TagLib.Picture(new TagLib.ByteVector(cover)){Type=TagLib.PictureType.FrontCover}];audio.Save();
    }
    if(!File.Exists(target))File.Move(temporary,target);else File.Delete(temporary);
    var result=new Result(target,url);File.WriteAllText(manifest+".tmp",JsonSerializer.Serialize(result));File.Move(manifest+".tmp",manifest,true);return result;
   }finally{if(File.Exists(temporary))File.Delete(temporary);}
  }
 }}
}
