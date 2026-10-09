using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Lolimusic;
using System.Reflection;

void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("OK "+message);}
if(args.Contains("--cover-smoke")){
 AppBuilder.Configure<App>().UsePlatformDetect().SetupWithoutStarting();SynchronizationContext.SetSynchronizationContext(null);var cached=System.Text.Json.JsonSerializer.Deserialize<List<LibraryIndex.SavedEntry>>(File.ReadAllText(Path.Combine(Environment.CurrentDirectory,"music",".cache","library-index","index.json")))!;
 foreach(var entry in cached.Where(e=>e.Path.Contains("嘻哈说唱"))){var original=File.ReadAllBytes(Path.Combine(Environment.CurrentDirectory,"music",".cache","library-index",entry.CoverHash+".cover"));var jpeg=await DiscordCoverEncoder.Encode(original);using var stream=new MemoryStream(jpeg);using var image=new Avalonia.Media.Imaging.Bitmap(stream);Check(image.PixelSize.Width<=384&&image.PixelSize.Height<=384&&jpeg.Length<original.Length,"Discord artwork is smaller and decodable: "+entry.Title+" ("+original.Length+" -> "+jpeg.Length+" bytes)");}return;
}
if(args.Contains("--netease-smoke")){
 using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(2));var downloadRoot=Path.Combine(AppContext.BaseDirectory,"netease-smoke",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(downloadRoot);var imported=await YouTubeImport.Download(args.Length>1?args[1]:"https://music.163.com/#/song?id=3397834659",downloadRoot,timeout.Token,true);using var downloaded=TagLib.File.Create(imported);Check(downloaded.Properties.Duration.TotalSeconds>1&&!string.IsNullOrWhiteSpace(downloaded.Tag.Title),"NetEase retrieves playable audio and title");Check(downloaded.Tag.Performers.Length>0&&downloaded.Tag.Pictures.Length>0,"NetEase embeds artists and artwork");return;
}
if(args.Contains("--youtube-smoke")){
 using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(2));var downloadRoot=Path.Combine(AppContext.BaseDirectory,"youtube-smoke",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(downloadRoot);
 var imported=await YouTubeImport.Download("https://www.youtube.com/watch?v=bZuzakHtGeg",downloadRoot,timeout.Token);
 using var downloaded=TagLib.File.Create(imported);Check(downloaded.Properties.Duration.TotalSeconds>1&&!string.IsNullOrWhiteSpace(downloaded.Tag.Title),"YouTube import retrieves playable audio and title");Check(downloaded.Tag.Pictures.Length>0,"YouTube import embeds the video thumbnail");return;
}
var root=Path.Combine(AppContext.BaseDirectory,"fixtures",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
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
var cards=(VirtualCardPanel)type.GetField("cards",flags)!.GetValue(window)!;
cards.Measure(new Size(900,800));cards.Arrange(new Rect(0,0,900,cards.DesiredSize.Height));
Check(cards.Children.Count==2&&((Button)cards.Children[0]).ContextMenu?.ItemsSource?.Cast<object>().Count()==2,"playlist context menu contains rename and delete");
type.GetField("reordering",flags)!.SetValue(window,true);type.GetMethod("Render",flags)!.Invoke(window,null);
cards.Measure(new Size(900,800));
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
cards.Measure(new Size(900,800));var selectable=(Button)cards.Children.Single();
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

using(var multiArtistFile=TagLib.File.Create(wave)){multiArtistFile.Tag.Performers=["Premier", "Deuxième"];multiArtistFile.Save();}Check(new LibraryIndex().Scan(new[]{wave}).Tracks.Single().Artist=="Premier / Deuxième","library displays all artists");
var indexCache=new LibraryIndex();var firstScan=indexCache.Scan(new[]{wave});var secondScan=indexCache.Scan(new[]{wave});
Check(firstScan.Parsed==1&&secondScan.Parsed==0&&ReferenceEquals(firstScan.Tracks[0],secondScan.Tracks[0]),"unchanged audio reuses metadata and cover bytes");
TrackMetadata.Save(wave,root,"New cached title","Test artist","Test album",picture,true);
var changedScan=indexCache.Scan(new[]{wave});
Check(changedScan.Parsed==1&&changedScan.Tracks[0].Title=="New cached title"&&changedScan.Tracks[0].Cover!=null,"metadata changes refresh title and embedded cover");
Check(indexCache.Scan(new[]{wave},true).Parsed==1&&indexCache.Scan(Array.Empty<string>()).Tracks.Count==0,"forced refresh and removed files update the index");
using(var covers=new CoverCache()){
 var image=covers.Acquire(picture);var duplicate=covers.Acquire(picture.ToArray());
 Check(ReferenceEquals(image,duplicate)&&covers.Decodes==1,"identical covers decode once across tracks");
 int cropCalls=0;var crop=covers.Crop(image,bm=>{cropCalls++;return bm;});covers.Crop(duplicate,bm=>{cropCalls++;return bm;});
 Check(cropCalls==1,"cover black-bar scan runs once");
 covers.Limit=0;covers.Release(image);Check(covers.Decodes==1&&image.PixelSize.Width==240,"visible covers stay pinned during cache eviction");covers.Release(duplicate);
 var reloaded=covers.Acquire(picture);Check(covers.Decodes==2,"released cover can be evicted and reloaded safely");covers.Release(reloaded);
}
var audioCacheRoot=Path.Combine(root,"cache-limit-test");Directory.CreateDirectory(audioCacheRoot);
var oldCache=Path.Combine(audioCacheRoot,new string('A',64)+".wav");var keptCache=Path.Combine(audioCacheRoot,new string('B',64)+".wav");var otherFile=Path.Combine(audioCacheRoot,"original.wav");
foreach(var file in new[]{oldCache,keptCache,otherFile}){File.WriteAllBytes(file,new byte[100]);File.SetLastWriteTimeUtc(file,DateTime.UtcNow.AddDays(-40));File.SetLastAccessTimeUtc(file,DateTime.UtcNow.AddDays(-40));}
var backupRoot=Path.Combine(audioCacheRoot,"metadata-backups");Directory.CreateDirectory(backupRoot);var backup=Path.Combine(backupRoot,new string('C',64)+".wav");File.WriteAllBytes(backup,new byte[100]);
AudioCache.Trim(audioCacheRoot,keptCache,100);
Check(!File.Exists(oldCache)&&File.Exists(keptCache)&&File.Exists(otherFile)&&File.Exists(backup),"audio cache limit preserves active file, originals and metadata backups");
var largeGrid=new VirtualCardPanel();int created=0,removed=0;largeGrid.CardRemoved+=_=>removed++;
for(int i=0;i<10000;i++){var position=i;largeGrid.AddCard(()=>{created++;return new Button{Tag=position};});}
largeGrid.Measure(new Size(900,double.PositiveInfinity));largeGrid.Arrange(new Rect(0,0,900,largeGrid.DesiredSize.Height));
Check(largeGrid.Count==10000&&created<=24&&largeGrid.DesiredSize.Height>600000,"large grid realizes only a few rows and retains full scroll extent");
largeGrid.FocusIndex(9999,Avalonia.Input.NavigationMethod.Tab);
Check(largeGrid.Children.OfType<Button>().Any(btn=>(int)btn.Tag! ==9999),"keyboard can reach an unrealized final card");
Check(largeGrid.Destination(2,Avalonia.Input.Key.Down)==6&&largeGrid.Destination(9999,Avalonia.Input.Key.End)==9999,"virtual grid keyboard follows logical card order");
largeGrid.ClearCards();Check(removed==created&&largeGrid.Children.Count==0&&largeGrid.Count==0,"virtual grid releases every realized card on refresh");
var scrollGrid=new VirtualCardPanel();for(int i=0;i<10000;i++){var position=i;scrollGrid.AddCard(()=>new Button{Tag=position});}
var scrollTest=new ScrollViewer{Content=scrollGrid,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled};
var scrollWindow=new Window{Width=900,Height=600,ShowInTaskbar=false,Content=scrollTest};scrollWindow.Show();Avalonia.Threading.Dispatcher.UIThread.RunJobs();
scrollTest.Offset=new Vector(0,scrollTest.Extent.Height-scrollTest.Viewport.Height);Avalonia.Threading.Dispatcher.UIThread.RunJobs();
Check(scrollGrid.Children.OfType<Button>().Any(btn=>(int)btn.Tag! ==9999)&&scrollGrid.Children.Count<30&&!scrollGrid.Children.OfType<Button>().Any(btn=>(int)btn.Tag! ==0),"scrolling reaches final cards and releases offscreen rows");
scrollGrid.FocusIndex(0,Avalonia.Input.NavigationMethod.Tab);Avalonia.Threading.Dispatcher.UIThread.RunJobs();
Check(scrollGrid.Children.OfType<Button>().Any(btn=>(int)btn.Tag! ==0)&&scrollTest.Offset.Y==0,"keyboard navigation scrolls back to the first row");scrollWindow.Close();scrollGrid.ClearCards();
Console.WriteLine("Optimization checks passed.");

Check(TrackSearch.Matches(new(wave,"Été rêvé","Beyoncé","Fête",null),"ete reve")&&TrackSearch.Contains("Électro","electro"),"search ignores accents in track and playlist names");
var persistentRoot=Path.Combine(root,".cache","persistent-index");var persistent=new LibraryIndex(persistentRoot);persistent.Scan(new[]{wave});
var warmIndex=new LibraryIndex(persistentRoot);var warmScan=warmIndex.Scan(new[]{wave});
Check(warmScan.Parsed==0&&warmScan.Tracks.Single().Title=="New cached title","restart restores metadata without reparsing unchanged audio");
var warmTrack=warmScan.Tracks.Single();Check(warmTrack.EmbeddedCover==null&&warmTrack.CoverHash!=null&&warmTrack.Cover!.SequenceEqual(picture),"persistent artwork remains original and loads lazily from disk");
File.Delete(warmTrack.CoverPath!);Check(warmIndex.Scan(new[]{wave}).Parsed==1,"missing cached artwork is recovered from the audio file");
File.WriteAllText(Path.Combine(persistentRoot,"index.json"),"broken JSON");Check(new LibraryIndex(persistentRoot).Scan(new[]{wave}).Parsed==1,"corrupt index safely rebuilds from music");
var rawCacheRoot=Path.Combine(root,".cache","bytes-limit");Directory.CreateDirectory(rawCacheRoot);
for(int i=0;i<5;i++){var cacheFile=Path.Combine(rawCacheRoot,i+".cover");File.WriteAllBytes(cacheFile,new byte[10*1024*1024]);CoverBytes.Read(cacheFile);}
Check(CoverBytes.CachedBytes<=CoverBytes.Limit,"original artwork memory cache remains under 32 MiB");
var discordType=typeof(MusicWindow).Assembly.GetType("Lolimusic.DiscordConnection")!;var rpc=Activator.CreateInstance(discordType,new object[]{"123456789012345678"})!;
var rpcPresence=new DiscordRPC.RichPresence{Details="Test",State="Artist",Timestamps=new DiscordRPC.Timestamps(DateTime.UtcNow)};
discordType.GetMethod("SetPresence")!.Invoke(rpc,new object[]{rpcPresence});discordType.GetMethod("SetPresence")!.Invoke(rpc,new object[]{rpcPresence});
Check((long)discordType.GetField("revision",flags)!.GetValue(rpc)! ==1,"identical Discord activities are deduplicated");
discordType.GetMethod("ClearPresence")!.Invoke(rpc,null);discordType.GetMethod("ClearPresence")!.Invoke(rpc,null);
Check((long)discordType.GetField("revision",flags)!.GetValue(rpc)! ==2,"repeated Discord clear is deduplicated");((IDisposable)rpc).Dispose();
var pickerChoices=Enumerable.Range(0,10000).Select(i=>new TrackChoice(new(wave+i,"Track "+i,"Artist","Album",null))).ToList();pickerChoices[9999].Selected=true;
var pickerList=new ListBox{ItemsSource=pickerChoices,ItemTemplate=new Avalonia.Controls.Templates.FuncDataTemplate<TrackChoice>((choice,_)=>{if(choice==null)return new Border();var check=new CheckBox{Content=choice.Track.Title};check.Bind(CheckBox.IsCheckedProperty,new Avalonia.Data.Binding(nameof(TrackChoice.Selected)){Source=choice,Mode=Avalonia.Data.BindingMode.TwoWay});return check;})};
var pickerTestWindow=new Window{Width=500,Height=400,ShowInTaskbar=false,Content=pickerList};pickerTestWindow.Show();Avalonia.Threading.Dispatcher.UIThread.RunJobs();
Check(pickerList.GetVisualDescendants().OfType<CheckBox>().Count()<40,"adding tracks realizes only visible checkbox rows");
pickerList.ItemsSource=new[]{pickerChoices[9999]};Avalonia.Threading.Dispatcher.UIThread.RunJobs();
Check(pickerList.GetVisualDescendants().OfType<CheckBox>().Single().IsChecked==true,"filtering preserves a selected unrealized track");
pickerChoices[9999].Selected=false;Avalonia.Threading.Dispatcher.UIThread.RunJobs();Check(pickerList.GetVisualDescendants().OfType<CheckBox>().Single().IsChecked==false,"bulk selection updates realized checkbox bindings");pickerTestWindow.Close();
Console.WriteLine("Persistent index, artwork memory, Discord and picker checks passed.");
using(var requestedArtwork=System.Text.Json.JsonDocument.Parse("{\"assets\":{\"large_image\":\"https://files.catbox.moe/test.png\"}}")){
 foreach(var replyJson in new[]{"{}","{\"data\":{\"assets\":{\"large_image\":null}}}","{\"data\":{\"assets\":{\"large_image\":\"\"}}}","{\"data\":{\"assets\":{\"large_image\":\"https://files.catbox.moe/test.png\"}}}"}){using var reply=System.Text.Json.JsonDocument.Parse(replyJson);Check(DiscordArtwork.Missing(requestedArtwork.RootElement,reply.RootElement),"missing, null, empty or unresolved artwork keeps retrying");}
 using var resolved=System.Text.Json.JsonDocument.Parse("{\"data\":{\"assets\":{\"large_image\":\"mp:external/resolved/https/files.catbox.moe/test.png\"}}}");
 Check(!DiscordArtwork.Missing(requestedArtwork.RootElement,resolved.RootElement),"resolved Discord artwork stops retries");
 using var noArtwork=System.Text.Json.JsonDocument.Parse("null");Check(!DiscordArtwork.Missing(noArtwork.RootElement,resolved.RootElement),"cleared activity does not request artwork retry");
}
Check(DiscordArtwork.RetryDelay(1).TotalSeconds==2&&DiscordArtwork.RetryDelay(2).TotalSeconds==5&&DiscordArtwork.RetryDelay(3).TotalSeconds==10&&DiscordArtwork.RetryDelay(20).TotalSeconds==15,"artwork retries start quickly and remain bounded");
SynchronizationContext.SetSynchronizationContext(null);var testPipeName="lolimusic-artwork-test-"+Guid.NewGuid().ToString("N");
using(var server=new System.IO.Pipes.NamedPipeServerStream(testPipeName,System.IO.Pipes.PipeDirection.InOut,1,System.IO.Pipes.PipeTransmissionMode.Byte,System.IO.Pipes.PipeOptions.Asynchronous)){
 Func<int,System.IO.Pipes.NamedPipeClientStream> factory=_=>new(".",testPipeName,System.IO.Pipes.PipeDirection.InOut,System.IO.Pipes.PipeOptions.Asynchronous);
 var testRpc=Activator.CreateInstance(discordType,new object[]{"123456789012345678",factory})!;
 var acceptedImage=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
 using var pipeTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
 async Task<System.Text.Json.JsonDocument> ReadFrame(){var header=new byte[8];await server.ReadExactlyAsync(header,pipeTimeout.Token);var payload=new byte[BitConverter.ToInt32(header,4)];await server.ReadExactlyAsync(payload,pipeTimeout.Token);return System.Text.Json.JsonDocument.Parse(payload);}
 async Task ReplyFrame(object body){var payload=System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(body);var header=new byte[8];BitConverter.GetBytes(1).CopyTo(header,0);BitConverter.GetBytes(payload.Length).CopyTo(header,4);await server.WriteAsync(header,pipeTimeout.Token);await server.WriteAsync(payload,pipeTimeout.Token);}
 var serverTask=Task.Run(async()=>{
  await server.WaitForConnectionAsync(pipeTimeout.Token);using(var handshake=await ReadFrame())await ReplyFrame(new{evt="READY"});
  using(var initial=await ReadFrame())await ReplyFrame(new{nonce=initial.RootElement.GetProperty("nonce").GetString(),data=new{assets=new{large_image=(string?)null}}});
  var retryClock=System.Diagnostics.Stopwatch.StartNew();using(var retry=await ReadFrame()){
   Check(retryClock.Elapsed.TotalSeconds>=1.8&&retryClock.Elapsed.TotalSeconds<5,"missing artwork retries over IPC after about two seconds");
   await ReplyFrame(new{nonce=retry.RootElement.GetProperty("nonce").GetString(),data=new{assets=new{large_image="mp:external/resolved/image"}}});
  }
  acceptedImage.SetResult();var updateClock=System.Diagnostics.Stopwatch.StartNew();using(var updated=await ReadFrame()){
   Check(updated.RootElement.GetProperty("args").GetProperty("activity").GetProperty("details").GetString()=="Next track"&&updateClock.Elapsed.TotalSeconds<2,"new track wakes Discord worker without waiting for retry interval");
   await ReplyFrame(new{nonce=updated.RootElement.GetProperty("nonce").GetString(),data=new{assets=new{large_image="mp:external/resolved/next"}}});
  }
 });
 try{
  discordType.GetMethod("SetPresence")!.Invoke(testRpc,new object[]{new DiscordRPC.RichPresence{Details="First track",Assets=new DiscordRPC.Assets{LargeImageKey="https://files.catbox.moe/first.png"}}});discordType.GetMethod("Initialize")!.Invoke(testRpc,null);
  await acceptedImage.Task.WaitAsync(pipeTimeout.Token);
  discordType.GetMethod("SetPresence")!.Invoke(testRpc,new object[]{new DiscordRPC.RichPresence{Details="Next track",Assets=new DiscordRPC.Assets{LargeImageKey="https://files.catbox.moe/next.png"}}});
  await serverTask;
 }finally{((IDisposable)testRpc).Dispose();}
}

var buttonSettings=new Settings();var linkedTrack=new Track(wave,"Linked","Artist","Album",null);
Check(YouTubeLinks.Buttons(buttonSettings,linkedTrack)==null,"YouTube button stays hidden without a source link");
buttonSettings.YouTubeLinks[wave]="https://youtu.be/bZuzakHtGeg?t=30";
var youtubeButtons=YouTubeLinks.Buttons(buttonSettings,linkedTrack)!;
Check(youtubeButtons.Length==1&&youtubeButtons[0].Label=="Écouter sur YouTube"&&youtubeButtons[0].Url=="https://www.youtube.com/watch?v=bZuzakHtGeg","YouTube button opens the saved video's canonical URL");
Check(YouTubeLinks.Normalize("https://files.catbox.moe/image.png")==null&&YouTubeLinks.Normalize("https://youtube.com.evil.example/watch?v=bZuzakHtGeg")==null&&YouTubeLinks.Normalize("https://youtube.com/watch?v=bad")==null,"image URLs, spoofed hosts and invalid video IDs cannot become listening buttons");
Check(YouTubeLinks.Normalize("https://i.ytimg.com/vi/bZuzakHtGeg/hqdefault.jpg")==youtubeButtons[0].Url,"known YouTube thumbnail preserves the exact source video");
var buttonPresence=new DiscordRPC.RichPresence{Details="Linked",Buttons=youtubeButtons};
using(var buttonPayload=System.Text.Json.JsonDocument.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(buttonPresence)))Check(buttonPayload.RootElement.GetProperty("buttons")[0].GetProperty("url").GetString()==youtubeButtons[0].Url,"Discord payload includes the clickable video URL");
Check(YouTubeLinks.Buttons(buttonSettings,linkedTrack with{Path=wave+"other"})==null,"switching to an unlinked song removes the YouTube button");
var movedButtonPath=wave+"renamed";LibraryReferences.Apply(buttonSettings,new Dictionary<string,string>{{wave,movedButtonPath}});
Check(buttonSettings.YouTubeLinks.ContainsKey(movedButtonPath)&&!buttonSettings.YouTubeLinks.ContainsKey(wave),"YouTube source link follows renamed music files");
buttonSettings.YouTubeLinks.Remove(movedButtonPath);Check(YouTubeLinks.Buttons(buttonSettings,linkedTrack with{Path=movedButtonPath})==null,"clearing a source link hides the button again");
Console.WriteLine("Conditional YouTube button checks passed.");
Check(NetEaseLinks.Normalize("https://music.163.com/#/song?id=123456")=="https://music.163.com/#/song?id=123456"&&NetEaseLinks.Normalize("https://y.music.163.com/m/song?id=123456&userid=777")=="https://music.163.com/#/song?id=123456","NetEase desktop and mobile song links normalize to the same song");
Check(NetEaseLinks.Normalize("https://music.163.com/#/playlist?id=123456")==null&&NetEaseLinks.Normalize("https://music.163.com.evil.example/song?id=123456")==null&&NetEaseLinks.Normalize("https://music.163.com/song?id=0")==null,"NetEase buttons reject playlists, spoofed hosts and invalid song IDs");
var neteaseSettings=new Settings();neteaseSettings.NetEaseLinks[wave]="https://music.163.com/song?id=123456";
Check(YouTubeLinks.Buttons(neteaseSettings,linkedTrack) is [{Label:"Écouter sur NetEase"}],"NetEase-only song displays exactly one listening button");
neteaseSettings.YouTubeLinks[wave]="https://youtu.be/bZuzakHtGeg";
Check(YouTubeLinks.Buttons(neteaseSettings,linkedTrack) is [{Label:"Écouter sur NetEase"}],"legacy dual links display a single button in the same slot");
LibraryReferences.Apply(neteaseSettings,new Dictionary<string,string>{{wave,movedButtonPath}});Check(neteaseSettings.NetEaseLinks.ContainsKey(movedButtonPath)&&!neteaseSettings.NetEaseLinks.ContainsKey(wave),"NetEase link follows renamed or moved music files");
var linksRoundTrip=System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(neteaseSettings))!;Check(linksRoundTrip.NetEaseLinks[movedButtonPath]==neteaseSettings.NetEaseLinks[movedButtonPath],"NetEase source links persist in settings");
YouTubeLinks.Set(neteaseSettings,movedButtonPath,"https://www.youtube.com/watch?v=bZuzakHtGeg",false);Check(!neteaseSettings.NetEaseLinks.ContainsKey(movedButtonPath)&&neteaseSettings.YouTubeLinks.ContainsKey(movedButtonPath),"choosing YouTube replaces NetEase");
YouTubeLinks.Set(neteaseSettings,movedButtonPath,"https://music.163.com/#/song?id=123456",true);Check(!neteaseSettings.YouTubeLinks.ContainsKey(movedButtonPath)&&YouTubeLinks.Buttons(neteaseSettings,linkedTrack with{Path=movedButtonPath}) is [{Label:"Écouter sur NetEase"}],"choosing NetEase replaces YouTube in the same Discord button slot");
var actualNcm=Directory.EnumerateFiles(MusicWindow.MusicDirectory,"*.ncm",SearchOption.AllDirectories).FirstOrDefault(p=>!p.Contains(Path.DirectorySeparatorChar+".cache"+Path.DirectorySeparatorChar));
if(actualNcm!=null){
 var originalNcmHash=System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(actualNcm));var ncmOutput=Path.Combine(root,"ncm-converted");var converted=NcmImporter.Import(actualNcm,ncmOutput);
 using(var convertedTags=TagLib.File.Create(converted.Path))Check(convertedTags.Properties.Duration.TotalSeconds>1&&!string.IsNullOrWhiteSpace(convertedTags.Tag.Title)&&convertedTags.Tag.Performers.Length>0&&convertedTags.Tag.Pictures.Length>0,"NCM import restores playable audio, title, artists and embedded cover");
 using(var convertedAudio=new NAudio.Wave.AudioFileReader(converted.Path))Check(convertedAudio.Read(new byte[8192],0,8192)>0,"converted NCM decodes through the app's audio reader");
 Check(originalNcmHash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(actualNcm))),"NCM import leaves the original container unchanged");
 Check(NetEaseLinks.Normalize(converted.NetEaseUrl)!=null,"NCM metadata restores the exact NetEase song link");
 var convertedTime=File.GetLastWriteTimeUtc(converted.Path);var repeated=NcmImporter.Import(actualNcm,ncmOutput);Check(repeated==converted&&File.GetLastWriteTimeUtc(converted.Path)==convertedTime&&Directory.GetFiles(ncmOutput).Length==1,"repeated NCM import reuses the converted track without duplicates");
 File.Delete(converted.Path);NcmImporter.Import(actualNcm,ncmOutput);Check(!File.Exists(converted.Path),"automatic scan does not resurrect a removed converted track");
}
var invalidNcm=Path.Combine(root,"invalid.ncm");File.WriteAllBytes(invalidNcm,"CTENFDAM\0\0\xff\xff\xff\xff"u8.ToArray());bool ncmRejected=false;try{NcmImporter.Import(invalidNcm,Path.Combine(root,"bad-ncm-output"));}catch(InvalidDataException){ncmRejected=true;}Check(ncmRejected,"NCM importer rejects oversized or truncated blocks without creating audio");
Console.WriteLine("NCM import checks passed.");

sealed class MemorySettings:Settings {
 public int Saves{get;private set;}
 public override void Save()=>Saves++;
}
