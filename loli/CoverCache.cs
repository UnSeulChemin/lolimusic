using Avalonia.Media;
using Avalonia.Media.Imaging;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Lolimusic;

public sealed class CoverCache:IDisposable
{
 sealed record Hash(string Value);
 sealed class Entry{public required Bitmap Bitmap;public IImage? Crop;public int Pins;public long Used;public long Bytes=>Bitmap.PixelSize.Width*(long)Bitmap.PixelSize.Height*4;}
 readonly ConditionalWeakTable<byte[],Hash> hashes=new();readonly Dictionary<string,Entry> entries=[];long clock;
 public int Decodes{get;private set;}public long Limit{get;set;}=64*1024*1024;
 public Bitmap Acquire(byte[] bytes){var hash=hashes.GetValue(bytes,b=>new Hash(Convert.ToHexString(SHA256.HashData(b)))).Value;
  if(!entries.TryGetValue(hash,out var entry)){using var stream=new MemoryStream(bytes);entry=new(){Bitmap=Bitmap.DecodeToWidth(stream,240)};entries[hash]=entry;Decodes++;}
  entry.Pins++;entry.Used=++clock;Trim();return entry.Bitmap;
 }
 public IImage Crop(Bitmap bitmap,Func<Bitmap,IImage> create){var entry=entries.Values.First(e=>ReferenceEquals(e.Bitmap,bitmap));return entry.Crop??=create(bitmap);}
 public void Release(Bitmap bitmap){var entry=entries.Values.FirstOrDefault(e=>ReferenceEquals(e.Bitmap,bitmap));if(entry!=null)entry.Pins=Math.Max(0,entry.Pins-1);Trim();}
 void Trim(){long total=entries.Values.Sum(e=>e.Bytes);foreach(var item in entries.Where(e=>e.Value.Pins==0).OrderBy(e=>e.Value.Used).ToList()){if(total<=Limit)break;total-=item.Value.Bytes;item.Value.Bitmap.Dispose();entries.Remove(item.Key);}}
 public void Dispose(){foreach(var entry in entries.Values)entry.Bitmap.Dispose();entries.Clear();}
}
