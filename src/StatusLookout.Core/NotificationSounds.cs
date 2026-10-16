using System.Buffers.Binary;
using System.Text.RegularExpressions;
namespace StatusLookout.Core;

public static class NotificationSounds
{
    public static readonly string[] Events=["Outage","Degraded","Recovery","Maintenance","Update"];
    public static readonly string[] Presets=["system","none","chime","pulse","recovery","soft"];
    public static bool IsCustom(string? key)=>key is not null&&Regex.IsMatch(key,"^custom:[a-f0-9]{64}$");
    public static bool IsValid(string? key)=>key is not null&&(Presets.Contains(key)||IsCustom(key));
}
public sealed partial class Preferences
{
    public Dictionary<string,string> NotificationSoundsByEvent {get;set;}=[];
    public Dictionary<string,string> CustomSoundNames {get;set;}=[];
    public double NotificationVolume {get;set;}=0.7;
    private void NormalizeNotificationSounds()
    {
        NotificationSoundsByEvent??=[];CustomSoundNames??=[];
        NotificationSoundsByEvent=NotificationSounds.Events.ToDictionary(key=>key,key=>NotificationSoundsByEvent.TryGetValue(key,out var value)&&NotificationSounds.IsValid(value)?value:"system");
        CustomSoundNames=CustomSoundNames.Where(pair=>NotificationSounds.IsCustom(pair.Key)&&!string.IsNullOrWhiteSpace(pair.Value)).ToDictionary(pair=>pair.Key,pair=>Path.GetFileName(pair.Value).Length>80?Path.GetFileName(pair.Value)[..80]:Path.GetFileName(pair.Value));
        NotificationVolume=double.IsFinite(NotificationVolume)?Math.Clamp(NotificationVolume,0,1):0.7;
    }
}
public sealed record NotificationSoundSelection(string Key,double Volume,string? FilePath=null);

public static class NotificationWave
{
    public const int MaxBytes=2*1024*1024;
    public static TimeSpan Validate(byte[] bytes)
    {
        if(bytes.Length<44||bytes.Length>MaxBytes||!bytes.AsSpan(0,4).SequenceEqual("RIFF"u8)||!bytes.AsSpan(8,4).SequenceEqual("WAVE"u8))throw new InvalidDataException("Choose a WAV sound up to 10 seconds and 2 MB.");
        if(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4,4))!=bytes.Length-8)throw new InvalidDataException("The WAV file is incomplete or invalid.");
        int channels=0,rate=0,alignment=0,dataLength=0;bool format=false;
        for(var at=12;at+8<=bytes.Length;)
        {
            var size=BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at+4,4));
            if(size>int.MaxValue||at+8L+size>bytes.Length)throw new InvalidDataException("The WAV file is incomplete or invalid.");
            var content=bytes.AsSpan(at+8,(int)size);
            if(bytes.AsSpan(at,4).SequenceEqual("fmt "u8))
            {
                if(content.Length<16||BinaryPrimitives.ReadUInt16LittleEndian(content)!=1||BinaryPrimitives.ReadUInt16LittleEndian(content[14..])!=16)throw new InvalidDataException("Use a standard 16-bit PCM WAV sound.");
                channels=BinaryPrimitives.ReadUInt16LittleEndian(content[2..]);rate=BinaryPrimitives.ReadInt32LittleEndian(content[4..]);alignment=BinaryPrimitives.ReadUInt16LittleEndian(content[12..]);
                if(channels is not (1 or 2)||rate is <8000 or >96000||alignment!=channels*2||BinaryPrimitives.ReadInt32LittleEndian(content[8..])!=rate*alignment)throw new InvalidDataException("Use a standard 16-bit PCM WAV sound.");
                format=true;
            }
            if(bytes.AsSpan(at,4).SequenceEqual("data"u8))dataLength=checked(dataLength+(int)size);
            at=checked(at+8+(int)size+((int)size&1));
        }
        if(!format||dataLength==0||dataLength%alignment!=0)throw new InvalidDataException("The WAV file is incomplete or invalid.");
        var duration=TimeSpan.FromSeconds((double)dataLength/(rate*alignment));
        if(duration.TotalSeconds>10)throw new InvalidDataException("Choose a WAV sound up to 10 seconds and 2 MB.");
        return duration;
    }
}
