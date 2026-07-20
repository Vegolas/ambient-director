using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AmbientDirector.Tests.Integration;

/// <summary>
/// GET /setup/backup (issue #110) streams a consistent SQLite snapshot as a file download. The bytes must be
/// a real, openable SQLite database (not the live file mid-write), and it comes back as an attachment.
/// </summary>
[Collection("integration")]
public class BackupTests
{
    // Every SQLite database file begins with this 16-byte header string.
    private static readonly byte[] SqliteMagic = Encoding.ASCII.GetBytes("SQLite format 3\0");

    [Fact]
    public async Task Backup_returns_a_valid_sqlite_file_download()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/setup/backup");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Downloaded as an attachment with a .db filename so the browser saves it instead of rendering it.
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName;
        Assert.NotNull(fileName);
        Assert.EndsWith(".db", fileName);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > SqliteMagic.Length);
        Assert.Equal(SqliteMagic, bytes[..SqliteMagic.Length]);

        // Prove it's actually a usable database: write it out and open it — the migrated schema is present.
        var path = Path.Combine(Path.GetTempPath(), "ad-backup-test", Guid.NewGuid().ToString("N") + ".db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            using var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Scenes';";
            Assert.Equal(1L, (long)cmd.ExecuteScalar()!);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }
}
