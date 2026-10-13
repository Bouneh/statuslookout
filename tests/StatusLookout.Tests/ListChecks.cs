using System.Text.Json;
using StatusLookout.Core;
internal static class ListChecks
{
    public static void Run(Action<string,Action> check)
    {
        void Assert(bool condition){if(!condition)throw new Exception("List assertion failed.");}
        void Reject(Action action){try{action();}catch(InvalidDataException){return;}throw new Exception("Invalid list was accepted.");}
        check("Legacy watchlists migrate to Work and an empty Personal list",()=>{
            var prefs=JsonSerializer.Deserialize<Preferences>("""{"selectedServices":["statuspage.GitHub"]}""",JsonStore.Options)!;prefs.Normalize();
            Assert(prefs.MonitoringLists!.Count==2&&prefs.ActiveListId=="work");Assert(prefs.SelectedServices.SetEquals(["statuspage.GitHub"]));Assert(prefs.MonitoringLists.Single(l=>l.Id=="personal").Services.Count==0);
        });
        check("Switching lists keeps selections independent and remembers edits",()=>{
            var p=new Preferences {SelectedServices=["statuspage.GitHub"]};p.Normalize();p.SelectList("personal");Assert(p.SelectedServices.Count==0);p.SelectedServices.Add("statuspage.Discord");p.SelectList("work");Assert(p.SelectedServices.SetEquals(["statuspage.GitHub"]));p.SelectList("personal");Assert(p.SelectedServices.SetEquals(["statuspage.Discord"]));
        });
        check("Custom lists start empty and survive JSON persistence",()=>{
            var p=new Preferences();var list=p.CreateList("Streaming");p.SelectList(list.Id);p.SelectedServices.Add("statuspage.Discord");
            var restored=JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(p,JsonStore.Options),JsonStore.Options)!;restored.Normalize();Assert(restored.ActiveListId==list.Id&&restored.SelectedServices.Contains("statuspage.Discord")&&restored.MonitoringLists!.Count==3);
        });
        check("Duplicate and blank list names are rejected",()=>{var p=new Preferences();p.CreateList("Streaming");Reject(()=>p.CreateList("streaming"));Reject(()=>p.CreateList(" "));Reject(()=>p.CreateList("Bad\nname"));});
        check("Preference transfer includes inactive lists and automatic update opt-out",()=>{
            var p=new Preferences {AutomaticUpdates=false,LastNotifiedUpdateVersion="1.0.0"};p.Normalize();p.SelectList("personal");p.SelectedServices.Add("statuspage.GitHub");p.SelectList("work");
            var copy=PreferenceTransfer.Import(PreferenceTransfer.Export(p),ServiceCatalog.Load());copy.SelectList("personal");Assert(copy.SelectedServices.Contains("statuspage.GitHub")&&!copy.AutomaticUpdates&&copy.LastNotifiedUpdateVersion is null);
        });
        check("Import rejects unknown services in inactive lists",()=>{
            var p=new Preferences();p.Normalize();p.SelectList("personal");p.SelectedServices.Add("missing.service");p.SelectList("work");Reject(()=>PreferenceTransfer.Import(PreferenceTransfer.Export(p),ServiceCatalog.Load()));
        });
        check("Automatic update alerts are limited to newer unseen versions",()=>{
            Assert(AutomaticUpdatePolicy.ShouldNotify(new(0,6,0),new(0,5,1),null));Assert(!AutomaticUpdatePolicy.ShouldNotify(new(0,5,1),new(0,5,1),null));Assert(!AutomaticUpdatePolicy.ShouldNotify(new(0,6,0),new(0,5,1),"0.6.0"));Assert(AutomaticUpdatePolicy.ShouldNotify(new(0,7,0),new(0,5,1),"0.6.0"));
        });
    }
}
