using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using NAudio.Wave;
using System.Runtime.InteropServices;

namespace Lolimusic;

public partial class MusicWindow
{
 TrayIcon? tray;bool quitting;IntPtr mediaHandle;
 readonly List<int> mediaRegistrations=[];
 readonly DispatcherTimer sessionTimer=new(){Interval=TimeSpan.FromSeconds(30)};
 readonly CancellationTokenSource instanceStop=new();
 [DllImport("user32.dll",SetLastError=true)]static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
 [DllImport("user32.dll")]static extern bool UnregisterHotKey(IntPtr window,int id);
 void ConfigureDesktopFeatures(){
  Closing+=(_,e)=>{RememberSession();if(!quitting&&tray?.IsVisible==true&&e.CloseReason==WindowCloseReason.WindowClosing){e.Cancel=true;queueWindow?.Hide();Hide();}};
  bool desktopInitialized=false;Opened+=(_,_)=>{if(desktopInitialized)return;desktopInitialized=true;
   _=InstanceBridge.Listen(command=>Dispatcher.UIThread.Post(()=>{if(command=="quit")QuitMusic();else ShowMusic();}),instanceStop.Token);
   if(tray==null){
    var menu=new NativeMenu();
    void Item(string label,Action action){var item=new NativeMenuItem(label);item.Click+=(_,_)=>action();menu.Items.Add(item);}
    Item("Ouvrir lolimusic",ShowMusic);Item("Lecture / pause",Toggle);Item("Titre précédent",()=>Next(-1));Item("Titre suivant",()=>Next(1));menu.Items.Add(new NativeMenuItemSeparator());Item("Quitter",QuitMusic);
    tray=new TrayIcon{Icon=Icon,ToolTipText="lolimusic — clic pour ouvrir",Menu=menu,IsVisible=true};tray.Clicked+=(_,_)=>ShowMusic();
    TrayIcon.SetIcons(Application.Current!,new TrayIcons{tray});
   }
   if(prefs.WindowMaximized)WindowState=WindowState.Maximized;
   var area=Screens.ScreenFromWindow(this)?.WorkingArea;if(area!=null){Width=Math.Min(Width,area.Value.Width/RenderScaling);Height=Math.Min(Height,area.Value.Height/RenderScaling);}
   RegisterMediaKeys();sessionTimer.Start();
  };
  sessionTimer.Tick+=(_,_)=>RememberSession();Closed+=(_,_)=>{sessionTimer.Stop();instanceStop.Cancel();};
  Win32Properties.AddWndProcHookCallback(this,MediaMessage);
 }
 void ShowMusic(){Show();if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Activate();}
 void QuitMusic(){quitting=true;Close();}
 void RegisterMediaKeys(){
  if(mediaHandle!=IntPtr.Zero)return;mediaHandle=TryGetPlatformHandle()?.Handle??IntPtr.Zero;
  if(mediaHandle==IntPtr.Zero)return;
  foreach(var (id,key) in new[]{(1,0xB3u),(2,0xB0u),(3,0xB1u),(4,0xB2u)})if(RegisterHotKey(mediaHandle,0x4C00+id,0x4000,key))mediaRegistrations.Add(0x4C00+id);
  if(mediaRegistrations.Count<4)status.Text="Certaines touches multimédias sont utilisées par une autre application.";
 }
 IntPtr MediaMessage(IntPtr handle,uint message,IntPtr wParam,IntPtr lParam,ref bool handled){
  if(message==0x0312&&mediaRegistrations.Contains(wParam.ToInt32())){handled=true;Dispatcher.UIThread.Post(()=>MediaCommand(wParam.ToInt32()-0x4C00));return IntPtr.Zero;}
  if(message==0x0319){int command=(int)((lParam.ToInt64()>>16)&0xFFF);if(command is 11 or 12 or 13 or 14){handled=true;Dispatcher.UIThread.Post(()=>MediaCommand(command switch{11=>2,12=>3,13=>4,_=>1}));return new IntPtr(1);}}
  return IntPtr.Zero;
 }
 void MediaCommand(int command){switch(command){case 1:Toggle();break;case 2:Next(1);break;case 3:Next(-1);break;case 4:output?.Pause();play.Content=PlayerIcon("play");Presence();break;}}
 void ReleaseMediaKeys(){foreach(var id in mediaRegistrations)UnregisterHotKey(mediaHandle,id);mediaRegistrations.Clear();mediaHandle=IntPtr.Zero;}
 void RememberSession(){
  if(WindowState==WindowState.Normal&&IsVisible){prefs.WindowWidth=Width;prefs.WindowHeight=Height;}
  if(WindowState!=WindowState.Minimized)prefs.WindowMaximized=WindowState==WindowState.Maximized;
  prefs.LastTrack=current?.Path;prefs.LastPosition=audio?.CurrentTime.TotalSeconds??0;prefs.LastQueue=queue.Select(t=>t.Path).ToList();prefs.Save();
 }
 async Task RestoreSession(){
  var song=tracks.FirstOrDefault(t=>t.Path.Equals(prefs.LastTrack,StringComparison.OrdinalIgnoreCase));if(song==null)return;
  queue=prefs.LastQueue.Select(p=>tracks.FirstOrDefault(t=>t.Path.Equals(p,StringComparison.OrdinalIgnoreCase))).OfType<Track>().ToList();if(!queue.Any(t=>t.Path==song.Path))queue=tracks.ToList();
  await StartTrack(song,prefs.LastPosition,paused:true);
 }
 void AddFeatureMenu(StackPanel panel,Flyout popup){
  panel.Children.Add(Button("Exporter les playlists…",async()=>{popup.Hide();await ExportPlaylists();}));
  panel.Children.Add(Button("Importer des playlists…",async()=>{popup.Hide();await ImportPlaylists();}));
  panel.Children.Add(Button("Quitter lolimusic",()=>{popup.Hide();QuitMusic();}));
 }
 async Task ExportPlaylists(){try{
  var file=await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions{Title="Sauvegarder les playlists",SuggestedFileName="lolimusic-playlists.json",DefaultExtension="json",FileTypeChoices=[new FilePickerFileType("Playlists lolimusic"){Patterns=["*.json"]}]});if(file==null)return;
  await using var stream=await file.OpenWriteAsync();stream.SetLength(0);await PlaylistArchive.Write(stream,prefs.Playlists,MusicDirectory);status.Text="Playlists exportées (les fichiers audio restent dans music).";
 }catch(Exception e){status.Text="Export impossible : "+e.Message;}}
 async Task ImportPlaylists(){try{
  var files=await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions{Title="Restaurer des playlists",AllowMultiple=false,FileTypeFilter=[new FilePickerFileType("Playlists lolimusic"){Patterns=["*.json"]}]});if(files.Count==0)return;
  await using var stream=await files[0].OpenReadAsync();var imported=await PlaylistArchive.Read(stream,MusicDirectory);
  foreach(var playlist in imported){var original=playlist.Name;int suffix=2;while(prefs.Playlists.Any(p=>p.Name.Equals(playlist.Name,StringComparison.OrdinalIgnoreCase)))playlist.Name=$"{original} ({suffix++})";prefs.Playlists.Add(playlist);}
  prefs.Save();Render();status.Text=$"{imported.Count} playlist(s) importée(s). Les titres absents sont conservés pour plus tard.";
 }catch(Exception e){status.Text="Import impossible : "+e.Message;}}
}
