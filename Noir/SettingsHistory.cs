using System.IO;
using System.Text.Json;

namespace Noir;
// Profile-local preferences; kept separate from the shared phone library format.
public sealed class HistorySettings
{
    public int WeeklyGoal {get;set;}=5;
    public static HistorySettings Load(string directory)
    {
        try{var settings=JsonSerializer.Deserialize<HistorySettings>(File.ReadAllText(Path.Combine(directory,"history-settings.json")))??new();settings.WeeklyGoal=Math.Clamp(settings.WeeklyGoal,0,100);return settings;}
        catch(IOException){return new();}catch(JsonException){return new();}
    }
    public void Save(string directory)
    {
        var path=Path.Combine(directory,"history-settings.json");File.WriteAllText(path+".tmp",JsonSerializer.Serialize(this));File.Move(path+".tmp",path,true);
    }
}
