namespace Lolimusic;

public static class TrackMetadata
{
 public static void Save(string path,string root,string title,string artist,string album,byte[]? cover,bool changeCover){
  path=Path.GetFullPath(path);root=Path.GetFullPath(root);
  if(!path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Le morceau doit être dans music.");
  if(string.IsNullOrWhiteSpace(title))throw new ArgumentException("Le titre ne peut pas être vide.");
  var cache=Path.Combine(root,".cache","metadata-backups");Directory.CreateDirectory(cache);
  var staged=Path.Combine(cache,Guid.NewGuid().ToString("N")+Path.GetExtension(path));
  var backup=Path.Combine(cache,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path)))+Path.GetExtension(path));
  try{
   File.Copy(path,staged);
   using(var file=TagLib.File.Create(staged)){
    file.Tag.Title=title.Trim();file.Tag.Performers=string.IsNullOrWhiteSpace(artist)?[]:[artist.Trim()];file.Tag.Album=album.Trim();
    if(changeCover)file.Tag.Pictures=cover==null?[]:[new TagLib.Picture(new TagLib.ByteVector(cover)){Type=TagLib.PictureType.FrontCover,Description="Pochette"}];
    file.Save();
   }
   // Verify the edited copy before atomically replacing the original.
   using(var check=TagLib.File.Create(staged)){if(check.Tag.Title!=title.Trim())throw new IOException("Ce format ne permet pas d'enregistrer le titre.");}
   File.Replace(staged,path,backup,true);
  }finally{if(File.Exists(staged))File.Delete(staged);}
 }
}
