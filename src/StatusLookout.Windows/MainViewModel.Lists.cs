using System.Collections.ObjectModel;
using StatusLookout.Core;
namespace StatusLookout.Windows;

public sealed class MonitoringListChoice(MonitoringList list) : ObservableObject
{
    public MonitoringList Definition { get; }=list;
    public string Id=>Definition.Id;
    public string Name=>Definition.IsBuiltIn?L.T(Definition.Name):Definition.Name;
    public void Refresh()=>Changed(nameof(Name));
}
public sealed partial class MainViewModel
{
    private bool _switchingList;
    public ObservableCollection<MonitoringListChoice> Lists {get;}=[];
    public string ActiveListId {get=>_preferences.ActiveListId;set{if(value==ActiveListId||!Lists.Any(list=>list.Id==value))return;_preferences.SelectList(value);ApplyActiveList();SavePreferences();}}
    public bool CanEditList=>_preferences.ActiveListId is not ("work" or "personal");
    private void RebuildLists()
    {
        Lists.Clear();foreach(var list in _preferences.MonitoringLists!)Lists.Add(new(list));Changed(nameof(ActiveListId));Changed(nameof(CanEditList));
    }
    private void ApplyActiveList()
    {
        _switchingList=true;
        try {foreach(var service in AllServices)service.IsWatched=_preferences.SelectedServices.Contains(service.Id);}
        finally {_switchingList=false;}
        Changed(nameof(ActiveListId));Changed(nameof(CanEditList));RebuildWatchlist();
        if(_started&&!IsPaused)_=RefreshAsync();
    }
    public void CreateList(string name)
    {
        if(Lists.Any(list=>list.Name.Equals(name.Trim(),StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException(L.T("A list with this name already exists."));
        var list=_preferences.CreateList(name);RebuildLists();ActiveListId=list.Id;
    }
    public void RenameList(string name)
    {
        if(!CanEditList)return;
        var active=_preferences.MonitoringLists!.Single(list=>list.Id==ActiveListId);var candidate=new MonitoringList{Id=active.Id,Name=name.Trim()};candidate.Validate();
        if(Lists.Any(list=>list.Id!=active.Id&&list.Name.Equals(candidate.Name,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException(L.T("A list with this name already exists."));
        active.Name=candidate.Name;foreach(var list in Lists)list.Refresh();SavePreferences();
    }
    public void DeleteList()
    {
        if(!CanEditList)return;
        var previous=ActiveListId;ActiveListId="work";_preferences.MonitoringLists!.RemoveAll(list=>list.Id==previous);RebuildLists();SavePreferences();
    }
}
