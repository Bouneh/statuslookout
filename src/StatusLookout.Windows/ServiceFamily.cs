using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media;
namespace StatusLookout.Windows;

public sealed class ServiceFamily : ObservableObject, IDisposable
{
    public string Name { get; }
    public IReadOnlyList<ServiceItem> Members { get; }
    public ObservableCollection<ServiceItem> VisibleMembers { get; } = [];
    public ImageSource? Logo => (Name=="Google"?Members.FirstOrDefault(s=>s.Id=="extra.GoogleCloud"):null)?.Logo??Members[0].Logo;
    public bool HasChildren => Members.Count > 1;
    public ServiceItem SingleService => Members[0];
    public bool? IsSelected => Members.All(s=>s.IsWatched) ? true : Members.All(s=>!s.IsWatched) ? false : null;
    public string Summary => L.Format("{0} of {1} selected",Members.Count(s=>s.IsWatched),Members.Count);
    public string SelectionHint => L.T("Select or deselect every service in this family, including those hidden by search.");
    private bool _expanded;
    public bool IsExpanded { get=>_expanded; set { _expanded=value;Changed(); } }
    public ServiceFamily(string name,IReadOnlyList<ServiceItem> members)
    {
        Name=name;Members=members;
        foreach(var member in members) member.PropertyChanged+=MemberChanged;
    }
    public void Filter(Func<ServiceItem,bool> filter,bool searching)
    {
        VisibleMembers.Clear();
        foreach(var member in Members.Where(filter)) VisibleMembers.Add(member);
        if(searching && VisibleMembers.Count>0) IsExpanded=true;
        Changed(nameof(Summary));Changed(nameof(SelectionHint));
    }
    private void MemberChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName is null or nameof(ServiceItem.IsWatched)) {Changed(nameof(IsSelected));Changed(nameof(Summary));}
        if(e.PropertyName is null or nameof(ServiceItem.Logo)) Changed(nameof(Logo));
    }
    public void Dispose() {foreach(var member in Members)member.PropertyChanged-=MemberChanged;}
}
