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
            return Deserialize(_path);
        }
        catch (Exception primaryError)
        {
            var backupPath = BackupPath();
            if (File.Exists(backupPath))
            {
                try
                {
                    var restored = Deserialize(backupPath);
                    PreserveCorruptFile();
                    File.Copy(backupPath, _path, true);
                    return restored;
                }
                catch
                {
                    // The original exception below contains the useful path and parse error.
                }
            }

            throw new InvalidDataException(
                $"Не удалось прочитать данные WideS: {_path}. Файл оставлен без изменений.",
                primaryError);
        }
    }

    public void Save(T data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tempPath = _path + ".tmp";
        var json = JsonSerializer.Serialize(data, _options);

        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
        {
            writer.Write(json);
            writer.Flush();
            stream.Flush(true);
        }

        if (File.Exists(_path))
        {
            File.Copy(_path, BackupPath(), true);
        }

        File.Move(tempPath, _path, true);
    }

    private T Deserialize(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), _options) ?? new T();

    private string BackupPath() => _path + ".bak";

    private void PreserveCorruptFile()
    {
        if (!File.Exists(_path)) return;
        var corruptPath = $"{_path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
        File.Copy(_path, corruptPath, false);
    }
}
