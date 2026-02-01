namespace Microbot.Memory;

using System.IO.Hashing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microbot.Memory.Chunking;
using Microbot.Memory.Database;
using Microbot.Memory.Database.Entities;
using Microbot.Memory.Embeddings;
using Microbot.Memory.Interfaces;
using Microbot.Memory.Search;
using Microbot.Memory.Sessions;

/// <summary>
/// Main memory manager that orchestrates all memory operations.
/// </summary>
public class MemoryManager : IMemoryManager
{
    private readonly MemoryDbContext _dbContext;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly ITextChunker _textChunker;
    private readonly VectorSearch _vectorSearch;
    private readonly HybridSearch _hybridSearch;
    private readonly SessionManager _sessionManager;
    private readonly ILogger<MemoryManager>? _logger;
    private readonly string _memoryFolder;
    private readonly string _sessionsFolder;
    private bool _isInitialized;
    private MemoryStatus _status;

    /// <summary>
    /// Creates a new MemoryManager instance.
    /// </summary>
    public MemoryManager(
        string dataFolder,
        IEmbeddingProvider embeddingProvider,
        ChunkingOptions? chunkingOptions = null,
        ILogger<MemoryManager>? logger = null)
    {
        _memoryFolder = Path.Combine(dataFolder, "memory");
        _sessionsFolder = Path.Combine(dataFolder, "sessions");
        var dbPath = Path.Combine(dataFolder, "memory.db");

        // Ensure folders exist
        Directory.CreateDirectory(_memoryFolder);
        Directory.CreateDirectory(_sessionsFolder);

        // Initialize components
        _dbContext = new MemoryDbContext(dbPath);
        _embeddingProvider = embeddingProvider;
        _textChunker = new MarkdownChunker(chunkingOptions ?? new ChunkingOptions());
        _vectorSearch = new VectorSearch(_dbContext);
        _hybridSearch = new HybridSearch(_dbContext, _vectorSearch, _embeddingProvider);
        _sessionManager = new SessionManager(_sessionsFolder);
        _logger = logger;
        _status = new MemoryStatus
        {
            IsDirty = true,
            TotalChunks = 0,
            TotalFiles = 0,
            LastSyncAt = null
        };
    }

    /// <summary>
    /// Initializes the memory system.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
        {
            return;
        }

        _logger?.LogInformation("Initializing memory system...");

        // Create database and FTS tables
        await _dbContext.EnsureCreatedWithFtsAsync(cancellationToken);

        // Load embeddings into vector search
        await _vectorSearch.LoadIndexAsync(cancellationToken);

        // Update status
        _status.TotalFiles = await _dbContext.Files.CountAsync(cancellationToken);
        _status.TotalChunks = await _dbContext.Chunks.CountAsync(cancellationToken);
        _status.IsDirty = false;

