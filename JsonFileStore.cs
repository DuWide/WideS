using System.Text.Encodings.Web;
using System.Text.Json;

namespace DevCockpit;

public sealed class JsonFileStore<T> where T : new()
{
    private readonly string _path;
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public JsonFileStore(string path)
    {
        _path = path;
    }

    public T Load()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        if (!File.Exists(_path))
        {
            var empty = new T();
            Save(empty);
            return empty;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(_path), _options) ?? new T();
        }
        catch
        {
            return new T();
        }
    }

    public void Save(T data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(data, _options));
    }
}
