using System.Media;
using System.Runtime.InteropServices;
using System.Windows.Media;
using Microsoft.Toolkit.Uwp.Notifications;
using StatusLookout.Core;
namespace StatusLookout.Windows;

public sealed class NotificationAudio(Action<string> report) : IDisposable
{
    private readonly MediaPlayer _player=new();
    private DateTimeOffset _lastPlayed;
    private bool _initialized;
    private void Initialize()
    {
        if(_initialized)return;
        _player.MediaEnded+=(_,_)=>_player.Close();_player.MediaFailed+=(_,_)=>{_player.Close();report(L.T("The sound could not be played. Try another WAV file."));};_initialized=true;
    }
    private static string Folder=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"StatusLookout","Sounds");
    public static string CustomPath(string key,string? folder=null)=>NotificationSounds.IsCustom(key)?Path.Combine(folder??Folder,key[7..]+".wav"):throw new InvalidDataException("Invalid sound.");
    public static string PresetPath(string key)
    {
        if(!NotificationSounds.Presets.Contains(key)||key is "system" or "none")throw new InvalidDataException("Invalid sound.");
        var path=Path.Combine(Folder,"preset-"+key+".wav");
        if(!File.Exists(path))
        {
            using var source=typeof(NotificationAudio).Assembly.GetManifestResourceStream("StatusLookout.Windows.Assets.Sounds."+key+".wav")??throw new InvalidDataException("Sound resource missing.");
            Directory.CreateDirectory(Folder);using var target=File.Create(path);source.CopyTo(target);
        }
        return path;
    }
    public NotificationSoundSelection Resolve(NotificationSoundSelection selection)
    {
        if(NotificationSounds.IsCustom(selection.Key)&&!File.Exists(selection.FilePath))return new("system",selection.Volume);
        return selection;
    }
    public void ConfigureToast(ToastContentBuilder builder,bool enabled,NotificationSoundSelection selection)
    {
        selection=Resolve(selection);
        if(!enabled||selection.Key!="system")builder.AddAudio(new ToastAudio{Silent=true});
    }
    public void Play(NotificationSoundSelection selection,bool preview=false)
    {
        selection=Resolve(selection);
        Initialize();
        if(preview)_player.Stop();
        if(selection.Key=="none")return;
        if(!preview)
        {
            if(DateTimeOffset.UtcNow-_lastPlayed<TimeSpan.FromSeconds(1)||SHQueryUserNotificationState(out var state)<0||state!=5)return;
            if(selection.Key=="system")return; // Windows owns the default toast sound.
            try {if(ToastNotificationManagerCompat.CreateToastNotifier().Setting!=global::Windows.UI.Notifications.NotificationSetting.Enabled)return;}catch{return;}
        }
        if(selection.Key=="system")
        {
            using var current=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"AppEvents\Schemes\Apps\.Default\Notification.Default\.Current");
            var path=current?.GetValue(null) as string;
            if(!string.IsNullOrWhiteSpace(path)&&File.Exists(Environment.ExpandEnvironmentVariables(path))){_player.Open(new Uri(Environment.ExpandEnvironmentVariables(path)));_player.Volume=1;_player.Play();}else SystemSounds.Asterisk.Play();
            return;
        }
        if(selection.Volume<=0)return;
        try
        {
            _player.Stop();
            var path=NotificationSounds.IsCustom(selection.Key)?selection.FilePath!:PresetPath(selection.Key);
            _player.Open(new Uri(path,UriKind.Absolute));_player.Volume=Math.Clamp(selection.Volume,0,1);_player.Play();_lastPlayed=DateTimeOffset.UtcNow;
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException){report(L.T("The sound could not be played. Try another WAV file."));}
    }
    public void Dispose()=>_player.Close();
    public void Stop()=>_player.Stop();
    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
}
