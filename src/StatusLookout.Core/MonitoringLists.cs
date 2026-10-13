using System.Text.Json.Serialization;
namespace StatusLookout.Core;

public sealed class MonitoringList
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public HashSet<string> Services { get; set; } = [];
    [JsonIgnore] public bool IsBuiltIn => Id is "work" or "personal";
    public void Validate()
    {
        if(string.IsNullOrWhiteSpace(Id)||Id.Length>80||Id.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not ('.' or '-'))||Services is null)
            throw new InvalidDataException("Invalid monitoring list.");
        if(string.IsNullOrWhiteSpace(Name)||Name.Trim().Length>48||Name.Any(char.IsControl))
            throw new InvalidDataException("Choose a list name between 1 and 48 characters.");
    }
}
public sealed partial class Preferences
{
    public List<MonitoringList>? MonitoringLists { get; set; }
    public string ActiveListId { get; set; } = "work";
    public bool AutomaticUpdates { get; set; } = true;
    public string? LastNotifiedUpdateVersion { get; set; }
    private void NormalizeWatchlists()
    {
        var legacy=MonitoringLists is null;
        MonitoringLists=(MonitoringLists??[]).Where(list=>{try{list.Validate();return true;}catch{return false;}}).DistinctBy(list=>list.Id).ToList();
        if(!MonitoringLists.Any(list=>list.Id=="work")) MonitoringLists.Insert(0,new(){Id="work",Name="Work",Services=legacy?new(SelectedServices):[]});
        if(!MonitoringLists.Any(list=>list.Id=="personal")) MonitoringLists.Insert(1,new(){Id="personal",Name="Personal"});
        foreach(var list in MonitoringLists) {list.Name=list.IsBuiltIn?(list.Id=="work"?"Work":"Personal"):list.Name.Trim();list.Services.RemoveWhere(string.IsNullOrWhiteSpace);}
        if(!MonitoringLists.Any(list=>list.Id==ActiveListId))ActiveListId="work";
        SelectedServices=MonitoringLists.Single(list=>list.Id==ActiveListId).Services;
    }
    public void SelectList(string id)
    {
        Normalize();
        var list=MonitoringLists!.SingleOrDefault(list=>list.Id==id)??throw new InvalidDataException("Unknown monitoring list.");
        ActiveListId=list.Id;SelectedServices=list.Services;
    }
    public MonitoringList CreateList(string name)
    {
        Normalize();
        if(MonitoringLists!.Any(list=>list.Name.Equals(name.Trim(),StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("A list with this name already exists.");
        var list=new MonitoringList {Id="list."+Guid.NewGuid().ToString("N"),Name=name.Trim()};list.Validate();MonitoringLists!.Add(list);return list;
    }
}
public static class AutomaticUpdatePolicy
{
    public static bool ShouldNotify(Version available,Version installed,string? lastNotified) => available>installed&&(!Version.TryParse(lastNotified,out var previous)||available>previous);
}
