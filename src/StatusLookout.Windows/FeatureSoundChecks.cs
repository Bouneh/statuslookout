using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Toolkit.Uwp.Notifications;
using StatusLookout.Core;
namespace StatusLookout.Windows;

internal static class FeatureSoundChecks
{
    public static async Task<object> RunAsync(MainViewModel vm,MainWindow window)
    {
        byte[]? imported=null;
        foreach(var preset in NotificationSounds.Presets.Where(key=>key is not ("system" or "none")))
        {
            using var source=typeof(NotificationAudio).Assembly.GetManifestResourceStream("StatusLookout.Windows.Assets.Sounds."+preset+".wav")??throw new InvalidDataException("Missing audio resource.");
            using var bytes=new MemoryStream();source.CopyTo(bytes);var data=bytes.ToArray();
            if(NotificationWave.Validate(data).TotalSeconds is <=0 or >2)throw new InvalidDataException("Invalid bundled notification sound.");imported??=data;
        }
        var selected=vm.SoundEvents.Single(item=>item.Key=="Outage");selected.SelectedSound="pulse";vm.NotificationVolume=35;
        vm.ImportPreferences(vm.ExportPreferences());var persisted=vm.SoundFor("Outage").Key=="pulse"&&vm.NotificationVolume==35;
        var path=Path.Combine(Path.GetTempPath(),"StatusLookout-test-sound-"+Guid.NewGuid().ToString("N")+".wav");
        try
        {
            await File.WriteAllBytesAsync(path,imported!);selected=vm.SoundEvents.Single(item=>item.Key=="Recovery");await vm.ImportSoundAsync(selected,path);
            File.Delete(path);var sound=vm.SoundFor("Recovery");if(!NotificationSounds.IsCustom(sound.Key)||!File.Exists(sound.FilePath))throw new InvalidOperationException("Custom audio was not copied.");
        }
        finally{if(File.Exists(path))File.Delete(path);}
        using var audio=new NotificationAudio(_=>{});
        bool Silent(string key,bool enabled)
        {
            var toast=new ToastContentBuilder().AddText("Sound test");audio.ConfigureToast(toast,enabled,new(key,.35));return toast.GetToastContent().GetContent().Contains("silent=\"true\"",StringComparison.OrdinalIgnoreCase);
        }
        var noDoubleSound=Silent("pulse",true)&&Silent("none",true)&&Silent("system",false)&&!Silent("system",true);
        window.ShowSounds();await System.Windows.Application.Current.Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ContextIdle);window.UpdateLayout();
        IEnumerable<DependencyObject> Descendants(DependencyObject root){for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}}
        var pickers=Descendants(window).OfType<ComboBox>().Where(box=>box.DataContext is SoundEventItem).ToArray();var controls=pickers.Length==5&&pickers.All(box=>box.Items.Count>=6);
        if(!persisted||!noDoubleSound||!controls)throw new InvalidOperationException($"Sound feature verification failed: saved={persisted}, silence={noDoubleSound}, controls={controls}.");
        return new{Presets=4,PerEventChoices=5,Preferences=persisted,CustomImport=true,SilentToastForCustomAudio=noDoubleSound,SettingsControls=controls};
    }
}
