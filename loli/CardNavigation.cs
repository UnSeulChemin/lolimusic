using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Lolimusic;

public static class CardNavigation
{
 public static int Destination(IReadOnlyList<Rect> bounds,int index,Key key){
  if(bounds.Count==0)return -1;
  if(key==Key.Home)return 0;if(key==Key.End)return bounds.Count-1;
  if(key==Key.Left)return Math.Max(0,index-1);if(key==Key.Right)return Math.Min(bounds.Count-1,index+1);
  if(key is not (Key.Up or Key.Down))return index;
  var origin=bounds[index].Center;var direction=key==Key.Up?-1:1;
  var rows=Enumerable.Range(0,bounds.Count).Where(i=>(bounds[i].Center.Y-origin.Y)*direction>1).ToList();
  if(rows.Count==0)return index;
  var nearest=rows.Min(i=>Math.Abs(bounds[i].Center.Y-origin.Y));
  return rows.Where(i=>Math.Abs(bounds[i].Center.Y-origin.Y)<=nearest+20).OrderBy(i=>Math.Abs(bounds[i].Center.X-origin.X)).First();
 }
}

public partial class MusicWindow
{
 bool cardNavigationActive;
 void ConfigureCardNavigation(){
  AddHandler(KeyDownEvent,(_,e)=>{
   if(e.Key==Key.Escape&&cardNavigationActive){CancelCardNavigation();e.Handled=true;return;}
   if(e.Key==Key.Escape&&selectingTracks){ClearTrackSelection();Render();e.Handled=true;return;}
   if(e.Key==Key.Escape&&section=="Playlists"&&selectedPlaylist!=null){selectedPlaylist=null;reordering=false;Render();e.Handled=true;return;}
   if(e.Key!=Key.Tab||e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Shift))return;
   if(e.Source is Control source&&(source is TextBox or Slider||source.GetVisualAncestors().Any(c=>c is TextBox or Slider)))return;
   var visible=cards.Children.OfType<Button>().Where(c=>c.IsVisible).ToList();if(visible.Count==0)return;
   int index=visible.FindIndex(c=>c.IsKeyboardFocusWithin);
   int target=!cardNavigationActive||index<0?(e.KeyModifiers==KeyModifiers.Shift?visible.Count-1:0):(index+(e.KeyModifiers==KeyModifiers.Shift?-1:1)+visible.Count)%visible.Count;
   cardNavigationActive=true;e.Handled=true;visible[target].Focus(NavigationMethod.Tab);visible[target].BringIntoView();
  },Avalonia.Interactivity.RoutingStrategies.Tunnel);
  AddHandler(PointerPressedEvent,(_,_)=>CancelCardNavigation(),Avalonia.Interactivity.RoutingStrategies.Tunnel);
 }
 void CancelCardNavigation(){
  cardNavigationActive=false;foreach(var card in cards.Children.OfType<Button>())UpdateCardOutline(card);
  if(cards.Children.Any(c=>c.IsKeyboardFocusWithin))FocusManager?.Focus(null);
 }
 void CardKeyboard(Button card){
  card.BorderThickness=new Thickness(2);UpdateCardOutline(card);
  card.GotFocus+=(_,_)=>UpdateCardOutline(card);card.LostFocus+=(_,_)=>UpdateCardOutline(card);
  card.KeyDown+=(_,e)=>{
   if(e.KeyModifiers!=KeyModifiers.None||e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End))return;
   if(!cardNavigationActive){e.Handled=true;return;}
   var visible=cards.Children.OfType<Button>().Where(c=>c.IsVisible).ToList();int index=visible.IndexOf(card);if(index<0)return;
   int target=CardNavigation.Destination(visible.Select(c=>c.Bounds).ToList(),index,e.Key);e.Handled=true;
   visible[target].Focus(NavigationMethod.Directional);visible[target].BringIntoView();
  };
 }
}
