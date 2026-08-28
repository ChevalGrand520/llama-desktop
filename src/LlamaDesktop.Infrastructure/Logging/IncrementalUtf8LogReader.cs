using System.Text;

namespace LlamaDesktop.Infrastructure.Logging;

public sealed class IncrementalUtf8LogReader : IDisposable
{
    private readonly string _path;
    private FileStream? _stream;
    private long _offset;
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();

    public IncrementalUtf8LogReader(string path)
    {
        _path = path;
    }

    public string? ReadNew()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            _stream ??= new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (_offset > _stream.Length)
            {
                _offset = 0;
                _decoder.Reset();
            }
            _stream.Seek(_offset, SeekOrigin.Begin);
            var available = (int)Math.Min(65536, _stream.Length - _offset);
            if (available <= 0) return null;
            var buffer = new byte[available];
            var read = _stream.Read(buffer, 0, available);
            _offset += read;
            var charCount = _decoder.GetCharCount(buffer, 0, read, false);
            if (charCount == 0) return null;
            var chars = new char[charCount];
            _decoder.GetChars(buffer, 0, read, chars, 0, false);
            return new string(chars);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
    }
}
