using HaspDemo.Core.Entities;
using HaspDemo.Core.Interfaces;
using System.Text;
using System.Text.Json;

namespace HaspDemo.Persistence;

public sealed class JsonRecordRepository : IDetectRecordRepository
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonRecordRepository(string path)
    {
        _path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    public async Task InsertAsync(InspectRecord record)
    {
        string line = JsonSerializer.Serialize(record) + Environment.NewLine;
        await _gate.WaitAsync();
        try { await File.AppendAllTextAsync(_path, line, new UTF8Encoding(false)); }
        finally { _gate.Release(); }
    }
}
