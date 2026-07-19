using Microsoft.EntityFrameworkCore;
using AmbientDirector.Api.Data;
using AmbientDirector.Api.Models;

namespace AmbientDirector.Api.Services;

/// <summary>Boards (composable player-facing TV layouts) persisted in SQLite. Ids match case-insensitively
/// (NOCASE collation). Mirrors <see cref="ScreenStore"/>.</summary>
public class BoardStore(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<List<Board>> GetAllAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Boards.AsNoTracking().OrderBy(b => b.Id).ToListAsync();
    }

    public async Task<Board?> GetAsync(string id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Boards.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id);
    }

    public async Task UpsertAsync(Board board)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var existing = await db.Boards.SingleOrDefaultAsync(b => b.Id == board.Id);
        if (existing is null)
        {
            db.Boards.Add(board);
        }
        else
        {
            existing.Name = board.Name;
            existing.BackgroundColor = board.BackgroundColor;
            existing.BackgroundImage = board.BackgroundImage;
            existing.Elements = board.Elements;
        }
        await db.SaveChangesAsync();
    }

    public async Task<bool> DeleteAsync(string id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Boards.Where(b => b.Id == id).ExecuteDeleteAsync() > 0;
    }
}