        _isInitialized = true;
        _logger?.LogInformation("Memory system initialized");
    }

    /// <summary>
    /// Searches memory using hybrid search.
    /// </summary>
    public async Task<IReadOnlyList<MemorySearchResult>> SearchAsync(
        string query,
        MemorySearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        options ??= new MemorySearchOptions();

        _logger?.LogDebug("Searching memory for: {Query}", query);

        var results = await _hybridSearch.SearchAsync(query, options, cancellationToken);

        _logger?.LogDebug("Found {Count} results", results.Count);

        return results;
    }

    /// <summary>
    /// Synchronizes memory index with source files.
    /// </summary>
    public async Task SyncAsync(
        SyncOptions? options = null,
        IProgress<SyncProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        options ??= new SyncOptions();
        var syncProgress = new SyncProgress { Phase = "Scanning" };

        _logger?.LogInformation("Starting memory sync: {Reason}", options.Reason ?? "Manual sync");

        // Get files to process
        var memoryFiles = new List<string>();
        var sessionFiles = new List<string>();

        if (options.Sources == null || options.Sources.Contains(MemorySource.Memory))
        {
            memoryFiles = Directory.GetFiles(_memoryFolder, "*.*", SearchOption.AllDirectories)
                .Where(f => IsIndexableFile(f))
                .ToList();
        }

        if (options.Sources == null || options.Sources.Contains(MemorySource.Sessions))
        {
            sessionFiles = _sessionManager.GetSessionFilePaths().ToList();
        }

        syncProgress.TotalFiles = memoryFiles.Count + sessionFiles.Count;
        progress?.Report(syncProgress);

        // Process memory files
        syncProgress.Phase = "Indexing memory files";
        foreach (var file in memoryFiles)
        {
            syncProgress.CurrentFile = Path.GetFileName(file);
            progress?.Report(syncProgress);

            try
            {
                var (chunksCreated, embeddingsGenerated) = await IndexFileInternalAsync(
                    file, MemorySource.Memory, options.Force, cancellationToken);
                syncProgress.ChunksCreated += chunksCreated;
                syncProgress.EmbeddingsGenerated += embeddingsGenerated;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to index file: {Path}", file);
            }

            syncProgress.FilesProcessed++;
            progress?.Report(syncProgress);
        }

        // Process session files
        syncProgress.Phase = "Indexing sessions";
        foreach (var file in sessionFiles)
        {
            syncProgress.CurrentFile = Path.GetFileName(file);
            progress?.Report(syncProgress);

            try
            {
                var (chunksCreated, embeddingsGenerated) = await IndexFileInternalAsync(
                    file, MemorySource.Sessions, options.Force, cancellationToken);
                syncProgress.ChunksCreated += chunksCreated;
                syncProgress.EmbeddingsGenerated += embeddingsGenerated;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to index session: {Path}", file);
            }

            syncProgress.FilesProcessed++;
            progress?.Report(syncProgress);
        }

        // Reload vector index
        syncProgress.Phase = "Loading vector index";
        progress?.Report(syncProgress);
        await _vectorSearch.LoadIndexAsync(cancellationToken);

        // Update status
        _status.TotalFiles = await _dbContext.Files.CountAsync(cancellationToken);
        _status.TotalChunks = await _dbContext.Chunks.CountAsync(cancellationToken);
        _status.LastSyncAt = DateTime.UtcNow;
        _status.IsDirty = false;

        syncProgress.Phase = "Complete";
        progress?.Report(syncProgress);

        _logger?.LogInformation("Memory sync complete: {Files} files, {Chunks} chunks, {Embeddings} embeddings",
            syncProgress.FilesProcessed, syncProgress.ChunksCreated, syncProgress.EmbeddingsGenerated);
    }

    /// <summary>
    /// Warms up the memory index for a session.
    /// </summary>
    public async Task WarmSessionAsync(
        string? sessionKey = null,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        _logger?.LogDebug("Warming session: {SessionKey}", sessionKey ?? "all");

        // Ensure vector index is loaded
        await _vectorSearch.LoadIndexAsync(cancellationToken);

        // If a specific session is requested, ensure it's indexed
        if (!string.IsNullOrEmpty(sessionKey))
        {
            var sessionPath = Path.Combine(_sessionsFolder, $"{sessionKey}.json");
            if (File.Exists(sessionPath))
            {
                await IndexFileInternalAsync(sessionPath, MemorySource.Sessions, false, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Gets the current status of the memory index.
    /// </summary>
    public MemoryStatus GetStatus()
    {
        return _status;
    }

    /// <summary>
    /// Saves a session transcript.
    /// </summary>
    public async Task SaveSessionAsync(
        SessionTranscript session,
        CancellationToken cancellationToken = default)
    {
        await _sessionManager.SaveSessionAsync(session, cancellationToken);

        // Index the session if initialized
        if (_isInitialized)
        {
            var sessionPath = Path.Combine(_sessionsFolder, $"{session.SessionKey}.json");
            if (File.Exists(sessionPath))
            {
                await IndexFileInternalAsync(sessionPath, MemorySource.Sessions, true, cancellationToken);
                await _vectorSearch.LoadIndexAsync(cancellationToken);
            }
        }
    }

    /// <summary>
    /// Loads a session transcript.
    /// </summary>
    public async Task<SessionTranscript?> LoadSessionAsync(
        string sessionKey,
        CancellationToken cancellationToken = default)
    {
        return await _sessionManager.LoadSessionAsync(sessionKey, cancellationToken);
    }

    /// <summary>
    /// Lists all sessions.
    /// </summary>
    public async Task<IReadOnlyList<SessionSummary>> ListSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        return await _sessionManager.ListSessionsAsync(cancellationToken);
    }

    /// <summary>
    /// Clears the memory index.
    /// </summary>
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        _logger?.LogWarning("Clearing all memory data...");

        _dbContext.Chunks.RemoveRange(_dbContext.Chunks);
        _dbContext.Files.RemoveRange(_dbContext.Files);
        _dbContext.EmbeddingCache.RemoveRange(_dbContext.EmbeddingCache);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _vectorSearch.ClearIndex();

        _status.TotalFiles = 0;
        _status.TotalChunks = 0;

        _logger?.LogInformation("All memory data cleared");
    }

    /// <summary>
    /// Adds a memory entry directly.
    /// </summary>
    public async Task AddMemoryAsync(
        string text,
        MemorySource source,
        string? path = null,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        path ??= $"memory_{DateTime.UtcNow:yyyyMMdd_HHmmss}.txt";
        var fullPath = Path.Combine(_memoryFolder, path);

        // Ensure directory exists
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Write the text to a file
        await File.WriteAllTextAsync(fullPath, text, cancellationToken);

        // Index the file
        await IndexFileInternalAsync(fullPath, source, true, cancellationToken);
        await _vectorSearch.LoadIndexAsync(cancellationToken);

        _logger?.LogInformation("Added memory: {Path}", path);
    }

    /// <summary>
    /// Gets the memory folder path.
    /// </summary>
    public string MemoryFolder => _memoryFolder;

    /// <summary>
    /// Gets the sessions folder path.
    /// </summary>
    public string SessionsFolder => _sessionsFolder;

    private void EnsureInitialized()
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("Memory manager not initialized. Call InitializeAsync first.");
        }
    }

    private async Task<(int ChunksCreated, int EmbeddingsGenerated)> IndexFileInternalAsync(
        string filePath,
        MemorySource source,
        bool force,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return (0, 0);
        }

        var relativePath = GetRelativePath(filePath, source);
        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        var fileInfo = new FileInfo(filePath);
        var hash = ComputeHash(content);

        _logger?.LogDebug("Indexing file: {Path}", relativePath);

        // Check if file already exists and is up to date
        var existingFile = await _dbContext.Files
            .FirstOrDefaultAsync(f => f.Path == relativePath, cancellationToken);

        if (existingFile != null)
        {
            if (!force && existingFile.Hash == hash)
            {
                _logger?.LogDebug("File {Path} is already up to date", relativePath);
                return (0, 0);
            }

            // Remove old chunks
            await RemoveFileChunksAsync(existingFile.Id, cancellationToken);
            existingFile.Hash = hash;
            existingFile.ModifiedTime = new DateTimeOffset(fileInfo.LastWriteTimeUtc).ToUnixTimeSeconds();
            existingFile.Size = fileInfo.Length;
            existingFile.IndexedAt = DateTime.UtcNow;
        }
        else
        {
            existingFile = new MemoryFile
            {
                Path = relativePath,
                Source = source == MemorySource.Sessions ? "sessions" : "memory",
                Hash = hash,
                ModifiedTime = new DateTimeOffset(fileInfo.LastWriteTimeUtc).ToUnixTimeSeconds(),
                Size = fileInfo.Length,
                IndexedAt = DateTime.UtcNow
            };
            _dbContext.Files.Add(existingFile);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        // Chunk the content
        var chunks = _textChunker.ChunkText(content, relativePath);

        _logger?.LogDebug("Created {Count} chunks for {Path}", chunks.Count, relativePath);

        var embeddingsGenerated = 0;

        // Generate embeddings and save chunks
        foreach (var chunk in chunks)
        {
            var embedding = await _embeddingProvider.GenerateEmbeddingAsync(chunk.Text, cancellationToken);
            embeddingsGenerated++;

            var memoryChunk = new MemoryChunk
            {
                FileId = existingFile.Id,
                Path = relativePath,
                Source = existingFile.Source,
                Text = chunk.Text,
                StartLine = chunk.StartLine,
                EndLine = chunk.EndLine,
                Embedding = SerializeEmbedding(embedding),
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.Chunks.Add(memoryChunk);
            
            // Add to vector index
            _vectorSearch.AddToIndex(memoryChunk.Id, embedding);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger?.LogInformation("Indexed file: {Path} ({Count} chunks)", relativePath, chunks.Count);

        return (chunks.Count, embeddingsGenerated);
    }

    private async Task RemoveFileChunksAsync(int fileId, CancellationToken cancellationToken)
    {
        var chunks = await _dbContext.Chunks
            .Where(c => c.FileId == fileId)
            .ToListAsync(cancellationToken);

        foreach (var chunk in chunks)
        {
            _vectorSearch.RemoveFromIndex(chunk.Id);
        }

        _dbContext.Chunks.RemoveRange(chunks);
    }

    private string GetRelativePath(string filePath, MemorySource source)
    {
        var baseFolder = source == MemorySource.Sessions ? _sessionsFolder : _memoryFolder;
        
        if (filePath.StartsWith(baseFolder, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetRelativePath(baseFolder, filePath);
        }

        return filePath;
    }

    private static bool IsIndexableFile(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".md" => true,
            ".txt" => true,
            ".json" => true,
            ".yaml" => true,
            ".yml" => true,
            ".xml" => true,
            ".cs" => true,
            ".py" => true,
            ".js" => true,
            ".ts" => true,
            ".html" => true,
            ".css" => true,
            ".sql" => true,
            ".sh" => true,
            ".ps1" => true,
            ".bat" => true,
            ".cmd" => true,
            _ => false
        };
    }

    private static string ComputeHash(string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var hash = XxHash64.Hash(bytes);
        return Convert.ToHexString(hash);
    }

    private static byte[] SerializeEmbedding(float[] embedding)
    {
        var bytes = new byte[embedding.Length * sizeof(float)];
        Buffer.BlockCopy(embedding, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
