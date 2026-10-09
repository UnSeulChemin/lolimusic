using System.ComponentModel;

namespace Lolimusic;

public sealed class TrackChoice(Track track):INotifyPropertyChanged
{
 public Track Track{get;}=track;
 bool selected;
 public bool Selected{get=>selected;set{if(selected==value)return;selected=value;PropertyChanged?.Invoke(this,new(nameof(Selected)));}}
 public event PropertyChangedEventHandler? PropertyChanged;
}
