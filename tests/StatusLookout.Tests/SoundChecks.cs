using System.Text.Json;
using StatusLookout.Core;
internal static class SoundChecks
{
    private static byte[] Wave(int samples=8000)
    {
        using var buffer=new MemoryStream();using var writer=new BinaryWriter(buffer);
        writer.Write("RIFF"u8);writer.Write(36+samples*2);writer.Write("WAVEfmt "u8);writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(8000);writer.Write(16000);writer.Write((short)2);writer.Write((short)16);writer.Write("data"u8);writer.Write(samples*2);writer.Write(new byte[samples*2]);return buffer.ToArray();
    }
    public static void Run(Action<string,Action> check)
    {
        void Assert(bool test){if(!test)throw new Exception("Sound assertion failed.");}
        void Reject(Action action){try{action();}catch(InvalidDataException){return;}throw new Exception("Invalid sound accepted.");}
        check("Legacy preferences keep Windows audio defaults and their existing mute",()=>{var p=new Preferences{NotificationSound=false};p.Normalize();Assert(!p.NotificationSound&&p.NotificationSoundsByEvent.Count==5&&p.NotificationSoundsByEvent.Values.All(value=>value=="system"));});
        check("Sound choices and volume survive preferences persistence",()=>{var p=new Preferences();p.Normalize();p.NotificationSoundsByEvent["Outage"]="pulse";p.NotificationSoundsByEvent["Recovery"]="recovery";p.NotificationSoundsByEvent["Update"]="none";p.NotificationVolume=.35;var copy=JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(p,JsonStore.Options),JsonStore.Options)!;copy.Normalize();Assert(copy.NotificationSoundsByEvent["Outage"]=="pulse"&&copy.NotificationSoundsByEvent["Recovery"]=="recovery"&&copy.NotificationSoundsByEvent["Update"]=="none"&&copy.NotificationVolume==.35);});
        check("Unknown sound paths and invalid volume are normalized safely",()=>{var p=new Preferences{NotificationSoundsByEvent=new(){["Outage"]="../../secret.wav"},NotificationVolume=double.NaN};p.Normalize();Assert(p.NotificationSoundsByEvent["Outage"]=="system"&&p.NotificationVolume==.7);Assert(!NotificationSounds.IsCustom("custom:../../foo")&&!NotificationSounds.IsCustom("https://example.com/a.wav"));});
        check("Sound exports retain presets and reset local-only files",()=>{var key="custom:"+new string('a',64);var p=new Preferences();p.Normalize();p.NotificationSoundsByEvent["Outage"]=key;p.NotificationSoundsByEvent["Recovery"]="recovery";p.CustomSoundNames[key]="alarm.wav";var copy=PreferenceTransfer.Import(PreferenceTransfer.Export(p),ServiceCatalog.Load());Assert(copy.CustomSoundNames.Count==0&&copy.NotificationSoundsByEvent["Outage"]=="system"&&copy.NotificationSoundsByEvent["Recovery"]=="recovery");});
        check("PCM WAV duration is measured from its actual frames",()=>Assert(NotificationWave.Validate(Wave()).TotalSeconds==1));
        check("Long, truncated and non-WAV custom files are rejected",()=>{Reject(()=>NotificationWave.Validate(Wave(80001)));Reject(()=>NotificationWave.Validate(Wave()[..40]));Reject(()=>NotificationWave.Validate("not audio"u8.ToArray()));});
        check("Unsupported WAV formats cannot be imported",()=>{var bytes=Wave();bytes[20]=3;Reject(()=>NotificationWave.Validate(bytes));bytes=Wave();bytes[32]=1;Reject(()=>NotificationWave.Validate(bytes));});
    }
}
