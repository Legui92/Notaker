using System.IO;
using System.Security.Cryptography;
using System.Text;
using NAudio.Wave;

namespace Notaker;

public sealed record RecoveryEntry(string Id, DateTime CreatedAt)
{
    public string Label => CreatedAt.ToString("dd MMM yyyy · HH:mm:ss");
}

public sealed class RecoveryStore
{
    private readonly string folder;
    public RecoveryStore(string root) { folder = Path.Combine(root, "recovery"); Directory.CreateDirectory(folder); }
    private string PathFor(string id, string extension)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Identificador de recuperación inválido.");
        return Path.Combine(folder, id + extension);
    }
    public IReadOnlyList<RecoveryEntry> Entries() => Directory.GetFiles(folder, "*.audio")
        .Select(p => new RecoveryEntry(Path.GetFileNameWithoutExtension(p), File.GetCreationTime(p)))
        .Where(x => Guid.TryParseExact(x.Id, "N", out _)).OrderByDescending(x => x.CreatedAt).ToArray();
    public Capture Begin()
    {
        var id = Guid.NewGuid().ToString("N");
        return new Capture(id, PathFor(id, ".audio"));
    }
    public byte[] ReadAudio(string id)
    {
        using var input = new BinaryReader(File.Open(PathFor(id, ".audio"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        if (input.BaseStream.Length < 4 || input.ReadInt32() != 0x31524B4E) throw new IOException("Copia de audio no reconocida.");
        using var pcm = new MemoryStream();
        while (input.BaseStream.Position + 4 <= input.BaseStream.Length)
        {
            int length = input.ReadInt32();
            if (length <= 0 || length > 1_000_000) throw new IOException("Copia de audio dañada.");
            // A crash during the final write does not invalidate the preceding complete chunks.
            if (input.BaseStream.Length - input.BaseStream.Position < length) break;
            var bytes = ProtectedData.Unprotect(input.ReadBytes(length), null, DataProtectionScope.CurrentUser);
            if (bytes.Length % 2 != 0) throw new IOException("Audio incompleto.");
            pcm.Write(bytes);
            if (pcm.Length > 20_000_000) throw new IOException("La copia supera el límite de audio.");
        }
        if (pcm.Length == 0) throw new IOException("No quedó audio completo en esta copia.");
        using var wav = new MemoryStream();
        using var writer = new WaveFileWriter(wav, new WaveFormat(16000, 16, 1));
        writer.Write(pcm.ToArray(), 0, (int)pcm.Length); writer.Flush();
        return wav.ToArray();
    }
    public void SaveText(string id, string text)
    {
        var path = PathFor(id, ".text");
        File.WriteAllBytes(path + ".tmp", ProtectedData.Protect(Encoding.UTF8.GetBytes(text), null, DataProtectionScope.CurrentUser));
        File.Move(path + ".tmp", path, true);
    }
    public string? ReadText(string id)
    {
        var path = PathFor(id, ".text");
        return File.Exists(path) ? Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser)) : null;
    }
    public void Delete(string id)
    {
        foreach (var extension in new[] { ".audio", ".text", ".text.tmp" }) File.Delete(PathFor(id, extension));
    }
    public sealed class Capture : IDisposable
    {
        private readonly BinaryWriter writer;
        public string Id { get; }
        internal Capture(string id, string path)
        {
            Id = id;
            writer = new BinaryWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough));
            writer.Write(0x31524B4E); writer.Flush();
        }
        public void Append(byte[] buffer, int count)
        {
            var encrypted = ProtectedData.Protect(buffer.AsSpan(0, count).ToArray(), null, DataProtectionScope.CurrentUser);
            writer.Write(encrypted.Length); writer.Write(encrypted); writer.Flush();
        }
        public void Dispose() => writer.Dispose();
    }
}
