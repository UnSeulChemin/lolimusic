using System.Text.Json;

namespace Lolimusic;

public static class DiscordArtwork
{
 public static TimeSpan RetryDelay(int attempt)=>TimeSpan.FromSeconds(attempt switch{<=1=>2,2=>5,3=>10,_=>15});
 public static bool Missing(JsonElement activity,JsonElement reply){
  if(activity.ValueKind!=JsonValueKind.Object||!activity.TryGetProperty("assets",out var desired)||desired.ValueKind!=JsonValueKind.Object||!desired.TryGetProperty("large_image",out var requested)||requested.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(requested.GetString()))return false;
  if(!reply.TryGetProperty("data",out var data)||data.ValueKind!=JsonValueKind.Object||!data.TryGetProperty("assets",out var assets)||assets.ValueKind!=JsonValueKind.Object||!assets.TryGetProperty("large_image",out var image)||image.ValueKind!=JsonValueKind.String)return true;
  var value=image.GetString();
  // An echoed URL is not a resolved Discord image. Empty/null values also need retrying.
  return string.IsNullOrWhiteSpace(value)||value.StartsWith("https://",StringComparison.OrdinalIgnoreCase)||value.StartsWith("http://",StringComparison.OrdinalIgnoreCase);
 }
}
