using System.Text.Json;
using System.Text.Json.Serialization;
namespace UptimeKite.Core;

public sealed class JsonStore(string directory)
{
    private readonly HashSet<string> _blockedWrites = [];
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true, Converters = { new JsonStringEnumConverter() }
    };
    public string? Warning { get; private set; }
    public T Load<T>(string filename) where T : new()
    {
        var path = Path.Combine(directory, filename);
        if (!File.Exists(path)) return new T();
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? throw new JsonException("Empty document."); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            try
            {
                var backup = path + ".unreadable-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "." + Guid.NewGuid().ToString("N") + ".json";
                File.Copy(path, backup);
                Warning = $"Could not read {filename}. Defaults were loaded and the original file was backed up.";
            }
            catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException)
            {
                _blockedWrites.Add(filename);
                Warning = $"Could not read or back up {filename}. The original file is preserved; saving it is disabled.";
            }
            return new T();
        }
    }
    public void Save<T>(string filename, T value)
    {
        if (_blockedWrites.Contains(filename)) throw new IOException($"The unreadable {filename} could not be backed up. Resolve the file permissions before saving.");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, filename);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, Options));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
