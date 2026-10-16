using System.Collections.ObjectModel;
using System.Security.Cryptography;
using StatusLookout.Core;
namespace StatusLookout.Windows;

public sealed class SoundChoice(string key,string label) : ObservableObject
{
    public string Key {get;}=key;
    public string Label=>NotificationSounds.IsCustom(Key)?L.T("Custom sound")+" · "+label:L.T(label);
    public void Refresh()=>Changed(nameof(Label));
}
public sealed class SoundEventItem : ObservableObject
{
    private readonly MainViewModel _vm;
    public string Key {get;}
    public string Label=>Key=="Update"?L.T("Updates"):Key=="Recovery"?L.T("Back to normal"):L.T(Key);
    public string SelectedSound {get=>_vm.SoundKey(Key);set{_vm.SetSound(Key,value);Changed();Changed(nameof(CustomSoundName));}}
    public string CustomSoundName=>_vm.SoundName(Key);
    public SoundEventItem(MainViewModel vm,string key){_vm=vm;Key=key;}
    public void Refresh(){Changed(null);}
}
public sealed partial class MainViewModel
{
    private readonly string _demoSoundFolder=Path.Combine(Path.GetTempPath(),"StatusLookout-sounds-"+Guid.NewGuid().ToString("N"));
    private string SoundPath(string key)=>NotificationAudio.CustomPath(key,IsDemo?_demoSoundFolder:null);
    public ObservableCollection<SoundChoice> SoundChoices {get;}=[];
    public ObservableCollection<SoundEventItem> SoundEvents {get;}=[];
    public double NotificationVolume {get=>_preferences.NotificationVolume*100;set{if(!double.IsFinite(value))return;_preferences.NotificationVolume=Math.Clamp(value/100,0,1);SavePreferences();Changed();}}
    private void InitializeSounds()
    {
        SoundChoices.Clear();
        foreach(var (key,label) in new[]{("system","Windows default"),("none","Silent"),("chime","Chime"),("pulse","Pulse"),("recovery","Rising notes"),("soft","Soft bell")})SoundChoices.Add(new(key,label));
        foreach(var pair in _preferences.CustomSoundNames)SoundChoices.Add(new(pair.Key,pair.Value));
        SoundEvents.Clear();foreach(var key in NotificationSounds.Events)SoundEvents.Add(new(this,key));
    }
    internal string SoundKey(string key)=>_preferences.NotificationSoundsByEvent.GetValueOrDefault(key,"system");
    internal string SoundName(string key)
    {
        var sound=SoundKey(key);if(!NotificationSounds.IsCustom(sound))return "";
        return File.Exists(SoundPath(sound))?_preferences.CustomSoundNames.GetValueOrDefault(sound,L.T("Custom sound")):L.T("Sound file missing. Import it again.");
    }
    internal void SetSound(string key,string? value)
    {
        if(!NotificationSounds.Events.Contains(key)||!NotificationSounds.IsValid(value))return;
        _preferences.NotificationSoundsByEvent[key]=value!;SavePreferences();
    }
    public NotificationSoundSelection SoundFor(string key)=>new(SoundKey(key),_preferences.NotificationVolume,NotificationSounds.IsCustom(SoundKey(key))?SoundPath(SoundKey(key)):null);
    public async Task ImportSoundAsync(SoundEventItem item,string path)
    {
        if(!SoundEvents.Contains(item))return;
        var info=new FileInfo(path);if(!info.Exists||info.Length>NotificationWave.MaxBytes)throw new InvalidDataException(L.T("Choose a WAV sound up to 10 seconds and 2 MB."));
        var bytes=await File.ReadAllBytesAsync(path,_lifetime.Token);NotificationWave.Validate(bytes);
        var key="custom:"+Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var destination=SoundPath(key);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);await File.WriteAllBytesAsync(destination,bytes,_lifetime.Token);
        _preferences.CustomSoundNames[key]=Path.GetFileName(path);
        if(!SoundChoices.Any(choice=>choice.Key==key))SoundChoices.Add(new(key,Path.GetFileName(path)));
        item.SelectedSound=key;SavePreferences();
    }
}
