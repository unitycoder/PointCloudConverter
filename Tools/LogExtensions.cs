using PointCloudConverter.Logger;
using System.Buffers;
using System.Text.Json;

public static class LogExtensions
{
    // Call this from ProgressTick
    public static void WriteProgressUtf8(ILogger log, int threadIndex, long current, long total, int percent, string filePath)
    {
        using var writer = PooledJsonWriter.Rent();        // pooled buffer + Utf8JsonWriter
        writer.WriteStartObject();
        writer.WriteString("event", "Progress");
        writer.WriteNumber("thread", threadIndex);
        writer.WriteNumber("currentPoint", current);
        writer.WriteNumber("totalPoints", total);
        writer.WriteNumber("percentage", percent);
        writer.WriteString("file", filePath);
        writer.WriteEndObject();
        writer.Flush();

        // Send raw UTF-8 to logger (you implement this; falls back to Console)
        log.Write(writer.WrittenSpan, LogEvent.Progress);
    }
}

/// <summary>Small pooled IBufferWriter + Utf8JsonWriter holder.</summary>
internal sealed class PooledJsonWriter : IDisposable
{
    private static readonly ArrayPool<byte> Pool = ArrayPool<byte>.Shared;
    private byte[] _buffer;
    private int _written;
    private readonly Utf8JsonWriter _json;

    private PooledJsonWriter()
    {
        _buffer = Pool.Rent(512); // grows if needed
        _json = new Utf8JsonWriter(new BufferWriter(this), new JsonWriterOptions { SkipValidation = true });
    }

    public static PooledJsonWriter Rent() => new PooledJsonWriter();

    public void WriteStartObject() => _json.WriteStartObject();
    public void WriteEndObject() => _json.WriteEndObject();
    public void WriteString(string name, string value) => _json.WriteString(name, value);
    public void WriteNumber(string name, long value) => _json.WriteNumber(name, value);
    public void WriteNumber(string name, int value) => _json.WriteNumber(name, value);
    public void Flush() => _json.Flush();

    public ReadOnlySpan<byte> WrittenSpan => new ReadOnlySpan<byte>(_buffer, 0, _written);

    public void Dispose()
    {
        _json.Dispose();
        var buf = _buffer;
        _buffer = null;
        _written = 0;
        if (buf != null) Pool.Return(buf);
    }

    // Minimal pooled IBufferWriter<byte>
    private sealed class BufferWriter : IBufferWriter<byte>
    {
        private readonly PooledJsonWriter _owner;
        public BufferWriter(PooledJsonWriter owner) => _owner = owner;

        public void Advance(int count) => _owner._written += count;

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            Ensure(sizeHint);
            return _owner._buffer.AsMemory(_owner._written);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            Ensure(sizeHint);
            return _owner._buffer.AsSpan(_owner._written);
        }

        private void Ensure(int sizeHint)
        {
            if (sizeHint < 1) sizeHint = 1;
            int need = _owner._written + sizeHint;
            if (need <= _owner._buffer.Length) return;

            int newSize = Math.Max(need, _owner._buffer.Length * 2);
            var newBuf = Pool.Rent(newSize);
            Buffer.BlockCopy(_owner._buffer, 0, newBuf, 0, _owner._written);
            Pool.Return(_owner._buffer);
            _owner._buffer = newBuf;
        }
    }
}
