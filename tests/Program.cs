using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Lolimusic;
using System.Reflection;

void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("OK "+message);}
var root=Path.Combine(AppContext.BaseDirectory,"fixtures");Directory.CreateDirectory(root);
var old=Path.Combine(root,"old.m4a");var moved=Path.Combine(root,"nested","renamed.m4a");File.WriteAllText(old,"fixture");
var id=LibraryReferences.Identity(old);
var settings=new Settings{Playlists=[new Playlist{Name="Test",Paths=[old]}],Favorites=[old]};settings.DiscordCovers[old]="https://example.com/cover.png";settings.FileIdentities[old]=id;
Directory.CreateDirectory(Path.GetDirectoryName(moved)!);File.Move(old,moved,true);
Check(LibraryReferences.Identity(moved)==id,"file identity survives folder move and rename");
var mappings=LibraryReferences.Reconcile(settings,new(){{moved,id}});
Check(mappings[old]==moved&&settings.Playlists[0].Paths.Single()==moved,"playlist follows moved file");
Check(settings.Favorites.Single()==moved&&settings.DiscordCovers.ContainsKey(moved),"favorites and cover follow moved file");
File.AppendAllText(moved,"changed tags");Check(LibraryReferences.Identity(moved)==id,"file identity survives metadata write");
LibraryReferences.Reconcile(settings,new());Check(settings.Playlists[0].Paths.Single()==moved,"missing file keeps playlist reference");
LibraryReferences.Reconcile(settings,new(){{moved,id}});Check(settings.Playlists[0].Paths.Count==1,"rescan does not duplicate references");
var ambiguous=new Settings{Playlists=[new Playlist{Name="Ambiguous",Paths=[old]}]};ambiguous.FileIdentities[old]="same";
LibraryReferences.Reconcile(ambiguous,new(){{moved,"same"},{moved+"2","same"}});Check(ambiguous.Playlists[0].Paths.Single()==old,"ambiguous identity is never guessed");
AppBuilder.Configure<App>().UsePlatformDetect().SetupWithoutStarting();
var memorySettings=new MemorySettings();var window=new MusicWindow(memorySettings);
var type=typeof(MusicWindow);var flags=BindingFlags.Instance|BindingFlags.NonPublic;
var preferences=(Settings)type.GetField("prefs",flags)!.GetValue(window)!;
preferences.Playlists=[new Playlist{Name="One"},new Playlist{Name="Two"}];
type.GetMethod("Render",flags)!.Invoke(window,null);
var cards=(WrapPanel)type.GetField("cards",flags)!.GetValue(window)!;
Check(cards.Children.Count==2&&((Button)cards.Children[0]).ContextMenu?.ItemsSource?.Cast<object>().Count()==2,"playlist context menu contains rename and delete");
type.GetField("reordering",flags)!.SetValue(window,true);type.GetMethod("Render",flags)!.Invoke(window,null);
Check(cards.Children.All(c=>c.Cursor!=null),"mouse reorder mode assigns drag cursor");
var frame=(Grid)window.Content!;Check(frame.Children.Count==9&&window.CanResize,"all eight resize grips exist");
Check(window.Icon!=null,"application icon loads");
using(var nativeTray=new TrayIcon{Icon=window.Icon,IsVisible=false}){
 TrayIcon.SetIcons(Application.Current!,new TrayIcons{nativeTray});
 var implementation=typeof(TrayIcon).GetField("_impl",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(nativeTray);
 Check(implementation!=null&&TrayIcon.GetIcons(Application.Current!)!.Contains(nativeTray),"tray icon attaches to application and creates a native Windows implementation");
 TrayIcon.SetIcons(Application.Current!,null);
}
Console.WriteLine("All checks passed.");

// A backup must keep order and still resolve under a different music root.
using(var archive=new MemoryStream()){
 var original=new List<Playlist>{new(){Name="Mix",Paths=[Path.Combine(root,"nested","renamed.m4a"),Path.Combine(root,"absent.mp3")]}};
 await PlaylistArchive.Write(archive,original,root);archive.Position=0;
 var restoredRoot=Path.Combine(root,"other-pc");var imported=await PlaylistArchive.Read(archive,restoredRoot);
 Check(imported.Single().Name=="Mix"&&imported[0].Paths[0]==Path.Combine(restoredRoot,"nested","renamed.m4a"),"playlist archive relocates relative paths");
 Check(imported[0].Paths[1]==Path.Combine(restoredRoot,"absent.mp3"),"playlist archive preserves order and absent files");
}
using(var invalid=new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{\"Version\":1,\"Playlists\":[{\"Name\":\"Bad\",\"Paths\":[\"../outside.mp3\"]}]}"))){
 bool rejected=false;try{await PlaylistArchive.Read(invalid,root);}catch(InvalidDataException){rejected=true;}Check(rejected,"playlist import rejects relative traversal");
}
var wave=Path.Combine(root,"editable.wav");
using(var writer=new NAudio.Wave.WaveFileWriter(wave,new NAudio.Wave.WaveFormat(44100,16,1))){writer.Write(new byte[88200],0,88200);}
var picture=File.ReadAllBytes(Path.Combine(Directory.GetCurrentDirectory(),"loli","Assets","lolimusic.png"));
TrackMetadata.Save(wave,root,"Test title","Test artist","Test album",picture,true);
using(var tagged=TagLib.File.Create(wave)){Check(tagged.Tag.Title=="Test title"&&tagged.Tag.FirstPerformer=="Test artist"&&tagged.Tag.Album=="Test album","metadata editor writes title artist and album");Check(tagged.Tag.Pictures.Length==1&&tagged.Tag.Pictures[0].Data.Data.SequenceEqual(picture),"metadata editor embeds original cover bytes");}
using(var audioCheck=new NAudio.Wave.AudioFileReader(wave)){Check(audioCheck.Read(new byte[1024],0,1024)>0,"editing metadata preserves decodable audio");}
TrackMetadata.Save(wave,root,"Changed","Test artist","Test album",null,true);
using(var tagged=TagLib.File.Create(wave))Check(tagged.Tag.Pictures.Length==0,"metadata editor removes embedded cover");
Check(Directory.EnumerateFiles(Path.Combine(root,".cache","metadata-backups")).Any(p=>p.EndsWith(".wav")),"metadata editor keeps previous file backup");
var session=new Settings{WindowWidth=1200,WindowHeight=700,WindowMaximized=true,LastTrack=moved,LastPosition=42,LastQueue=[moved]};
var restoredSession=System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(session))!;
Check(restoredSession.LastPosition==42&&restoredSession.LastQueue.Single()==moved&&restoredSession.WindowWidth==1200&&restoredSession.WindowMaximized,"session settings round-trip size track queue and position");
Console.WriteLine("Feature checks passed.");

