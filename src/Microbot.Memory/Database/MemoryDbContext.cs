namespace Microbot.Memory.Database;

using Microsoft.EntityFrameworkCore;
using Microbot.Memory.Database.Entities;

/// <summary>
/// Entity Framework Core DbContext for the memory database.
/// </summary>
public class MemoryDbContext : DbContext
{
    private readonly string _databasePath;

    /// <summary>
    /// Creates a new MemoryDbContext with the specified database path.
    /// </summary>
    public MemoryDbContext(string databasePath)
    {
        _databasePath = databasePath;
    }

    /// <summary>
    /// Creates a new MemoryDbContext with DbContextOptions.
    /// </summary>
    public MemoryDbContext(DbContextOptions<MemoryDbContext> options) : base(options)
    {
        _databasePath = string.Empty;
    }

    /// <summary>
    /// Indexed files.
    /// </summary>
    public DbSet<MemoryFile> Files { get; set; } = null!;

    /// <summary>
    /// Text chunks with embeddings.
    /// </summary>
    public DbSet<MemoryChunk> Chunks { get; set; } = null!;

    /// <summary>
    /// Embedding cache.
    /// </summary>
    public DbSet<EmbeddingCache> EmbeddingCache { get; set; } = null!;

    /// <summary>
    /// Index metadata.
    /// </summary>
    public DbSet<MemoryMeta> Meta { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured && !string.IsNullOrEmpty(_databasePath))
        {
            optionsBuilder.UseSqlite($"Data Source={_databasePath}");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // MemoryFile configuration
        modelBuilder.Entity<MemoryFile>(entity =>
        {
            entity.ToTable("memory_files");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Path).HasColumnName("path").IsRequired();
            entity.Property(e => e.Source).HasColumnName("source").IsRequired();
            entity.Property(e => e.Hash).HasColumnName("hash").IsRequired();
            entity.Property(e => e.ModifiedTime).HasColumnName("mtime");
            entity.Property(e => e.Size).HasColumnName("size");
            entity.Property(e => e.IndexedAt).HasColumnName("indexed_at");

            entity.HasIndex(e => e.Path).IsUnique();
            entity.HasIndex(e => e.Source);
        });

        // MemoryChunk configuration
        modelBuilder.Entity<MemoryChunk>(entity =>
        {
            entity.ToTable("memory_chunks");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.FileId).HasColumnName("file_id");
            entity.Property(e => e.Path).HasColumnName("path").IsRequired();
            entity.Property(e => e.Source).HasColumnName("source").IsRequired();
            entity.Property(e => e.StartLine).HasColumnName("start_line");
            entity.Property(e => e.EndLine).HasColumnName("end_line");
            entity.Property(e => e.Hash).HasColumnName("hash").IsRequired();
            entity.Property(e => e.Model).HasColumnName("model").IsRequired();
            entity.Property(e => e.Text).HasColumnName("text").IsRequired();
            entity.Property(e => e.Embedding).HasColumnName("embedding");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(e => e.Path);
            entity.HasIndex(e => e.Source);

            entity.HasOne(e => e.File)
                .WithMany(f => f.Chunks)
                .HasForeignKey(e => e.FileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // EmbeddingCache configuration
        modelBuilder.Entity<EmbeddingCache>(entity =>
        {
            entity.ToTable("embedding_cache");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Provider).HasColumnName("provider").IsRequired();
            entity.Property(e => e.Model).HasColumnName("model").IsRequired();
            entity.Property(e => e.TextHash).HasColumnName("text_hash").IsRequired();
            entity.Property(e => e.Embedding).HasColumnName("embedding").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");

            entity.HasIndex(e => new { e.Provider, e.Model, e.TextHash }).IsUnique();
        });

        // MemoryMeta configuration
        modelBuilder.Entity<MemoryMeta>(entity =>
        {
            entity.ToTable("memory_meta");
            entity.HasKey(e => e.Key);
            entity.Property(e => e.Key).HasColumnName("key");
            entity.Property(e => e.Value).HasColumnName("value").IsRequired();
        });
    }

