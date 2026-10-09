using Avalonia;
using Avalonia.Controls;
namespace Lolimusic;
public partial class MusicWindow
{
 async Task ImportYouTube(bool netease=false){
  var service=netease?"NetEase":"YouTube";
  using var cancel=new CancellationTokenSource(TimeSpan.FromMinutes(15));var dialog=new Window{Title="Importer depuis "+service,Width=540,SizeToContent=SizeToContent.Height,CanResize=false,Background=Brush("#241A30"),WindowStartupLocation=WindowStartupLocation.CenterOwner};
  var panel=new StackPanel{Margin=new Thickness(20),Spacing=12};panel.Children.Add(Text("Importer une musique "+service,20));var link=new TextBox{PlaceholderText=netease?"https://music.163.com/#/song?id=…":"https://www.youtube.com/watch?v=…"};panel.Children.Add(link);var notice=new TextBlock{Text=netease?"Le titre, les artistes, la pochette et le lien seront récupérés si le morceau complet est accessible. Sinon, importe le fichier .ncm téléchargé dans NetEase.":"Le titre, la miniature et le lien YouTube seront récupérés. L’artiste dépend des informations de la vidéo.",TextWrapping=Avalonia.Media.TextWrapping.Wrap};panel.Children.Add(notice);var import=Button("Importer",()=>{});panel.Children.Add(import);dialog.Content=panel;bool finished=false;dialog.Closed+=(_,_)=>cancel.Cancel();
  import.Click+=async(_,_)=>{var url=netease?Lolimusic.NetEaseLinks.Normalize(link.Text):Lolimusic.YouTubeLinks.Normalize(link.Text);if(url==null){notice.Text="Colle un lien "+service+" valide.";return;}import.IsEnabled=false;link.IsEnabled=false;notice.Text="Import en cours… Tu peux fermer cette fenêtre pour annuler.";try{var path=await YouTubeImport.Download(url,MusicDirectory,cancel.Token,netease);Lolimusic.YouTubeLinks.Set(prefs,path,url,netease);prefs.Save();await Reload();finished=true;dialog.Close();status.Text="Musique "+service+" importée : retrouve-la dans Titres.";}catch(OperationCanceledException){notice.Text="Import annulé ou délai dépassé.";}catch(Exception e){notice.Text="Import impossible : "+e.Message;}finally{if(!finished&&!cancel.IsCancellationRequested){import.IsEnabled=true;link.IsEnabled=true;}}};await dialog.ShowDialog(this);
 }
}
