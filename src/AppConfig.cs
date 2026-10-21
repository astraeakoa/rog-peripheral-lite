using System.Text.Json;
namespace GHelper;
public static class AppConfig
{
    static readonly object Gate = new();
    public static readonly string Folder = Environment.GetEnvironmentVariable("ROG_PERIPHERAL_CONFIG_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ROGPeripheral");
    static readonly string FilePath = Path.Combine(Folder, "config.json");
    static Dictionary<string, JsonElement> values = Load();
    static Dictionary<string, JsonElement> Load() { try { return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(FilePath)) ?? new(); } catch { return new(); } }
    public static int Get(string key, int fallback = -1) { lock (Gate) return values.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : fallback; }
    public static string? GetString(string key, string? fallback = null) { lock (Gate) return values.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : fallback; }
    public static bool Is(string key) => Get(key) == 1;
    public static void Set(string key, object value) { lock (Gate) { values[key] = JsonSerializer.SerializeToElement(value); Directory.CreateDirectory(Folder); File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(values)); File.Move(FilePath + ".tmp", FilePath, true); } }
    public static bool IsAuraSync() => false;
    public static bool IsKeyboardAuraSync() => false;
    public static bool IsHeadsetAuraSync() => false;
}
public static class Logger
{
    static readonly object Gate = new();
    public static void WriteLine(string message) { lock(Gate) try { Directory.CreateDirectory(AppConfig.Folder); var p=Path.Combine(AppConfig.Folder,"log.txt"); if (File.Exists(p) && new FileInfo(p).Length>2_000_000) File.Move(p,p+".old",true); File.AppendAllText(p,$"{DateTime.Now:O} {message}\n"); } catch {} }
}