var restoredTrack=new Track(wave,"Changed","Test artist","Test album",null);
preferences.LastTrack=wave;preferences.LastPosition=.04;preferences.LastQueue=[wave];
type.GetField("tracks",flags)!.SetValue(window,new List<Track>{restoredTrack});
await (Task)type.GetMethod("RestoreSession",flags)!.Invoke(window,null)!;
var restoredOutput=(NAudio.Wave.WaveOutEvent?)type.GetField("output",flags)!.GetValue(window);
var restoredAudio=(NAudio.Wave.AudioFileReader?)type.GetField("audio",flags)!.GetValue(window);
Check(restoredOutput?.PlaybackState==NAudio.Wave.PlaybackState.Paused&&restoredAudio?.CurrentTime.TotalSeconds>=.03,"session restores last track and position in pause");
Check(memorySettings.Saves>0&&memorySettings.LastTrack==wave,"session persistence uses isolated test settings");
Check(!(bool)type.GetField("sessionHasPlayed",flags)!.GetValue(window)!,"restoring paused track does not activate Discord for this session");
type.GetMethod("Toggle",flags)!.Invoke(window,null);
Check((bool)type.GetField("sessionHasPlayed",flags)!.GetValue(window)!,"first playback activates Discord for this session");
type.GetMethod("Toggle",flags)!.Invoke(window,null);
Check((bool)type.GetField("sessionHasPlayed",flags)!.GetValue(window)!,"later pause keeps Discord enabled");
restoredOutput?.Dispose();restoredAudio?.Dispose();
var sample=Directory.Exists(MusicWindow.MusicDirectory)?Directory.EnumerateFiles(MusicWindow.MusicDirectory,"*.m4a",SearchOption.AllDirectories).OrderBy(p=>new FileInfo(p).Length).FirstOrDefault():null;
if(sample!=null){
 var m4a=Path.Combine(root,"editable.m4a");File.Copy(sample,m4a,true);TimeSpan duration;using(var original=TagLib.File.Create(m4a))duration=original.Properties.Duration;
 TrackMetadata.Save(m4a,root,"M4A test","Artist","Album",picture,true);
 using(var edited=TagLib.File.Create(m4a))Check(edited.Tag.Title=="M4A test"&&edited.Tag.Pictures.Length==1&&edited.Properties.Duration==duration,"M4A tags and cover save without changing audio duration");
}
var gridBounds=new List<Rect>{new(0,0,210,250),new(224,0,210,250),new(448,0,210,250),new(0,264,210,250),new(224,264,210,250)};
Check(CardNavigation.Destination(gridBounds,1,Avalonia.Input.Key.Down)==4,"keyboard Down preserves column across rows");
Check(CardNavigation.Destination(gridBounds,2,Avalonia.Input.Key.Down)==4,"keyboard Down handles incomplete final row");
Check(CardNavigation.Destination(gridBounds,4,Avalonia.Input.Key.Up)==1,"keyboard Up returns to previous row");
Check(CardNavigation.Destination(gridBounds,0,Avalonia.Input.Key.Left)==0&&CardNavigation.Destination(gridBounds,4,Avalonia.Input.Key.Right)==4,"keyboard arrows stay within card boundaries");
var importedFiles=LibraryImport.Files(new[]{wave,root,wave},new HashSet<string>(StringComparer.OrdinalIgnoreCase){".wav"}).ToList();
Check(importedFiles.Count==1&&importedFiles.Single()==wave,"drop import accepts files and folders without duplicates or cached backups");
Check(!LibraryImport.Files(new[]{Path.Combine(root,"missing.mp3")},new HashSet<string>{".mp3"}).Any(),"drop import ignores missing files");
Check(TrackSearch.Matches(restoredTrack,"  ARTIST ")&&TrackSearch.Matches(restoredTrack,"album")&&!TrackSearch.Matches(restoredTrack,"no match"),"playlist search matches artist and album regardless of case");
type.GetField("section",flags)!.SetValue(window,"Titres");
type.GetField("selectingTracks",flags)!.SetValue(window,true);
type.GetField("tracks",flags)!.SetValue(window,new List<Track>{restoredTrack});
type.GetMethod("Render",flags)!.Invoke(window,null);
var selectedSet=(HashSet<string>)type.GetField("selectedTracks",flags)!.GetValue(window)!;
var selectable=(Button)cards.Children.Single();
selectable.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
Check(selectedSet.Contains(wave),"selection mode selects a card without starting playback");
selectable.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
Check(!selectedSet.Contains(wave),"selection mode toggles a selected card off");
var multi=new List<Track>{restoredTrack,restoredTrack with{Path=wave+"2"}};
type.GetField("selectedPlaylist",flags)!.SetValue(window,new Playlist{Name="Test",Paths=multi.Select(t=>t.Path).ToList()});type.GetField("section",flags)!.SetValue(window,"Playlists");
var groupMenu=(ContextMenu)type.GetMethod("TrackMenu",flags)!.Invoke(window,new object[]{multi})!;
var menuItems=groupMenu.ItemsSource!.Cast<MenuItem>().ToList();
Check(menuItems.Any(i=>i.Header?.ToString()=="Retirer de cette playlist")&&menuItems.Last().Header?.ToString()=="Supprimer ces titres de la bibliothèque…","bulk menu removes from playlist and keeps file deletion last");
var selectedPlaylist=(Playlist)type.GetField("selectedPlaylist",flags)!.GetValue(window)!;
menuItems.First(i=>i.Header?.ToString()=="Retirer de cette playlist").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
Check(selectedPlaylist.Paths.Count==0&&File.Exists(wave),"bulk removal clears playlist references while preserving the music file");
var queueRows=new StackPanel();type.GetField("queueRows",flags)!.SetValue(window,queueRows);
var a=restoredTrack with{Title="A",Path=wave+"a"};var b=restoredTrack with{Title="B",Path=wave+"b"};var c=restoredTrack with{Title="C",Path=wave+"c"};
type.GetField("queue",flags)!.SetValue(window,new List<Track>{a,b,c});type.GetField("current",flags)!.SetValue(window,b);
type.GetMethod("RefreshQueue",flags)!.Invoke(window,null);
var queuedLabels=queueRows.Children.OfType<Button>().Select(i=>i.Content?.ToString()).ToList();
Check(queuedLabels[0]!.Contains("B —")&&queuedLabels[1]!.Contains("C —")&&queuedLabels[2]!.Contains("A —"),"queue display starts with current track then follows playback order");
Console.WriteLine("Import, search, selection, bulk-action and queue checks passed.");

sealed class MemorySettings:Settings {
 public int Saves{get;private set;}
 public override void Save()=>Saves++;
}