    /// <summary>
    /// Ensures the database and FTS5 virtual table are created.
    /// </summary>
    public async Task EnsureCreatedWithFtsAsync(CancellationToken cancellationToken = default)
    {
        // Ensure the database directory exists
        var dbDir = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(dbDir) && !Directory.Exists(dbDir))
        {
            Directory.CreateDirectory(dbDir);
        }

        // Create the database schema
        await Database.EnsureCreatedAsync(cancellationToken);

        // Create FTS5 virtual table for full-text search
        await CreateFtsTableAsync(cancellationToken);
    }

    /// <summary>
    /// Creates the FTS5 virtual table and triggers.
    /// </summary>
    private async Task CreateFtsTableAsync(CancellationToken cancellationToken)
    {
        var connection = Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);

        try
        {
            using var command = connection.CreateCommand();

            // Check if FTS table exists
            command.CommandText = @"
                SELECT name FROM sqlite_master 
                WHERE type='table' AND name='memory_chunks_fts';
            ";
            var exists = await command.ExecuteScalarAsync(cancellationToken);

            if (exists == null)
            {
                // Create FTS5 virtual table
                command.CommandText = @"
                    CREATE VIRTUAL TABLE IF NOT EXISTS memory_chunks_fts USING fts5(
                        text,
                        content='memory_chunks',
                        content_rowid='id'
                    );
                ";
                await command.ExecuteNonQueryAsync(cancellationToken);

                // Create triggers to keep FTS in sync
                command.CommandText = @"
                    CREATE TRIGGER IF NOT EXISTS memory_chunks_ai AFTER INSERT ON memory_chunks BEGIN
                        INSERT INTO memory_chunks_fts(rowid, text) VALUES (new.id, new.text);
                    END;
                ";
                await command.ExecuteNonQueryAsync(cancellationToken);

                command.CommandText = @"
                    CREATE TRIGGER IF NOT EXISTS memory_chunks_ad AFTER DELETE ON memory_chunks BEGIN
                        INSERT INTO memory_chunks_fts(memory_chunks_fts, rowid, text) VALUES('delete', old.id, old.text);
                    END;
                ";
                await command.ExecuteNonQueryAsync(cancellationToken);

                command.CommandText = @"
                    CREATE TRIGGER IF NOT EXISTS memory_chunks_au AFTER UPDATE ON memory_chunks BEGIN
                        INSERT INTO memory_chunks_fts(memory_chunks_fts, rowid, text) VALUES('delete', old.id, old.text);
                        INSERT INTO memory_chunks_fts(rowid, text) VALUES (new.id, new.text);
                    END;
                ";
                await command.ExecuteNonQueryAsync(cancellationToken);

                // Populate FTS table with existing data
                command.CommandText = @"
                    INSERT INTO memory_chunks_fts(rowid, text)
                    SELECT id, text FROM memory_chunks;
                ";
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    /// <summary>
    /// Performs a full-text search using FTS5.
    /// </summary>
    public async Task<List<(int ChunkId, double Score)>> FullTextSearchAsync(
        string query,
        int maxResults = 10,
        CancellationToken cancellationToken = default)
    {
        var results = new List<(int ChunkId, double Score)>();

        var connection = Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT rowid, bm25(memory_chunks_fts) as score
                FROM memory_chunks_fts
                WHERE memory_chunks_fts MATCH @query
                ORDER BY score
                LIMIT @limit;
            ";

            var queryParam = command.CreateParameter();
            queryParam.ParameterName = "@query";
            queryParam.Value = query;
            command.Parameters.Add(queryParam);

            var limitParam = command.CreateParameter();
            limitParam.ParameterName = "@limit";
            limitParam.Value = maxResults;
            command.Parameters.Add(limitParam);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var chunkId = reader.GetInt32(0);
                var score = reader.GetDouble(1);
                // BM25 returns negative scores, lower is better
                // Convert to positive score where higher is better
                results.Add((chunkId, -score));
            }
        }
        finally
        {
            await connection.CloseAsync();
        }

        return results;
    }
}
