using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Lolimusic;

public sealed class VirtualCardPanel:Panel
{
 const double CellWidth=224,CellHeight=320;
 readonly List<Func<Button>> factories=[];
 readonly Dictionary<int,Button> realized=[];
 ScrollViewer? scroll;int columns=1;
 public int Count=>factories.Count;
 public event Action<Button>? CardRemoved;
 public void AddCard(Func<Button> factory){factories.Add(factory);InvalidateMeasure();}
 public void ClearCards(){foreach(var card in realized.Values.ToList()){Children.Remove(card);CardRemoved?.Invoke(card);}realized.Clear();factories.Clear();Children.Clear();if(scroll!=null)scroll.Offset=default;InvalidateMeasure();}
 public int IndexOf(Button card)=>realized.FirstOrDefault(p=>p.Value==card,new(-1,card)).Key;
 public Button? CardAt(Point point)=>realized.Values.FirstOrDefault(c=>c.Bounds.Contains(point));
 Button Realize(int index){if(!realized.TryGetValue(index,out var card)){card=factories[index]();realized[index]=card;Children.Add(card);}return card;}
 public void FocusIndex(int index,NavigationMethod method){if(index<0||index>=Count)return;var card=Realize(index);if(scroll!=null){double y=index/columns*CellHeight;if(y<scroll.Offset.Y||y+CellHeight>scroll.Offset.Y+scroll.Viewport.Height)scroll.Offset=new Vector(0,y);}card.Focus(method);InvalidateMeasure();}
 public int Destination(int index,Key key)=>key switch{Key.Home=>0,Key.End=>Count-1,Key.Left=>Math.Max(0,index-1),Key.Right=>Math.Min(Count-1,index+1),Key.Up=>Math.Max(index-columns,index%columns),Key.Down=>Math.Min(Count-1,index+columns),_=>index};
 protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e){base.OnAttachedToVisualTree(e);scroll=this.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();if(scroll!=null)scroll.ScrollChanged+=Scrolled;}
 protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e){if(scroll!=null)scroll.ScrollChanged-=Scrolled;scroll=null;base.OnDetachedFromVisualTree(e);}
 void Scrolled(object? sender,ScrollChangedEventArgs e)=>InvalidateMeasure();
 protected override Size MeasureOverride(Size available){
  double width=double.IsFinite(available.Width)?available.Width:CellWidth*4;columns=Math.Max(1,(int)(width/CellWidth));
  double notice=0;foreach(var child in Children.Where(c=>c is not Button)){child.Measure(new Size(width,double.PositiveInfinity));notice+=child.DesiredSize.Height;}
  double offset=Math.Max(0,(scroll?.Offset.Y??0)-notice),height=scroll?.Viewport.Height??0;if(height<=0)height=CellHeight*3;
  int first=Math.Max(0,(int)(offset/CellHeight)-1)*columns,last=Math.Min(Count,((int)((offset+height)/CellHeight)+2)*columns);
  foreach(var pair in realized.ToList())if((pair.Key<first||pair.Key>=last)&&!pair.Value.IsKeyboardFocusWithin&&!pair.Value.IsPointerOver){Children.Remove(pair.Value);realized.Remove(pair.Key);CardRemoved?.Invoke(pair.Value);}
  for(int i=first;i<last;i++)Realize(i);
  foreach(var card in realized.Values)card.Measure(new Size(CellWidth,CellHeight));
  return new Size(width,notice+Math.Ceiling(Count/(double)columns)*CellHeight);
 }
 protected override Size ArrangeOverride(Size final){double notice=0;foreach(var child in Children.Where(c=>c is not Button)){child.Arrange(new Rect(0,notice,final.Width,child.DesiredSize.Height));notice+=child.DesiredSize.Height;}foreach(var pair in realized)pair.Value.Arrange(new Rect(pair.Key%columns*CellWidth,notice+pair.Key/columns*CellHeight,CellWidth,CellHeight));return final;}
}
