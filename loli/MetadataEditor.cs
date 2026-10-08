using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using NAudio.Wave;

namespace Lolimusic;

public partial class MusicWindow
{
 MenuItem MetadataMenu(List<Track> songs){var edit=new MenuItem{Header="Modifier les infos et la pochette…",IsEnabled=songs.Count==1};edit.Click+=async(_,_)=>await EditMetadata(songs[0]);return edit;}
 async Task EditMetadata(Track song){
  var dialog=new Window{Title="Modifier le morceau",Background=Brush("#241A30"),Width=520,SizeToContent=SizeToContent.Height,CanResize=false,WindowStartupLocation=WindowStartupLocation.CenterOwner};
  var panel=new StackPanel{Margin=new Thickness(24),Spacing=10};panel.Children.Add(Text("Infos et pochette",21));
  TextBox Field(string label,string value){panel.Children.Add(Text(label,12,"#BFB2D3"));var box=new TextBox{Text=value};panel.Children.Add(box);return box;}
  var name=Field("Titre",song.Title);var artist=Field("Artiste",song.Artist=="Artiste inconnu"?"":song.Artist);var album=Field("Album",song.Album=="Sans album"?"":song.Album);
  byte[]? cover=song.Cover;bool changedCover=false;Bitmap? previewBitmap=null;
  var preview=new Image{Width=112,Height=112,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Left};
  void Preview(){preview.Source=null;previewBitmap?.Dispose();previewBitmap=null;if(cover!=null){using var stream=new MemoryStream(cover);previewBitmap=Bitmap.DecodeToWidth(stream,224);preview.Source=previewBitmap;}}
  try{Preview();}catch{cover=null;}panel.Children.Add(preview);var error=new TextBlock{Foreground=Brush("#E9B1CE"),TextWrapping=TextWrapping.Wrap};
  var coverActions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
  coverActions.Children.Add(Button("Choisir une image…",async()=>{try{
   var selected=await dialog.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions{Title="Pochette du morceau",AllowMultiple=false,FileTypeFilter=[new FilePickerFileType("Images PNG / JPEG"){Patterns=["*.png","*.jpg","*.jpeg"]}]});if(selected.Count==0)return;
   await using var stream=await selected[0].OpenReadAsync();if(stream.CanSeek&&stream.Length>20*1024*1024)throw new IOException("Choisis une image de moins de 20 Mo.");using var memory=new MemoryStream();await stream.CopyToAsync(memory);var bytes=memory.ToArray();using(var validate=new Bitmap(new MemoryStream(bytes))){}cover=bytes;changedCover=true;Preview();error.Text="";
  }catch(Exception e){error.Text="Image impossible : "+e.Message;}}));
  coverActions.Children.Add(Button("Retirer la pochette",()=>{cover=null;changedCover=true;Preview();}));panel.Children.Add(coverActions);panel.Children.Add(error);
  var actions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};var save=Button("Enregistrer",()=>{});var cancel=Button("Annuler",()=>dialog.Close());actions.Children.Add(save);actions.Children.Add(cancel);panel.Children.Add(actions);
  save.Click+=async(_,_)=>{
   if(string.IsNullOrWhiteSpace(name.Text)){error.Text="Renseigne un titre.";return;}
   save.IsEnabled=cancel.IsEnabled=false;coverActions.IsEnabled=false;
   bool playing=output?.PlaybackState==PlaybackState.Playing;bool editingCurrent=current?.Path.Equals(song.Path,StringComparison.OrdinalIgnoreCase)==true;double position=audio?.CurrentTime.TotalSeconds??0;
   try{
    if(editingCurrent){playbackRequest++;output?.Stop();output?.Dispose();audio?.Dispose();output=null;audio=null;discord?.ClearPresence();}
    var titleValue=name.Text!;var artistValue=artist.Text??"";var albumValue=album.Text??"";
    await Task.Run(()=>TrackMetadata.Save(song.Path,MusicDirectory,titleValue,artistValue,albumValue,cover,changedCover));await Reload();
    if(editingCurrent){var refreshed=tracks.FirstOrDefault(t=>t.Path.Equals(song.Path,StringComparison.OrdinalIgnoreCase));if(refreshed!=null)await StartTrack(refreshed,position,!playing);}
    status.Text="Infos et pochette enregistrées dans le fichier.";dialog.Close();
   }catch(Exception e){error.Text="Enregistrement impossible : "+e.Message;if(editingCurrent&&output==null)await StartTrack(song,position,!playing);}
   finally{save.IsEnabled=cancel.IsEnabled=true;coverActions.IsEnabled=true;}
  };
  dialog.Content=panel;try{await dialog.ShowDialog(this);}finally{preview.Source=null;previewBitmap?.Dispose();}
 }
}
