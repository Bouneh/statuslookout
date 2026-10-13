using System.Collections.ObjectModel;
namespace StatusLookout.Windows;

public sealed partial class MainViewModel
{
    private readonly List<ServiceFamily> _families=[];
    private bool _batchWatching;
    public ObservableCollection<ServiceFamily> LibraryFamilies {get;}=[];
    public bool IsLibraryEmpty=>LibraryFamilies.Count==0;
    private void RebuildLibrary()
    {
        var expansion=_families.ToDictionary(g=>g.Name,g=>g.IsExpanded);
        foreach(var group in _families)group.Dispose();
        _families.Clear();
        foreach(var family in AllServices.GroupBy(s=>s.DefaultDefinition.Family??s.Name).OrderBy(g=>g.Key,StringComparer.OrdinalIgnoreCase))
            _families.Add(new(family.Key,family.OrderBy(s=>s.Name,StringComparer.OrdinalIgnoreCase).ToArray()){IsExpanded=expansion.GetValueOrDefault(family.Key)});
        RefreshLibraryFamilies();
    }
    private void RefreshLibraryFamilies()
    {
        LibraryFamilies.Clear();
        foreach(var group in _families)
        {
            group.Filter(s=>FilterService(s)||(!string.IsNullOrWhiteSpace(Search)&&group.Name.Contains(Search.Trim(),StringComparison.OrdinalIgnoreCase)&&(Category==Categories[0]||s.Category==Category)),!string.IsNullOrWhiteSpace(Search));
            if(group.VisibleMembers.Count>0)LibraryFamilies.Add(group);
        }
        Changed(nameof(IsLibraryEmpty));
    }
    public void SetFamilyWatched(ServiceFamily family,bool value)
    {
        if(!_families.Contains(family))return;
        _batchWatching=true;
        try {foreach(var service in family.Members)service.IsWatched=value;}
        finally {_batchWatching=false;}
        SavePreferences();SaveState();RebuildWatchlist();
        if(value&&_started&&!IsPaused)_=RefreshAsync();
    }
    public void ExpandFamilies(bool expanded) {foreach(var group in LibraryFamilies)group.IsExpanded=expanded;}
}
