using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Media;
using Avalonia.Layout;

namespace Lolimusic;

public partial class MusicWindow
{
 bool selectingTracks;readonly HashSet<string> selectedTracks=new(StringComparer.OrdinalIgnoreCase);Button? selectionActions;
 void ClearTrackSelection(){selectingTracks=false;selectedTracks.Clear();}
 void AddSelectionActions(){
  libraryActions.Children.Add(Button(selectingTracks?"Terminer la sélection":"Sélectionner",()=>{selectingTracks=!selectingTracks;reordering=false;selectedTracks.Clear();Render();}));
  if(!selectingTracks)return;
  selectionActions=Button($"Actions ({selectedTracks.Count})",()=>{});selectionActions.IsEnabled=selectedTracks.Count>0;
  selectionActions.Click+=(_,_)=>{var chosen=tracks.Where(t=>selectedTracks.Contains(t.Path)).ToList();if(chosen.Count==0)return;selectionActions.ContextMenu=TrackMenu(chosen);selectionActions.ContextMenu.Open(selectionActions);};libraryActions.Children.Add(selectionActions);
 }
 Button TrackCard(Track track,string heading,string caption,Action action){
  var card=CoverCard(track,heading,caption,()=>{if(selectingTracks){if(!selectedTracks.Remove(track.Path))selectedTracks.Add(track.Path);RefreshSelection();}else action();});card.Tag=track;
  card.PointerPressed+=(_,e)=>{if(e.GetCurrentPoint(card).Properties.IsRightButtonPressed){var chosen=selectingTracks&&selectedTracks.Contains(track.Path)?tracks.Where(t=>selectedTracks.Contains(t.Path)).ToList():new List<Track>{track};card.ContextMenu=TrackMenu(chosen);}};
  UpdateCardOutline(card);return card;
 }
 void UpdateCardOutline(Button card){card.BorderBrush=(cardNavigationActive&&card.IsKeyboardFocusWithin)||(selectingTracks&&card.Tag is Track t&&selectedTracks.Contains(t.Path))?Brush("#C59AFF"):Brushes.Transparent;}
 void RefreshSelection(){foreach(var card in cards.Children.OfType<Button>())UpdateCardOutline(card);if(selectionActions!=null){selectionActions.Content=$"Actions ({selectedTracks.Count})";selectionActions.IsEnabled=selectedTracks.Count>0;}}
 void ConfigureFileDrop(){
  DragDrop.SetAllowDrop(this,true);
  AddHandler(DragDrop.DragOverEvent,(_,e)=>{e.DragEffects=e.DataTransfer.TryGetFiles()?.Any()==true?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;});
  AddHandler(DragDrop.DropEvent,async(_,e)=>{
   e.Handled=true;var paths=e.DataTransfer.TryGetFiles()?.Select(f=>f.TryGetLocalPath()).OfType<string>().ToList();if(paths==null||paths.Count==0)return;
   try{var files=await Task.Run(()=>LibraryImport.Files(paths,MusicExtensions).ToList());if(files.Count==0){status.Text="Aucun fichier audio compatible dans ce dépôt.";return;}status.Text="Import des fichiers déposés…";await CopyIntoLibrary(files);await Reload();status.Text=$"{files.Count} fichier(s) audio détecté(s), bibliothèque actualisée.";}catch(Exception error){status.Text="Import impossible : "+error.Message;}
  });
 }
 Window? queueWindow;StackPanel? queueRows;
 void ShowQueue(){
  if(queueWindow!=null){queueWindow.Show(this);queueWindow.Activate();return;}
  queueWindow=new Window{Title="File de lecture",Background=Brush("#241A30"),Width=520,Height=520,WindowStartupLocation=WindowStartupLocation.CenterOwner};
  var panel=new Grid{RowDefinitions=new RowDefinitions("Auto,*"),Margin=new Avalonia.Thickness(20)};panel.Children.Add(Text("File de lecture",21));queueRows=new StackPanel{Spacing=6};Add(panel,new ScrollViewer{Content=queueRows,Margin=new Avalonia.Thickness(0,14,0,0)},1);queueWindow.Content=panel;
  queueWindow.Closed+=(_,_)=>{queueWindow=null;queueRows=null;};RefreshQueue();queueWindow.Show(this);
 }
 void RefreshQueue(){
  if(queueRows==null)return;queueRows.Children.Clear();
  if(queue.Count==0){queueRows.Children.Add(Text("Lance un morceau pour remplir la file.",14));return;}
  if(shuffle)queueRows.Children.Add(Text("Aléatoire activé : le prochain titre est choisi au hasard.",12,"#BFB2D3"));
  if(repeat)queueRows.Children.Add(Text("Répétition activée : le titre actuel sera rejoué.",12,"#BFB2D3"));
  int currentIndex=current==null?-1:queue.FindIndex(t=>t.Path.Equals(current.Path,StringComparison.OrdinalIgnoreCase));
  var ordered=currentIndex<0?queue.ToList():queue.Skip(currentIndex).Concat(queue.Take(currentIndex)).ToList();
  foreach(var song in ordered){var button=Button($"{(song==current?"▶ ":"")}{song.Title} — {song.Artist}",()=>Start(song));queueRows.Children.Add(button);}
 }
}
