namespace Microbot.Memory.Sync;

using Microsoft.Extensions.Logging;
using Microbot.Memory.Interfaces;

/// <summary>
/// Service that watches for file changes and triggers memory sync.
/// </summary>
public class MemorySyncService : IDisposable
{
    private readonly IMemoryManager _memoryManager;
    private readonly ILogger<MemorySyncService>? _logger;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly HashSet<string> _pendingChanges = [];
    private Timer? _debounceTimer;
    private bool _isDisposed;

    /// <summary>
    /// Debounce delay in milliseconds.
    /// </summary>
    public int DebounceDelayMs { get; set; } = 2000;

    /// <summary>
    /// Creates a new MemorySyncService.
    /// </summary>
    public MemorySyncService(
        IMemoryManager memoryManager,
        ILogger<MemorySyncService>? logger = null)
    {
        _memoryManager = memoryManager;
        _logger = logger;
    }

    /// <summary>
    /// Starts watching the specified folders for changes.
    /// </summary>
    public void StartWatching(params string[] folders)
    {
        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder))
            {
                _logger?.LogWarning("Folder does not exist, skipping: {Folder}", folder);
                continue;
            }

            var watcher = new FileSystemWatcher(folder)
            {
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            watcher.Created += OnFileChanged;
            watcher.Changed += OnFileChanged;
            watcher.Deleted += OnFileChanged;
            watcher.Renamed += OnFileRenamed;
            watcher.Error += OnWatcherError;

            _watchers.Add(watcher);
            _logger?.LogInformation("Started watching folder: {Folder}", folder);
        }
    }

    /// <summary>
    /// Stops watching all folders.
    /// </summary>
    public void StopWatching()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();
        _logger?.LogInformation("Stopped watching all folders");
    }

    /// <summary>
    /// Triggers an immediate sync.
    /// </summary>
    public async Task TriggerSyncAsync(
        string? reason = null,
        IProgress<SyncProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            _pendingChanges.Clear();
            await _memoryManager.SyncAsync(
                new SyncOptions { Reason = reason ?? "Manual trigger" },
                progress,
                cancellationToken);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (!IsIndexableFile(e.FullPath))
        {
            return;
        }

        _logger?.LogDebug("File changed: {Path} ({ChangeType})", e.FullPath, e.ChangeType);
        QueueSync(e.FullPath);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        if (!IsIndexableFile(e.FullPath) && !IsIndexableFile(e.OldFullPath))
        {
            return;
        }

        _logger?.LogDebug("File renamed: {OldPath} -> {NewPath}", e.OldFullPath, e.FullPath);
        QueueSync(e.FullPath);
        QueueSync(e.OldFullPath);
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _logger?.LogError(e.GetException(), "File watcher error");
    }

    private void QueueSync(string path)
    {
        lock (_pendingChanges)
        {
            _pendingChanges.Add(path);
        }

        // Reset debounce timer
        _debounceTimer?.Dispose();
        _debounceTimer = new Timer(
            _ => _ = ExecuteDebouncedSyncAsync(),
            null,
            DebounceDelayMs,
            Timeout.Infinite);
    }

    private async Task ExecuteDebouncedSyncAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        HashSet<string> changes;
        lock (_pendingChanges)
        {
            if (_pendingChanges.Count == 0)
            {
                return;
            }
            changes = new HashSet<string>(_pendingChanges);
            _pendingChanges.Clear();
        }

        _logger?.LogInformation("Syncing {Count} changed files", changes.Count);

        try
        {
            await _syncLock.WaitAsync();
            try
            {
                await _memoryManager.SyncAsync(
                    new SyncOptions { Reason = $"File changes detected ({changes.Count} files)" });
            }
            finally
            {
                _syncLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error during debounced sync");
        }
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

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _debounceTimer?.Dispose();
        StopWatching();
        _syncLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
