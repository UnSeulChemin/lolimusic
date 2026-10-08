using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using DiscordRPC;
using Button = Avalonia.Controls.Button;
using NAudio.Wave;
using System.Text.Json;
namespace Lolimusic;
internal static class Program {
 [STAThread] public static void Main(string[] args) {
  if(args.Contains("--quit")){InstanceBridge.Send("quit");return;}
  using var instance=new Mutex(true,"Local\\"+InstanceBridge.Name,out var first);
  if(!first){InstanceBridge.Send("open");return;}
  try{AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);}finally{instance.ReleaseMutex();}
 }
}
public class App : Application {
 public override void Initialize() { RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark; var theme=new FluentTheme();theme.Palettes[Avalonia.Styling.ThemeVariant.Dark]=new ColorPaletteResources{Accent=Color.Parse("#7B2CFF"),RegionColor=Color.Parse("#241A30")};Styles.Add(theme); }
 public override void OnFrameworkInitializationCompleted() { if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d) d.MainWindow = new MusicWindow(); base.OnFrameworkInitializationCompleted(); }
}
public record Track(string Path, string Title, string Artist, string Album, byte[]? Cover);
public class Playlist { public string Name {get;set;}=""; public List<string> Paths {get;set;}=[]; }
public class Settings {
 public double WindowWidth {get;set;}=1120;public double WindowHeight {get;set;}=790;public bool WindowMaximized {get;set;}
 public string? LastTrack {get;set;} public double LastPosition {get;set;} public List<string> LastQueue {get;set;}=[];
 public Dictionary<string,string> FileIdentities {get;set;}=new(StringComparer.OrdinalIgnoreCase);
 public Dictionary<string,string> DiscordCovers {get;set;}=[];
 public Dictionary<string,string> UploadedCovers {get;set;}=[]; public bool AutoDiscordCovers {get;set;}=false;
 public List<Playlist> Playlists {get;set;}=[];
 public List<string> Folders {get;set;} = []; public List<string> Favorites {get;set;} = []; public string DiscordId {get;set;} = ""; public double Volume {get;set;} = .7;
 static string Location => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lolimusic", "settings.json");
 public static Settings Load() { foreach(var path in new[]{Location,Location+".bak"})try { var settings=JsonSerializer.Deserialize<Settings>(File.ReadAllText(path));if(settings!=null){settings.FileIdentities=new(settings.FileIdentities,StringComparer.OrdinalIgnoreCase);return settings;} } catch {} return new(); }
 public virtual void Save() { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Location)!);var temporary=Location+".tmp";File.WriteAllText(temporary,JsonSerializer.Serialize(this));if(File.Exists(Location))File.Copy(Location,Location+".bak",true);File.Move(temporary,Location,true); }
}
public partial class MusicWindow : Window {
 readonly TextBlock discordStatus=Text("Discord désactivé",11,"#BFB2D3");
 readonly DispatcherTimer libraryTimer=new(){Interval=TimeSpan.FromMilliseconds(900)};FileSystemWatcher? libraryWatcher;bool reloadPending,closed;
 readonly Settings prefs; List<Track> tracks = [], queue = []; Track? current;
 AudioFileReader? audio; WaveOutEvent? output; DiscordConnection? discord;
 readonly StackPanel libraryActions = new(){Orientation=Orientation.Horizontal,Spacing=6,HorizontalAlignment=HorizontalAlignment.Right}; readonly WrapPanel cards = new(); readonly List<Bitmap> images = [];
 readonly TextBlock status = Text("Ta musique. Ton univers.",13,"#BFB2D3"), title = Text("Rien en lecture",16), subtitle = Text("Importe ta bibliothèque",12,"#BFB2D3"), time = Text("0:00",12), end = Text("0:00",12);
 readonly Image artwork = new() {Width=64,Height=64,Stretch=Stretch.UniformToFill};
 readonly Slider seek = new(){Minimum=0,Maximum=1}; readonly TextBox search = new(){PlaceholderText="Rechercher ta musique…",Width=245}; readonly Button play = IconButton("play","Lire / pause",()=>{});
 readonly DispatcherTimer timer = new(){Interval=TimeSpan.FromMilliseconds(500)};
 static readonly System.Net.Http.HttpClient coverClient=CreateCoverClient();
 static System.Net.Http.HttpClient CreateCoverClient(){var client=new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler{SslProtocols=System.Security.Authentication.SslProtocols.Tls12}){Timeout=TimeSpan.FromSeconds(30)};client.DefaultRequestHeaders.UserAgent.ParseAdd("lolimusic/1.0");return client;}
 readonly HashSet<string> uploadingCovers=[];
 string section="Playlists"; Playlist? selectedPlaylist; bool updating,shuffle,repeat,busy,descending,sessionHasPlayed; int playbackRequest;
 static readonly HashSet<string> MusicExtensions=new(StringComparer.OrdinalIgnoreCase){".mp3",".flac",".wav",".m4a",".aac",".wma",".aiff"};
 public static string MusicDirectory {get {var directory=new DirectoryInfo(AppContext.BaseDirectory);for(var parent=directory;parent!=null;parent=parent.Parent)if(File.Exists(System.IO.Path.Combine(parent.FullName,"loli","lolimusic.csproj")))return System.IO.Path.Combine(parent.FullName,"music");return System.IO.Path.Combine(directory.FullName,"music");}}
 public MusicWindow(Settings? settings=null) {
  prefs=settings??Settings.Load();
  Title="lolimusic"; Width=Math.Clamp(prefs.WindowWidth,900,3840); Height=Math.Clamp(prefs.WindowHeight,540,2160); MinWidth=900; MinHeight=540;
  using(var iconStream=Avalonia.Platform.AssetLoader.Open(new Uri("avares://lolimusic/Assets/lolimusic.png")))Icon=new WindowIcon(iconStream);
  Background=new LinearGradientBrush {StartPoint=new RelativePoint(0,0,RelativeUnit.Relative),EndPoint=new RelativePoint(1,1,RelativeUnit.Relative),GradientStops=new GradientStops{new(Color.Parse("#241536"),0),new(Color.Parse("#17121F"),.6),new(Color.Parse("#24182E"),1)}};
  var root=new Grid{RowDefinitions=new RowDefinitions("Auto,Auto,*,Auto"),Margin=new Thickness(24)};
  WindowDecorations=Avalonia.Controls.WindowDecorations.None;
  var header=new Grid{ColumnDefinitions=new ColumnDefinitions("120,*,120"),Height=38};
  var tools=new StackPanel{Orientation=Orientation.Horizontal,Spacing=2,VerticalAlignment=VerticalAlignment.Center};
  tools.Children.Add(IconButton("library","Importer un dossier de musique",async()=>await Import()));
  var searchPopup=new Flyout{Content=search};var searchButton=IconButton("search","Rechercher",()=>{});searchButton.Click+=(_,_)=>{searchPopup.ShowAt(searchButton);search.Focus();};tools.Children.Add(searchButton);
  var sortButton=IconButton("sort","Inverser l’ordre alphabétique",()=>{});sortButton.Click+=(_,_)=>{descending=!descending;sortButton.Foreground=Brush(descending?"#C59AFF":"#EEE5FA");Render();};tools.Children.Add(sortButton);header.Children.Add(tools);
  var tabs=new StackPanel{Orientation=Orientation.Horizontal,Spacing=4,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
  var tabButtons=new Dictionary<string,Button>();
  foreach(var (name,icon) in new[]{("Playlists","library"),("Titres","music"),("Favoris","heart")}){
   var label=new StackPanel{Orientation=Orientation.Horizontal,Spacing=7};label.Children.Add(PlayerIcon(icon));label.Children.Add(Text(name,12));
   var tab=new Button{Content=label,Padding=new Thickness(12,7),MinHeight=0,CornerRadius=new CornerRadius(6),Background=Brush(name==section?"#6731B6":"#00000000")};
   tab.Click+=(_,_)=>{section=name;selectedPlaylist=null;reordering=false;ClearTrackSelection();foreach(var entry in tabButtons)entry.Value.Background=Brush(entry.Key==section?"#6731B6":"#00000000");Render();};tabButtons.Add(name,tab);tabs.Children.Add(tab);
  }
  Add(header,tabs,0,1);
  var windowButtons=new StackPanel{Orientation=Orientation.Horizontal,Spacing=2,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center};
  var menuPanel=new StackPanel{Spacing=6};menuPanel.Children.Add(Text("lolimusic",14));var menuPopup=new Flyout{Content=menuPanel};
  menuPanel.Children.Add(Button("Importer un dossier",async()=>{menuPopup.Hide();await Import();}));menuPanel.Children.Add(Button("Discord",async()=>{menuPopup.Hide();await DiscordSettings();}));menuPanel.Children.Add(Button("Actualiser la bibliothèque",async()=>{menuPopup.Hide();await Reload();}));
  AddFeatureMenu(menuPanel,menuPopup); var menuButton=IconButton("menu","Menu lolimusic",()=>{});menuButton.Click+=(_,_)=>menuPopup.ShowAt(menuButton);windowButtons.Children.Add(menuButton);
  windowButtons.Children.Add(IconButton("minimize","Réduire",()=>WindowState=WindowState.Minimized));windowButtons.Children.Add(IconButton("maximize","Agrandir / restaurer",()=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized));windowButtons.Children.Add(IconButton("close","Fermer",Close));Add(header,windowButtons,0,2);
  var headerBar=new Border{Child=header,Background=Brush("#281938"),Padding=new Thickness(8,2),Margin=new Thickness(-24,-24,-24,12)};
  headerBar.PointerPressed+=(_,e)=>{if(e.GetCurrentPoint(this).Properties.IsLeftButtonPressed&&(e.Source is Border||e.Source is Grid)){if(e.ClickCount==2)WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;else BeginMoveDrag(e);}};root.Children.Add(headerBar);
  status.FontSize=11;
  var library=new Grid{RowDefinitions=new RowDefinitions("Auto,*")}; var libraryHeader=new Grid{ColumnDefinitions=new ColumnDefinitions("*,Auto")};var notices=new StackPanel{Spacing=3};notices.Children.Add(status);notices.Children.Add(discordStatus);libraryHeader.Children.Add(notices);Add(libraryHeader,libraryActions,0,1);library.Children.Add(libraryHeader); Add(library,new ScrollViewer{Content=cards,Margin=new Thickness(0,18,0,12),HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled},1); Add(root,library,2);
  var player=new Grid{ColumnDefinitions=new ColumnDefinitions("64,*,Auto")}; player.Children.Add(new Border{Child=artwork,Width=64,Height=64,CornerRadius=new CornerRadius(7),Background=Brush("#382447"),ClipToBounds=true});
  title.FontSize=14;title.FontWeight=FontWeight.SemiBold;title.HorizontalAlignment=HorizontalAlignment.Center;subtitle.HorizontalAlignment=HorizontalAlignment.Center;
  seek.Height=24;seek.MinHeight=24;
  var info=new StackPanel{Margin=new Thickness(16,0),Spacing=2,VerticalAlignment=VerticalAlignment.Center};info.Children.Add(title);info.Children.Add(subtitle);
  var timeline=new Grid{ColumnDefinitions=new ColumnDefinitions("Auto,*,Auto")};time.Margin=new Thickness(0,0,8,0);end.Margin=new Thickness(8,0,0,0);timeline.Children.Add(time);Add(timeline,seek,0,1);Add(timeline,end,0,2);info.Children.Add(timeline);Add(player,info,0,1);
  var controls=new StackPanel{Spacing=4};var options=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
  var random=IconButton("shuffle","Lecture aléatoire",()=>{});random.Click+=(_,_)=>{shuffle=!shuffle;random.Foreground=Brush(shuffle?"#C59AFF":"#EEE5FA");RefreshQueue();};options.Children.Add(random);
  var loop=IconButton("repeat","Répéter le titre",()=>{});loop.Click+=(_,_)=>{repeat=!repeat;loop.Foreground=Brush(repeat?"#C59AFF":"#EEE5FA");RefreshQueue();};options.Children.Add(loop);
  var volume=new Slider{Minimum=0,Maximum=1,Value=prefs.Volume,Width=100};volume.ValueChanged+=(_,_)=>{prefs.Volume=volume.Value;if(audio!=null)audio.Volume=(float)volume.Value;};
  var popup=new Avalonia.Controls.Flyout{Content=volume};var speaker=IconButton("volume","Volume",()=>{});speaker.Click+=(_,_)=>popup.ShowAt(speaker);options.Children.Add(speaker);options.Children.Insert(0,IconButton("library","File de lecture",ShowQueue));controls.Children.Add(options);
  var transport=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
  transport.Children.Add(IconButton("heart","Ajouter / retirer des favoris",()=>{if(current==null)return;if(!prefs.Favorites.Remove(current.Path))prefs.Favorites.Add(current.Path);prefs.Save();Render();}));
  transport.Children.Add(IconButton("previous","Titre précédent",()=>Next(-1)));play.Click+=(_,_)=>Toggle();transport.Children.Add(play);transport.Children.Add(IconButton("next","Titre suivant",()=>Next(1)));controls.Children.Add(transport);Add(player,controls,0,2);
  Add(root,new Border{Child=player,Background=Brush("#201628"),BorderBrush=Brush("#49305F"),BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(12,10),Margin=new Thickness(-24,12,-24,-24)},3);Content=ResizeFrame(root);
  Resources["SystemAccentColor"]=Color.Parse("#7B2CFF");Resources["SystemAccentColorLight1"]=Color.Parse("#A84DFF");Resources["SystemAccentColorDark1"]=Color.Parse("#6A1FFF");
  search.TextChanged+=(_,_)=>Render();seek.ValueChanged+=(_,_)=>{if(!updating&&audio!=null){audio.CurrentTime=TimeSpan.FromSeconds(seek.Value);Presence();}};
  timer.Tick+=(_,_)=>{if(audio==null)return;updating=true;seek.Maximum=Math.Max(1,audio.TotalTime.TotalSeconds);seek.Value=audio.CurrentTime.TotalSeconds;time.Text=Format(audio.CurrentTime);updating=false;if(output?.PlaybackState==PlaybackState.Stopped&&audio.CurrentTime.TotalSeconds>=audio.TotalTime.TotalSeconds-.5){if(repeat&&current!=null)Start(current);else Next(1);}};timer.Start();
  ConfigureCardNavigation();ConfigureFileDrop();ConfigureDesktopFeatures(); libraryTimer.Tick+=async(_,_)=>{libraryTimer.Stop();await Reload();}; bool libraryOpened=false;Opened+=async(_,_)=>{if(libraryOpened)return;libraryOpened=true;Connect();try{await CopyIntoLibrary(prefs.Folders.ToList());}catch(Exception e){status.Text="Copie impossible : "+e.Message;}await Reload();WatchLibrary();await RestoreSession();};Closed+=(_,_)=>{RememberSession();tray?.Dispose();ReleaseMediaKeys();closed=true;libraryTimer.Stop();libraryWatcher?.Dispose();playbackRequest++;timer.Stop();output?.Dispose();audio?.Dispose();discord?.Dispose();prefs.Save();foreach(var image in images)image.Dispose();};
 }
 Control ResizeFrame(Control content){
  CanResize=true;var frame=new Grid();frame.Children.Add(content);var grips=new List<Border>();
  void Grip(WindowEdge edge,HorizontalAlignment horizontal,VerticalAlignment vertical,double width,double height,Avalonia.Input.StandardCursorType cursor){
   var grip=new Border{Background=Brushes.Transparent,HorizontalAlignment=horizontal,VerticalAlignment=vertical,Cursor=new Avalonia.Input.Cursor(cursor)};
   if(width>0)grip.Width=width;if(height>0)grip.Height=height;
   grip.PointerPressed+=(_,e)=>{if(WindowState==WindowState.Normal&&CanResize&&e.GetCurrentPoint(this).Properties.IsLeftButtonPressed){e.Handled=true;BeginResizeDrag(edge,e);}};grips.Add(grip);frame.Children.Add(grip);
  }
  Grip(WindowEdge.West,HorizontalAlignment.Left,VerticalAlignment.Stretch,5,0,Avalonia.Input.StandardCursorType.SizeWestEast);
  Grip(WindowEdge.East,HorizontalAlignment.Right,VerticalAlignment.Stretch,5,0,Avalonia.Input.StandardCursorType.SizeWestEast);
  Grip(WindowEdge.North,HorizontalAlignment.Stretch,VerticalAlignment.Top,0,5,Avalonia.Input.StandardCursorType.SizeNorthSouth);
  Grip(WindowEdge.South,HorizontalAlignment.Stretch,VerticalAlignment.Bottom,0,5,Avalonia.Input.StandardCursorType.SizeNorthSouth);
  Grip(WindowEdge.NorthWest,HorizontalAlignment.Left,VerticalAlignment.Top,10,10,Avalonia.Input.StandardCursorType.TopLeftCorner);
  Grip(WindowEdge.NorthEast,HorizontalAlignment.Right,VerticalAlignment.Top,10,10,Avalonia.Input.StandardCursorType.TopRightCorner);
  Grip(WindowEdge.SouthWest,HorizontalAlignment.Left,VerticalAlignment.Bottom,10,10,Avalonia.Input.StandardCursorType.BottomLeftCorner);
  Grip(WindowEdge.SouthEast,HorizontalAlignment.Right,VerticalAlignment.Bottom,10,10,Avalonia.Input.StandardCursorType.BottomRightCorner);
  PropertyChanged+=(_,e)=>{if(e.Property==WindowStateProperty||e.Property==CanResizeProperty)foreach(var grip in grips)grip.IsVisible=WindowState==WindowState.Normal&&CanResize;};return frame;
 }
 static IBrush Brush(string c)=>new SolidColorBrush(Color.Parse(c));
 static TextBlock Text(string t,double size=14,string color="#F4EEFC")=>new(){Text=t,FontSize=size,Foreground=Brush(color),VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis};
 static Button Button(string t,System.Action a){var b=new Button{Content=t,Padding=new Thickness(12,9),CornerRadius=new CornerRadius(8),Margin=new Thickness(3,0),Background=Brush("#6430A6")};b.Click+=(_,_)=>a();return b;}
 static Control PlayerIcon(string name){
  string data=name switch{
   "plus"=>"M 12,3 L 12,21 M 3,12 L 21,12",
   "back"=>"M 14,4 L 6,12 L 14,20 M 6,12 L 22,12",
   "library"=>"M 3,4 L 21,4 L 21,20 L 3,20 Z M 8,4 L 8,20",
   "search"=>"M 16,10 A 6,6 0 1 1 4,10 A 6,6 0 1 1 16,10 M 15,15 L 21,21",
   "sort"=>"M 5,3 L 5,21 M 2,18 L 5,21 L 8,18 M 11,5 L 22,5 M 11,10 L 19,10 M 11,15 L 16,15",
   "album"=>"M 22,12 A 10,10 0 1 1 2,12 A 10,10 0 1 1 22,12 M 15,12 A 3,3 0 1 1 9,12 A 3,3 0 1 1 15,12",
   "artists"=>"M 15,6 A 3,3 0 1 1 9,6 A 3,3 0 1 1 15,6 M 6,21 L 6,17 Q 12,9 18,17 L 18,21 M 4,6 A 2,2 0 1 0 4,10 M 2,20 L 2,16 L 5,13 M 20,6 A 2,2 0 1 1 20,10 M 22,20 L 22,16 L 19,13",
   "music"=>"M 8,17 L 8,4 L 20,2 L 20,15 M 8,8 L 20,6 M 8,17 C 8,22 1,22 1,18 C 1,15 8,14 8,17 M 20,15 C 20,20 13,20 13,16 C 13,13 20,12 20,15",
   "menu"=>"M 4,6 L 20,6 M 4,12 L 20,12 M 4,18 L 20,18",
   "minimize"=>"M 4,11 L 20,11 L 20,13 L 4,13 Z",
   "maximize"=>"M 5,5 L 19,5 L 19,19 L 5,19 Z",
   "close"=>"M 6,6 L 18,18 M 18,6 L 6,18",
   "play"=>"M 6,3 L 18,12 L 6,21 Z",
   "pause"=>"M 6,4 L 10,4 L 10,20 L 6,20 Z M 14,4 L 18,4 L 18,20 L 14,20 Z",
   "previous"=>"M 4,4 L 7,4 L 7,20 L 4,20 Z M 20,4 L 8,12 L 20,20 Z",
   "next"=>"M 17,4 L 20,4 L 20,20 L 17,20 Z M 4,4 L 16,12 L 4,20 Z",
   "shuffle"=>"M 3,6 L 7,6 L 17,18 L 21,18 M 17,14 L 21,18 L 17,22 M 3,18 L 7,18 L 17,6 L 21,6 M 17,2 L 21,6 L 17,10",
   "repeat"=>"M 4,10 L 4,6 L 20,6 M 16,2 L 20,6 L 16,10 M 20,14 L 20,18 L 4,18 M 8,14 L 4,18 L 8,22",
   "volume"=>"M 3,9 L 7,9 L 12,4 L 12,20 L 7,15 L 3,15 Z M 16,8 Q 20,12 16,16 M 19,4 Q 26,12 19,20",
   _=>"M 12,20 L 3,11 C -2,3 8,0 12,7 C 16,0 26,3 21,11 Z"};
  bool filled=name is "play" or "pause" or "previous" or "next" or "minimize";
  var path=new Avalonia.Controls.Shapes.Path{Data=Geometry.Parse(data),Width=24,Height=24,Stretch=Stretch.None,StrokeThickness=1.7,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
  path.Bind(filled?Avalonia.Controls.Shapes.Shape.FillProperty:Avalonia.Controls.Shapes.Shape.StrokeProperty,new Avalonia.Data.Binding("Foreground"){RelativeSource=new Avalonia.Data.RelativeSource(Avalonia.Data.RelativeSourceMode.FindAncestor){AncestorType=typeof(Button)}});return new Viewbox{Width=16,Height=16,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Child=path};
 }
 static Button IconButton(string icon,string tooltip,System.Action action){var b=new Button{Content=PlayerIcon(icon),Width=28,Height=28,MinHeight=0,Padding=new Thickness(6),Background=Brushes.Transparent,Foreground=Brush("#EEE5FA"),CornerRadius=new CornerRadius(5),HorizontalContentAlignment=HorizontalAlignment.Center,VerticalContentAlignment=VerticalAlignment.Center};ToolTip.SetTip(b,tooltip);b.Click+=(_,_)=>action();return b;}
 static void Add(Grid grid,Control control,int row,int column=0){Grid.SetRow(control,row);Grid.SetColumn(control,column);grid.Children.Add(control);}
 static string Format(TimeSpan t)=>$"{(int)t.TotalMinutes}:{t.Seconds:00}";
 Bitmap? Cover(Track t){try{if(t.Cover==null)return null;using var stream=new MemoryStream(t.Cover);var b=Bitmap.DecodeToWidth(stream,240);images.Add(b);return b;}catch{return null;}}
 async Task CopyIntoLibrary(List<string> folders){
  var root=MusicDirectory;Directory.CreateDirectory(root);
  var mappings=await Task.Run(()=>{var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);foreach(var source in LibraryImport.Files(folders,MusicExtensions)){
   if(System.IO.Path.GetFullPath(source).StartsWith(root+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))continue;
   using var stream=File.OpenRead(source);var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));var destination=System.IO.Path.Combine(root,System.IO.Path.GetFileNameWithoutExtension(source)+"_"+hash[..12]+System.IO.Path.GetExtension(source));if(!File.Exists(destination))File.Copy(source,destination);result[source]=destination;
  }return result;});
  LibraryReferences.Apply(prefs,mappings);prefs.Folders=[root];prefs.Save();
 }
 async Task Import(){var selected=await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions{Title="Copier des musiques dans la bibliothèque",AllowMultiple=true});if(selected.Count==0)return;try{status.Text="Copie des musiques…";await CopyIntoLibrary(prefs.Folders.Concat(selected.Select(f=>f.TryGetLocalPath()).OfType<string>()).ToList());await Reload();}catch(Exception e){status.Text="Import impossible : "+e.Message;}}
 public static async Task<string> PlayableFile(string path){
  try{using var reader=new AudioFileReader(path);var buffer=new byte[4096];if(reader.Read(buffer,0,buffer.Length)>0)return path;}catch{}
  var ffmpeg=System.IO.Path.Combine(AppContext.BaseDirectory,"tools","ffmpeg.exe");if(!File.Exists(ffmpeg))throw new InvalidOperationException("Décodeur de secours manquant : tools/ffmpeg.exe");
  var key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path+File.GetLastWriteTimeUtc(path).Ticks+new FileInfo(path).Length)));
  var cache=System.IO.Path.Combine(MusicDirectory,".cache");Directory.CreateDirectory(cache);var target=System.IO.Path.Combine(cache,key+".wav");if(File.Exists(target))return target;var temporary=target+"."+Guid.NewGuid().ToString("N")+".wav";
  var start=new System.Diagnostics.ProcessStartInfo(ffmpeg){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true};foreach(var arg in new[]{"-v","error","-nostdin","-i",path,"-vn","-c:a","pcm_s16le",temporary})start.ArgumentList.Add(arg);
  try{using var process=System.Diagnostics.Process.Start(start)??throw new IOException("Impossible de lancer le décodeur");var errors=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();var message=await errors;if(process.ExitCode!=0)throw new IOException(message);using(var reader=new AudioFileReader(temporary)){if(reader.Read(new byte[4096],0,4096)==0)throw new IOException("Aucun son décodable dans ce fichier.");}File.Move(temporary,target,true);return target;}finally{if(File.Exists(temporary))File.Delete(temporary);}
 }
 void WatchLibrary(){
  Directory.CreateDirectory(MusicDirectory);libraryWatcher=new FileSystemWatcher(MusicDirectory){IncludeSubdirectories=true,NotifyFilter=NotifyFilters.FileName|NotifyFilters.DirectoryName|NotifyFilters.LastWrite|NotifyFilters.Size};
  void Schedule(string path){if(path.Contains(System.IO.Path.DirectorySeparatorChar+".cache"+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||closed)return;Dispatcher.UIThread.Post(()=>{if(closed)return;libraryTimer.Stop();libraryTimer.Start();});}
  libraryWatcher.Created+=(_,e)=>Schedule(e.FullPath);libraryWatcher.Changed+=(_,e)=>Schedule(e.FullPath);libraryWatcher.Deleted+=(_,e)=>Schedule(e.FullPath);libraryWatcher.Renamed+=(_,e)=>Schedule(e.FullPath);libraryWatcher.Error+=(_,_)=>Schedule(MusicDirectory);libraryWatcher.EnableRaisingEvents=true;
 }
 async Task Reload(){if(closed)return;if(busy){reloadPending=true;return;}busy=true;status.Text="Lecture des fichiers et des pochettes…";try{
  var result=await Task.Run(()=>{
   var list=new List<Track>();var ids=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);int unreadable=0;
   foreach(var folder in prefs.Folders.ToList().Where(Directory.Exists))foreach(var path in Directory.EnumerateFiles(folder,"*",new EnumerationOptions{RecurseSubdirectories=true,IgnoreInaccessible=true}).Where(p=>MusicExtensions.Contains(System.IO.Path.GetExtension(p))&&!p.StartsWith(System.IO.Path.Combine(MusicDirectory,".cache")+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))){
    try{ids[path]=LibraryReferences.Identity(path);}catch{}
    try{using var f=TagLib.File.Create(path);list.Add(new(path,string.IsNullOrWhiteSpace(f.Tag.Title)?System.IO.Path.GetFileNameWithoutExtension(path):f.Tag.Title,string.IsNullOrWhiteSpace(f.Tag.FirstPerformer)?"Artiste inconnu":f.Tag.FirstPerformer,string.IsNullOrWhiteSpace(f.Tag.Album)?"Sans album":f.Tag.Album,f.Tag.Pictures.FirstOrDefault()?.Data.Data));}catch{unreadable++;}
   }
   return (Tracks:list.DistinctBy(t=>t.Path,StringComparer.OrdinalIgnoreCase).OrderBy(t=>t.Album).ThenBy(t=>t.Title).ToList(),Ids:ids,Unreadable:unreadable);
  });if(closed)return;
  tracks=result.Tracks;var mappings=LibraryReferences.Reconcile(prefs,result.Ids);prefs.Save();
  string Map(string path)=>mappings.GetValueOrDefault(path,path);var remappedSelection=selectedTracks.Select(Map).ToList();selectedTracks.Clear();selectedTracks.UnionWith(remappedSelection);
  queue=queue.Select(t=>tracks.FirstOrDefault(f=>f.Path.Equals(Map(t.Path),StringComparison.OrdinalIgnoreCase))??t).ToList();
  if(current!=null)current=tracks.FirstOrDefault(t=>t.Path.Equals(Map(current.Path),StringComparison.OrdinalIgnoreCase))??current;
  RefreshQueue();Render();if(current!=null){title.Text=current.Title;subtitle.Text=$"{current.Artist} · {current.Album}";Presence();}
  if(result.Unreadable>0)status.Text+=$" · {result.Unreadable} fichier(s) illisible(s)";
 }catch(Exception e){status.Text="Actualisation impossible : "+e.Message;}finally{busy=false;if(reloadPending&&!closed){reloadPending=false;libraryTimer.Start();}}}
 void Render(){selectedTracks.IntersectWith(tracks.Select(t=>t.Path));CancelCardNavigation();if(current!=null){var refreshed=tracks.FirstOrDefault(t=>t.Path.Equals(current.Path,StringComparison.OrdinalIgnoreCase));if(refreshed!=null&&!ReferenceEquals(refreshed,current)){current=refreshed;title.Text=current.Title;subtitle.Text=$"{current.Artist} · {current.Album}";Presence();}}libraryActions.Children.Clear();cards.Children.Clear();artwork.Source=null;foreach(var image in images)image.Dispose();images.Clear();if(current!=null)artwork.Source=Cover(current);
  if(section=="Playlists") { RenderPlaylists();return; }
  var playlistPaths=prefs.Playlists.SelectMany(p=>p.Paths).ToHashSet(StringComparer.OrdinalIgnoreCase);
  var filtered=tracks.Where(t=>section!="Titres"||!playlistPaths.Contains(t.Path)).Where(t=>$"{t.Title} {t.Artist} {t.Album}".Contains(search.Text??"",StringComparison.OrdinalIgnoreCase)).Where(t=>section!="Favoris"||prefs.Favorites.Contains(t.Path)).ToList();filtered=filtered.OrderBy(t=>section=="Albums"?t.Album:section=="Artistes"?t.Artist:t.Title,StringComparer.CurrentCultureIgnoreCase).ToList();if(descending)filtered.Reverse();status.Text=$"{section}  /  {filtered.Count} titres · {filtered.Select(t=>t.Album).Distinct().Count()} albums";
  AddSelectionActions();if(tracks.Count==0){var empty=new StackPanel{Margin=new Thickness(28,65),Spacing=18};empty.Children.Add(Text("Une place pour tous tes albums.",28));empty.Children.Add(Text("Importe tes MP3, FLAC, WAV ou M4A.\nLes pochettes sont lues depuis tes fichiers.",16,"#BFB2D3"));empty.Children.Add(Button("+ Choisir un dossier",async()=>await Import()));cards.Children.Add(empty);return;}
  IEnumerable<List<Track>> groups=section=="Albums"?filtered.GroupBy(t=>t.Album+"\0"+t.Artist).Select(g=>g.ToList()):section=="Artistes"?filtered.GroupBy(t=>t.Artist).Select(g=>g.ToList()):filtered.Select(t=>new List<Track>{t});
  foreach(var group in groups){var t=group[0];var card=TrackCard(t,section=="Albums"?t.Album:section=="Artistes"?t.Artist:t.Title,section=="Artistes"?$"{group.Count} titres":t.Artist,()=>{queue=section is "Albums" or "Artistes"?group:filtered;Start(t);});card.ContextMenu=TrackMenu(group);cards.Children.Add(card);}
 }
 async Task<Playlist?> CreatePlaylist(){
  var dialog=new Window{Title="Créer une playlist",Background=Brush("#241A30"),Width=380,Height=230,CanResize=false,WindowStartupLocation=WindowStartupLocation.CenterOwner};
  var panel=new StackPanel{Margin=new Thickness(20),Spacing=12};panel.Children.Add(Text("Nouvelle playlist",20));var name=new TextBox{PlaceholderText="Nom de ta playlist"};panel.Children.Add(name);var error=Text("",12);panel.Children.Add(error);
  panel.Children.Add(Button("Créer",()=>{var value=name.Text?.Trim()??"";if(value.Length==0){error.Text="Choisis un nom.";return;}if(prefs.Playlists.Any(p=>p.Name.Equals(value,StringComparison.OrdinalIgnoreCase))){error.Text="Ce nom existe déjà.";return;}var playlist=new Playlist{Name=value};prefs.Playlists.Add(playlist);prefs.Save();dialog.Close(playlist);}));dialog.Content=panel;return await dialog.ShowDialog<Playlist?>(this);
 }
 ContextMenu PlaylistMenu(Playlist playlist){
  var rename=new MenuItem{Header="Renommer…"};rename.Click+=async(_,_)=>await RenamePlaylist(playlist);
  var remove=new MenuItem{Header="Supprimer la playlist…"};remove.Click+=async(_,_)=>await RemovePlaylist(playlist);
  return new ContextMenu{ItemsSource=new[]{rename,remove}};
 }
 async Task RenamePlaylist(Playlist playlist){
  var dialog=new Window{Title="Renommer la playlist",Background=Brush("#241A30"),Width=380,Height=230,CanResize=false,WindowStartupLocation=WindowStartupLocation.CenterOwner};
  var panel=new StackPanel{Margin=new Thickness(20),Spacing=12};panel.Children.Add(Text("Nom de la playlist",20));var name=new TextBox{Text=playlist.Name};panel.Children.Add(name);var error=Text("",12);panel.Children.Add(error);
  panel.Children.Add(Button("Enregistrer",()=>{var value=name.Text?.Trim()??"";if(value.Length==0||prefs.Playlists.Any(p=>p!=playlist&&p.Name.Equals(value,StringComparison.OrdinalIgnoreCase))){error.Text="Choisis un nom unique et non vide.";return;}playlist.Name=value;prefs.Save();dialog.Close();Render();}));dialog.Content=panel;await dialog.ShowDialog(this);
 }
 async Task RemovePlaylist(Playlist playlist){
  var dialog=new Window{Title="Supprimer la playlist",Background=Brush("#241A30"),Width=420,SizeToContent=SizeToContent.Height,CanResize=false,WindowStartupLocation=WindowStartupLocation.CenterOwner};
  var panel=new StackPanel{Margin=new Thickness(20),Spacing=16};panel.Children.Add(Text($"Supprimer {playlist.Name} ?",20));panel.Children.Add(Text("Les fichiers musicaux sont conservés.",13));var actions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};actions.Children.Add(Button("Supprimer",()=>dialog.Close(true)));actions.Children.Add(Button("Annuler",()=>dialog.Close(false)));panel.Children.Add(actions);dialog.Content=panel;
  if(!await dialog.ShowDialog<bool>(this))return;prefs.Playlists.Remove(playlist);if(selectedPlaylist==playlist)selectedPlaylist=null;reordering=false;prefs.Save();Render();
 }
 void AddToPlaylist(Playlist playlist,IEnumerable<Track> songs){foreach(var t in songs)if(!playlist.Paths.Contains(t.Path,StringComparer.OrdinalIgnoreCase))playlist.Paths.Add(t.Path);prefs.Save();Render();status.Text=$"Ajouté à {playlist.Name}";}
 ContextMenu TrackMenu(List<Track> songs){
  var add=new MenuItem{Header=songs.Count>1?"Ajouter ces titres à une playlist":"Ajouter à une playlist"};var choices=new List<MenuItem>();
  foreach(var playlist in prefs.Playlists){var item=new MenuItem{Header=playlist.Name};item.Click+=(_,_)=>AddToPlaylist(playlist,songs);choices.Add(item);}
  var create=new MenuItem{Header="+ Nouvelle playlist…"};create.Click+=async(_,_)=>{var playlist=await CreatePlaylist();if(playlist!=null)AddToPlaylist(playlist,songs);};choices.Add(create);add.ItemsSource=choices;
  var cover=new MenuItem{Header="Miniature Discord…"};cover.Click+=async(_,_)=>await DiscordCover(songs);
  var remove=new MenuItem{Header=songs.Count==1?"Supprimer de la bibliothèque…":"Supprimer ces titres de la bibliothèque…"};remove.Click+=async(_,_)=>await DeleteTracks(songs);
  var entries=new List<MenuItem>{add,MetadataMenu(songs),cover};
  if(section=="Playlists"&&selectedPlaylist!=null){var playlist=selectedPlaylist;var detach=new MenuItem{Header="Retirer de cette playlist"};detach.Click+=(_,_)=>{var paths=songs.Select(t=>t.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);playlist.Paths.RemoveAll(paths.Contains);selectedTracks.ExceptWith(paths);prefs.Save();Render();};entries.Add(detach);}
  entries.Add(remove);return new ContextMenu{ItemsSource=entries};
 }
 async Task DeleteTracks(List<Track> songs){
  var dialog=new Window{Title="Supprimer de la bibliothèque",Width=460,SizeToContent=SizeToContent.Height,CanResize=false,Background=Brush("#241A30"),WindowStartupLocation=WindowStartupLocation.CenterOwner};
  var panel=new StackPanel{Margin=new Thickness(20),Spacing=16};panel.Children.Add(Text(songs.Count==1?"Supprimer ce titre ?":$"Supprimer ces {songs.Count} titres ?",20));panel.Children.Add(new TextBlock{Text="Les fichiers de music seront déplacés dans la Corbeille et retirés des playlists et favoris. Les fichiers d’origine restent dans leur dossier.",TextWrapping=TextWrapping.Wrap});
  var buttons=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};buttons.Children.Add(Button("Supprimer",()=>dialog.Close(true)));buttons.Children.Add(Button("Annuler",()=>dialog.Close(false)));panel.Children.Add(buttons);dialog.Content=panel;
  if(!await dialog.ShowDialog<bool>(this))return;
  var deleted=new HashSet<string>(StringComparer.OrdinalIgnoreCase);string? error=null;
  foreach(var song in songs.DistinctBy(t=>t.Path))try{
   var path=System.IO.Path.GetFullPath(song.Path);var root=System.IO.Path.GetFullPath(MusicDirectory)+System.IO.Path.DirectorySeparatorChar;
   if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new IOException("Ce fichier est en dehors du dossier music.");
   if(current?.Path.Equals(path,StringComparison.OrdinalIgnoreCase)==true){playbackRequest++;output?.Stop();output?.Dispose();audio?.Dispose();output=null;audio=null;current=null;title.Text="Rien en lecture";subtitle.Text="Choisis un titre";play.Content=PlayerIcon("play");updating=true;seek.Value=0;updating=false;time.Text=end.Text="0:00";discord?.ClearPresence();}
   if(File.Exists(path))Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
   deleted.Add(song.Path);
  }catch(Exception e){error=e.Message;break;}
  foreach(var playlist in prefs.Playlists)playlist.Paths.RemoveAll(deleted.Contains);prefs.Favorites.RemoveAll(deleted.Contains);foreach(var path in deleted){prefs.DiscordCovers.Remove(path);prefs.FileIdentities.Remove(path);}queue.RemoveAll(t=>deleted.Contains(t.Path));selectedTracks.ExceptWith(deleted);RefreshQueue();prefs.Save();await Reload();
  status.Text=error==null?$"{deleted.Count} titre(s) déplacé(s) dans la Corbeille":"Suppression interrompue : "+error;
 }
 public static string CoverUrl(string value){
  value=value.Trim();if(value.Length==0)return "";
  if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!="https")throw new ArgumentException("Colle un lien HTTPS vers une image ou une vidéo YouTube.");
  var host=uri.Host.ToLowerInvariant();string? id=null;
  if(host=="youtu.be")id=uri.AbsolutePath.Trim('/').Split('/')[0];
  else if(host is "youtube.com" or "www.youtube.com" or "m.youtube.com" or "music.youtube.com"){
   var parts=uri.AbsolutePath.Trim('/').Split('/');if(parts.Length==2&&parts[0] is "shorts" or "embed" or "live")id=parts[1];else foreach(var pair in uri.Query.TrimStart('?').Split('&'))if(pair.StartsWith("v="))id=Uri.UnescapeDataString(pair[2..]);
  }else return uri.AbsoluteUri;
  if(id==null||!System.Text.RegularExpressions.Regex.IsMatch(id,"^[A-Za-z0-9_-]{11}$"))throw new ArgumentException("Le lien YouTube ne contient pas un identifiant vidéo valide.");
  return $"https://i.ytimg.com/vi/{id}/hqdefault.jpg";
 }
 async Task DiscordCover(List<Track> songs){
  var dialog=new Window{Title="Miniature Discord",Width=500,Height=285,Background=Brush("#241A30"),CanResize=false,WindowStartupLocation=WindowStartupLocation.CenterOwner};
  var panel=new StackPanel{Margin=new Thickness(20),Spacing=12};panel.Children.Add(Text("Pochette sur Discord",20));panel.Children.Add(new TextBlock{Text="Colle le lien YouTube du titre ou un lien HTTPS direct vers son image. Vide le champ pour retirer la miniature. Le lien sera enregistré pour les titres sélectionnés.",TextWrapping=TextWrapping.Wrap});
  var input=new TextBox{Text=prefs.DiscordCovers.GetValueOrDefault(songs[0].Path,""),PlaceholderText="https://www.youtube.com/watch?v=…"};panel.Children.Add(input);var error=Text("",12);panel.Children.Add(error);
  panel.Children.Add(Button("Enregistrer",()=>{try{var url=CoverUrl(input.Text??"");foreach(var song in songs){if(url.Length==0)prefs.DiscordCovers.Remove(song.Path);else prefs.DiscordCovers[song.Path]=url;}prefs.Save();Presence();dialog.Close();}catch(Exception e){error.Text=e.Message;}}));dialog.Content=panel;await dialog.ShowDialog(this);
 }
 static IImage MosaicImage(Bitmap bitmap){
  var size=bitmap.PixelSize;
  using var pixels=new WriteableBitmap(size,bitmap.Dpi,Avalonia.Platform.PixelFormat.Bgra8888,Avalonia.Platform.AlphaFormat.Premul);
  using var buffer=pixels.Lock();bitmap.CopyPixels(buffer);var bytes=new byte[buffer.RowBytes*size.Height];System.Runtime.InteropServices.Marshal.Copy(buffer.Address,bytes,0,bytes.Length);
  bool BlackRow(int y){int dark=0;for(int x=0;x<size.Width;x++){int i=y*buffer.RowBytes+x*4;if(bytes[i]<24&&bytes[i+1]<24&&bytes[i+2]<24)dark++;}return dark>=size.Width*.98;}
  int top=0,bottom=size.Height;while(top<size.Height/4&&BlackRow(top))top++;while(bottom>size.Height*3/4&&BlackRow(bottom-1))bottom--;
  return top==0&&bottom==size.Height?bitmap:new CroppedBitmap{Source=bitmap,SourceRect=new PixelRect(0,top,size.Width,bottom-top)};
 }
 Button CoverCard(Track? track,string heading,string caption,System.Action action,List<Track>? collage=null){
  var visual=new Border{Width=190,Height=190,CornerRadius=new CornerRadius(9),ClipToBounds=true,Background=Brush("#392449")};
  var cover=track==null?null:Cover(track);
  if(collage!=null){var covers=collage.AsEnumerable().Reverse().Take(4).Select(Cover).Where(b=>b!=null).ToList();
   if(covers.Count>1){var mosaic=new Grid{Width=186,Height=186,Background=Brushes.Black,RowSpacing=2,ColumnSpacing=2,RowDefinitions=new RowDefinitions("*,*"),ColumnDefinitions=new ColumnDefinitions("*,*")};for(int i=0;i<4;i++)Add(mosaic,new Border{Width=92,Height=92,ClipToBounds=true,Child=new Image{Source=MosaicImage(covers[i% covers.Count]!),Width=92,Height=92,Stretch=Stretch.UniformToFill}},i/2,i%2);visual.Child=mosaic;}else if(covers.Count==1)visual.Child=new Image{Source=MosaicImage(covers[0]!),Width=186,Height=186,Stretch=Stretch.UniformToFill};
  }
  if(visual.Child==null){if(cover!=null)visual.Child=new Image{Source=MosaicImage(cover),Width=186,Height=186,Stretch=Stretch.UniformToFill};else{
   visual.Background=new LinearGradientBrush{StartPoint=new RelativePoint(0,0,RelativeUnit.Relative),EndPoint=new RelativePoint(1,1,RelativeUnit.Relative),GradientStops=new GradientStops{new(Color.Parse("#62369B"),0),new(Color.Parse("#2C1D40"),.55),new(Color.Parse("#4D2868"),1)}};
   visual.Child=new Avalonia.Controls.Shapes.Path{Data=Geometry.Parse("M 8,17 L 8,4 L 20,2 L 20,15 M 8,8 L 20,6 M 8,17 C 8,22 1,22 1,18 C 1,15 8,14 8,17 M 20,15 C 20,20 13,20 13,16 C 13,13 20,12 20,15"),Stroke=Brush("#D7B9FF"),StrokeThickness=1.2,Width=68,Height=68,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
  }}
  var picture=visual.Child;visual.Child=null;var pictureBackground=visual.Background;visual.Background=Brushes.Black;visual.Child=new Border{Padding=new Thickness(2),Background=Brushes.Black,CornerRadius=new CornerRadius(9),Child=new Border{Width=186,Height=186,Background=pictureBackground,CornerRadius=new CornerRadius(7),ClipToBounds=true,Child=picture}};
  var content=new StackPanel{Spacing=5};content.Children.Add(visual);var name=Text(heading,13);name.TextAlignment=TextAlignment.Center;content.Children.Add(name);var detail=Text(caption,12,"#BFB2D3");detail.TextAlignment=TextAlignment.Center;content.Children.Add(detail);
  var card=new Button{Content=content,Width=210,Padding=new Thickness(10),Margin=new Thickness(0,0,14,14),Background=Brushes.Transparent,CornerRadius=new CornerRadius(11),HorizontalContentAlignment=HorizontalAlignment.Stretch};ToolTip.SetTip(card,heading);card.Click+=(_,_)=>action();CardKeyboard(card);return card;
 }
 bool reordering;
 void ReorderCard(Control card,Action<Point> drop){
  if(!reordering)return;card.Cursor=new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.SizeAll);ToolTip.SetTip(card,"Glisse cette pochette sur une autre pour changer l'ordre");
  Point origin=default;bool dragging=false;var transform=new TranslateTransform();
  card.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent,(_,e)=>{if(!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed)return;origin=e.GetPosition(cards);dragging=true;card.Opacity=.7;card.RenderTransform=transform;card.ZIndex=10;e.Pointer.Capture(card);e.Handled=true;},Avalonia.Interactivity.RoutingStrategies.Tunnel);
  card.PointerMoved+=(_,e)=>{if(!dragging)return;var position=e.GetPosition(cards);transform.X=position.X-origin.X;transform.Y=position.Y-origin.Y;};
  card.AddHandler(Avalonia.Input.InputElement.PointerReleasedEvent,(_,e)=>{if(!dragging)return;var position=e.GetPosition(cards);dragging=false;card.RenderTransform=null;card.Opacity=1;card.ZIndex=0;e.Pointer.Capture(null);e.Handled=true;if(Math.Abs(position.X-origin.X)+Math.Abs(position.Y-origin.Y)>5)drop(position);},Avalonia.Interactivity.RoutingStrategies.Tunnel);
  card.PointerCaptureLost+=(_,_)=>{dragging=false;card.RenderTransform=null;card.Opacity=1;card.ZIndex=0;};
 }
 void RenderPlaylists(){
  libraryActions.Children.Add(Button(reordering?"Terminer":"Réorganiser",()=>{reordering=!reordering;ClearTrackSelection();Render();}));
  libraryActions.Children.Add(IconButton("plus","Nouvelle playlist",async()=>{var playlist=await CreatePlaylist();if(playlist!=null){selectedPlaylist=playlist;Render();}}));
  if(selectedPlaylist==null){ClearTrackSelection();
   status.Text=$"Playlists · {prefs.Playlists.Count}";
   var playlistCards=new List<(Playlist Playlist,Button Card)>();
   foreach(var playlist in prefs.Playlists.Where(p=>p.Name.Contains(search.Text??"",StringComparison.OrdinalIgnoreCase))){var songs=playlist.Paths.Select(path=>tracks.FirstOrDefault(t=>t.Path.Equals(path,StringComparison.OrdinalIgnoreCase))).OfType<Track>().ToList();var card=CoverCard(songs.FirstOrDefault(),playlist.Name,$"{playlist.Paths.Count} titres",()=>{if(reordering)return;selectedPlaylist=playlist;Render();},songs);card.ContextMenu=PlaylistMenu(playlist);playlistCards.Add((playlist,card));cards.Children.Add(card);
    ReorderCard(card,position=>{var target=playlistCards.FirstOrDefault(entry=>entry.Card!=card&&entry.Card.Bounds.Contains(position));if(target.Playlist==null)return;int index=prefs.Playlists.IndexOf(target.Playlist);prefs.Playlists.Remove(playlist);prefs.Playlists.Insert(index,playlist);prefs.Save();Render();});
   }
   if(prefs.Playlists.Count==0)cards.Children.Add(Text("Crée ta première playlist avec le + en haut à droite.",14));return;
  }
  var selected=selectedPlaylist;AddSelectionActions();

  libraryActions.Children.Insert(0,IconButton("back","Toutes les playlists",()=>{selectedPlaylist=null;reordering=false;Render();}));
  var byPath=tracks.ToDictionary(t=>t.Path,StringComparer.OrdinalIgnoreCase);var available=selected.Paths.Where(byPath.ContainsKey).Select(p=>byPath[p]).ToList();
  status.Text=$"{selected.Name} · {available.Count} / {selected.Paths.Count} titres disponibles";
  var playlistMenu=IconButton("menu","Options de cette playlist",()=>{});playlistMenu.ContextMenu=PlaylistMenu(selected);playlistMenu.Click+=(_,_)=>playlistMenu.ContextMenu.Open(playlistMenu);libraryActions.Children.Add(playlistMenu);
  libraryActions.Children.Add(IconButton("play","Lire la playlist",()=>{if(available.Count>0){queue=available.ToList();Start(queue[0]);}}));
  libraryActions.Children.Add(IconButton("music","Ajouter des titres",async()=>await PickPlaylistTracks(selected)));
  if(selected.Paths.Count>available.Count)cards.Children.Add(Text("Certains fichiers sont introuvables. Remets-les dans music : la playlist est conservée.",12)); if(selected.Paths.Count==0)cards.Children.Add(Text("Ajoute des musiques avec l’icône note en haut à droite.",14));
  var trackCards=new List<(Track Track,Button Card)>();
  foreach(var t in available.Where(t=>TrackSearch.Matches(t,search.Text))){var card=TrackCard(t,t.Title,t.Artist,()=>{if(reordering)return;queue=available.ToList();Start(t);});trackCards.Add((t,card));ReorderCard(card,position=>{var target=trackCards.FirstOrDefault(entry=>entry.Card!=card&&entry.Card.Bounds.Contains(position));if(target.Track==null)return;int index=selected.Paths.IndexOf(target.Track.Path);selected.Paths.Remove(t.Path);selected.Paths.Insert(index,t.Path);prefs.Save();Render();});card.ContextMenu=TrackMenu(new List<Track>{t});cards.Children.Add(card);}
 }
 async Task PickPlaylistTracks(Playlist playlist){
  var dialog=new Window{Title=$"Ajouter des titres · {playlist.Name}",Background=Brush("#241A30"),Width=540,Height=560,WindowStartupLocation=WindowStartupLocation.CenterOwner};
  var panel=new Grid{RowDefinitions=new RowDefinitions("Auto,Auto,*,Auto"),Margin=new Thickness(20)};panel.Children.Add(Text("Choisis les musiques à ajouter",20));
  var filter=new TextBox{PlaceholderText="Rechercher un titre, artiste ou album…",Margin=new Thickness(0,12,0,0)};Add(panel,filter,1);
  var list=new StackPanel{Spacing=6};var choices=new List<(Track Track,CheckBox Check)>();
  foreach(var t in tracks.Where(t=>!playlist.Paths.Contains(t.Path,StringComparer.OrdinalIgnoreCase))){var check=new CheckBox{Content=$"{t.Title} — {t.Artist}"};choices.Add((t,check));list.Children.Add(check);}
  var empty=Text(choices.Count==0?(tracks.Count==0?"Importe d’abord un dossier avec tes musiques.":"Tous tes titres sont déjà dans cette playlist."):"Aucun titre ne correspond à ta recherche.",13);empty.IsVisible=choices.Count==0;list.Children.Add(empty);
  filter.TextChanged+=(_,_)=>{foreach(var choice in choices)choice.Check.IsVisible=TrackSearch.Matches(choice.Track,filter.Text);empty.IsVisible=!choices.Any(c=>c.Check.IsVisible);};
  Add(panel,new ScrollViewer{Content=list,Margin=new Thickness(0,14)},2);var actions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6};
  actions.Children.Add(Button("Tout cocher / décocher",()=>{var visible=choices.Where(c=>c.Check.IsVisible).ToList();bool value=!visible.All(c=>c.Check.IsChecked==true);foreach(var choice in visible)choice.Check.IsChecked=value;}));
  actions.Children.Add(Button("Ajouter la sélection",()=>{AddToPlaylist(playlist,choices.Where(c=>c.Check.IsChecked==true).Select(c=>c.Track));dialog.Close();}));Add(panel,actions,3);dialog.Content=panel;await dialog.ShowDialog(this);
 }
 async void Start(Track t)=>await StartTrack(t);
 async Task StartTrack(Track t,double position=0,bool paused=false){var request=++playbackRequest;try{output?.Stop();output?.Dispose();audio?.Dispose();output=null;audio=null;status.Text="Préparation du son…";var playable=await PlayableFile(t.Path);if(request!=playbackRequest||closed)return;audio=new AudioFileReader(playable){Volume=(float)prefs.Volume};audio.CurrentTime=TimeSpan.FromSeconds(Math.Clamp(position,0,Math.Max(0,audio.TotalTime.TotalSeconds-.1)));output=new WaveOutEvent();output.Init(audio);output.Play();if(paused)output.Pause();else sessionHasPlayed=true;current=t;title.Text=t.Title;subtitle.Text=$"{t.Artist} · {t.Album}";end.Text=Format(audio.TotalTime);artwork.Source=Cover(t);play.Content=PlayerIcon(paused?"play":"pause");status.Text=$"{(paused?"Prêt à reprendre":"En lecture")} · {t.Title}";Presence();RememberSession();RefreshQueue();}catch(Exception e){if(request!=playbackRequest)return;output?.Dispose();audio?.Dispose();output=null;audio=null;status.Text="Lecture impossible : "+e.Message;}}
 void Toggle(){if(output==null){if(tracks.Count>0){queue=tracks.ToList();Start(queue[0]);}return;}if(output.PlaybackState==PlaybackState.Playing){output.Pause();play.Content=PlayerIcon("play");}else{output.Play();sessionHasPlayed=true;play.Content=PlayerIcon("pause");}Presence();}
 void Next(int delta){if(queue.Count==0)return;if(delta<0&&audio?.CurrentTime.TotalSeconds>3){audio.CurrentTime=TimeSpan.Zero;Presence();return;}int index=current==null?0:queue.FindIndex(t=>t.Path.Equals(current.Path,StringComparison.OrdinalIgnoreCase));Start(queue[shuffle?Random.Shared.Next(queue.Count):(index+delta+queue.Count)%queue.Count]);}
 async Task DiscordSettings(){var dialog=new Window{Title="lolimusic · Discord",Background=Brush("#241A30"),Width=500,Height=380,CanResize=false,WindowStartupLocation=WindowStartupLocation.CenterOwner};var panel=new StackPanel{Margin=new Thickness(24),Spacing=16};panel.Children.Add(Text("Affiche ta musique sur Discord",21));panel.Children.Add(new TextBlock{Text="Crée une application nommée lolimusic sur le portail développeur Discord. Colle son Application ID et ouvre Discord sur ce PC. Aucun token nécessaire. Vide le champ pour désactiver.",TextWrapping=TextWrapping.Wrap});var id=new TextBox{Text=prefs.DiscordId,PlaceholderText="Application ID"};panel.Children.Add(id);var automatic=new CheckBox{Content="Pochettes automatiques (hébergées publiquement sur Catbox)",IsChecked=prefs.AutoDiscordCovers};panel.Children.Add(automatic);var error=Text("",12);panel.Children.Add(error);panel.Children.Add(Button("Enregistrer",()=>{var value=id.Text?.Trim()??"";if(value.Length>0&&(!ulong.TryParse(value,out _)||value.Length<17)){error.Text="Identifiant Discord invalide";return;}prefs.DiscordId=value;prefs.AutoDiscordCovers=automatic.IsChecked==true;prefs.Save();Connect();dialog.Close();}));dialog.Content=panel;await dialog.ShowDialog(this);}
 void Connect(){discord?.Dispose();discord=null;if(string.IsNullOrWhiteSpace(prefs.DiscordId)){discordStatus.Text="Discord désactivé";return;}discordStatus.Text="Connexion à Discord…";var client=new DiscordConnection(prefs.DiscordId);discord=client;client.Ready+=()=>Dispatcher.UIThread.Post(()=>{if(discord!=client)return;discordStatus.Text="Discord connecté";Presence();});client.ActivityUpdated+=image=>Dispatcher.UIThread.Post(()=>{if(discord!=client||uploadingCovers.Count>0)return;discordStatus.Text=output?.PlaybackState==PlaybackState.Playing?(image?"Discord : activité synchronisée":"Discord : miniature en cours de récupération…"):(sessionHasPlayed?"Discord connecté · lecture en pause":"Discord connecté · en attente de lecture");});client.Error+=message=>Dispatcher.UIThread.Post(()=>{if(discord==client)discordStatus.Text="Discord : "+message+" · nouvelle tentative automatique";});client.Initialize();}
 async void Presence(){
  if(discord==null||current==null)return;
  var song=current;var client=discord;
  try{
   if(!sessionHasPlayed||output==null||audio==null){client.ClearPresence();return;}
   var hash=prefs.AutoDiscordCovers&&song.Cover is {Length:>0}?Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(song.Cover)):null;
   var cover=hash==null?null:prefs.UploadedCovers.GetValueOrDefault(hash);
   cover??=prefs.DiscordCovers.GetValueOrDefault(song.Path);
   void Send(string? url){bool playing=output?.PlaybackState==PlaybackState.Playing;client.SetPresence(new RichPresence{Details=song.Title,State=(playing?"":"En pause · ")+$"{song.Artist} · {song.Album}",Assets=string.IsNullOrWhiteSpace(url)?null:new Assets{LargeImageKey=url,LargeImageText=song.Album=="Sans album"?song.Title:song.Album},Timestamps=playing?new Timestamps(DateTime.UtcNow-(audio?.CurrentTime??TimeSpan.Zero)):null});}
   Send(cover);
   if(!prefs.AutoDiscordCovers||hash==null||prefs.UploadedCovers.ContainsKey(hash)||!uploadingCovers.Add(hash))return;
   try{
    discordStatus.Text="Discord : envoi de la pochette…";using var original=new MemoryStream(song.Cover!);using var bitmap=Bitmap.DecodeToWidth(original,512);using var png=new MemoryStream();bitmap.Save(png,PngBitmapEncoderOptions.Default);
    using var form=new System.Net.Http.MultipartFormDataContent();form.Add(new System.Net.Http.StringContent("fileupload"),"reqtype");var file=new System.Net.Http.ByteArrayContent(png.ToArray());file.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");form.Add(file,"fileToUpload","cover.png");
    await form.LoadIntoBufferAsync();using var response=await coverClient.PostAsync("https://catbox.moe/user/api.php",form);response.EnsureSuccessStatusCode();var url=(await response.Content.ReadAsStringAsync()).Trim();
    if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host!="files.catbox.moe")throw new IOException("Réponse invalide du service de pochettes.");
    prefs.UploadedCovers[hash]=url;prefs.Save();if(discord==client)discordStatus.Text="Discord : pochette envoyée, synchronisation…";
    if(!closed&&current==song&&discord==client&&output!=null)Send(url);
   }finally{uploadingCovers.Remove(hash);}
  }catch(Exception e){if(current==song&&discord==client)discordStatus.Text="Pochette Discord indisponible : "+e.Message;}
 }
}


