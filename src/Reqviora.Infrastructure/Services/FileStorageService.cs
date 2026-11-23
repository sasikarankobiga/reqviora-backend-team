using Reqviora.Application.Common.Interfaces;

using System.Buffers.Binary;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Reqviora.Infrastructure.Services;

public sealed class FileStorageService(IOptions<StorageOptions> options) : IFileStorageService
{
    public async Task<string> SaveAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        var value = options.Value;
        var root = Path.GetFullPath(value.RootPath);
        Directory.CreateDirectory(root);
        var extension = Path.GetExtension(Path.GetFileName(fileName));
        var relative = Path.Combine(DateTime.UtcNow.ToString("yyyy/MM/dd"), $"{Guid.NewGuid():N}{extension}");
        var destination = Path.GetFullPath(Path.Combine(root, relative));
        if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid storage path.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0 || buffer.Length > value.MaxFileBytes)
            throw new InvalidDataException("File is empty or exceeds the configured size limit.");
        buffer.Position = 0;
        var clean = await ScanAsync(buffer, value, cancellationToken);
        // A positive malware result is always fatal. RequireAntivirus controls only
        // whether an unavailable scanner may be bypassed outside production.
        if (!clean)
            throw new InvalidDataException("File was rejected by antivirus scanning.");
        buffer.Position = 0;
        await using (var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                         81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            await buffer.CopyToAsync(file, cancellationToken);
        return relative.Replace('\\', '/');
    }

    private static async Task<bool> ScanAsync(MemoryStream content, StorageOptions options, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(options.ClamAvHost, options.ClamAvPort, ct);
            await using var network = client.GetStream();
            await network.WriteAsync("zINSTREAM\0"u8.ToArray(), ct);
            var chunk = new byte[8192];
            int read;
            while ((read = await content.ReadAsync(chunk, ct)) > 0)
            {
                var size = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(size, read);
                await network.WriteAsync(size, ct);
                await network.WriteAsync(chunk.AsMemory(0, read), ct);
            }
            await network.WriteAsync(new byte[4], ct);
            var response = new byte[1024];
            var length = await network.ReadAsync(response, ct);
            return System.Text.Encoding.UTF8.GetString(response, 0, length).Contains("OK", StringComparison.Ordinal);
        }
        catch (Exception exception) when (!options.RequireAntivirus && exception is SocketException or IOException)
        {
            return true;
        }
    }
}
