using System.Text;
using System.IO.Compression;
namespace StatusLookout.Core;

public static class StatusPayload
{
    public static string Decode(byte[] bytes)
    {
        if(bytes.Length>=2&&bytes[0]==0x1F&&bytes[1]==0x8B)
        {
            using var input=new MemoryStream(bytes);using var gzip=new GZipStream(input,CompressionMode.Decompress);using var output=new MemoryStream();
            var buffer=new byte[16384];int count;
            while((count=gzip.Read(buffer))>0) {if(output.Length+count>16*1024*1024)throw new InvalidDataException("Expanded status response is too large.");output.Write(buffer,0,count);}
            bytes=output.ToArray();
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        return Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
    }
}
