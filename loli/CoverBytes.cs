namespace Lolimusic;

// Original embedded artwork stays on disk; only recently used bytes stay in memory.
public static class CoverBytes
{
 sealed record Entry(byte[] Bytes,long Used);
 static readonly Dictionary<string,Entry> entries=new(StringComparer.OrdinalIgnoreCase);
 static readonly object gate=new();static long clock,total;
 public const long Limit=32L*1024*1024;
 public static byte[]? Read(string path){lock(gate){
  if(entries.TryGetValue(path,out var existing)){entries[path]=existing with{Used=++clock};return existing.Bytes;}
  byte[] bytes;try{bytes=File.ReadAllBytes(path);}catch(IOException){return null;}catch(UnauthorizedAccessException){return null;}
  if(bytes.LongLength>Limit)return bytes;
  while(total+bytes.LongLength>Limit&&entries.Count>0){var oldest=entries.MinBy(e=>e.Value.Used);total-=oldest.Value.Bytes.LongLength;entries.Remove(oldest.Key);}
  entries[path]=new(bytes,++clock);total+=bytes.LongLength;return bytes;
 }}
 public static long CachedBytes{get{lock(gate)return total;}}
}
