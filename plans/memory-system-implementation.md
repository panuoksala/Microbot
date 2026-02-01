# Memory System Implementation Plan

## Overview

This document describes the long-term memory system implementation for Microbot, inspired by OpenClaw's memory architecture. The system provides persistent memory across sessions, enabling the AI agent to recall past conversations and stored information.

## Status: ✅ COMPLETED

All phases have been implemented and the system is ready for use.

## Architecture

### Core Components

```
┌─────────────────────────────────────────────────────────────────┐
│                        Microbot.Memory                          │
├─────────────────────────────────────────────────────────────────┤
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────────┐ │
│  │MemoryManager│  │MemorySkill  │  │MemorySyncService        │ │
│  │(Orchestrator)│  │(SK Plugin)  │  │(FileSystemWatcher)      │ │
│  └──────┬──────┘  └──────┬──────┘  └───────────┬─────────────┘ │
│         │                │                      │               │
│  ┌──────┴──────────────────────────────────────┴──────────────┐│
│  │                    Core Services                            ││
│  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────────┐  ││
│  │  │MarkdownChunker│  │HybridSearch  │  │SessionManager    │  ││
│  │  │(Text Chunking)│  │(Vector+FTS5) │  │(Transcripts)     │  ││
│  │  └──────────────┘  └──────────────┘  └──────────────────┘  ││
│  └────────────────────────────────────────────────────────────┘│
│                                                                 │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │                  Embedding Providers                        ││
│  │  ┌──────────┐  ┌──────────────┐  ┌────────────────────┐    ││
│  │  │ OpenAI   │  │ Azure OpenAI │  │ Ollama             │    ││
│  │  └──────────┘  └──────────────┘  └────────────────────┘    ││
│  └─────────────────────────────────────────────────────────────┘│
│                                                                 │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │                    Data Layer                               ││
│  │  ┌──────────────────────────────────────────────────────┐  ││
│  │  │ SQLite + EF Core                                      │  ││
│  │  │ - MemoryFile (indexed files)                          │  ││
│  │  │ - MemoryChunk (text chunks with embeddings)           │  ││
│  │  │ - EmbeddingCache (cached embeddings)                  │  ││
│  │  │ - MemoryMeta (metadata)                               │  ││
│  │  │ - FTS5 Virtual Table (full-text search)               │  ││
│  │  └──────────────────────────────────────────────────────┘  ││
│  └─────────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────────┘
```

### Data Flow

1. **Indexing Flow**:
   - Files in `memory/` folder are watched by `MemorySyncService`
   - Changes trigger `MemoryManager.SyncAsync()`
   - Files are chunked by `MarkdownChunker` (markdown-aware, token-based)
   - Chunks are embedded using configured provider (OpenAI/Azure/Ollama)
   - Chunks and embeddings stored in SQLite database

2. **Search Flow**:
   - User query → `MemoryManager.SearchAsync()`
   - Query embedded using same provider
   - `HybridSearch` combines:
     - Vector similarity (cosine distance) - 70% weight
     - FTS5 BM25 text search - 30% weight
   - Results ranked and returned

3. **Session Flow**:
   - Conversations tracked in `SessionTranscript`
   - Sessions saved to `sessions/` folder as JSON
   - Sessions indexed for search like memory files

## Implementation Details

### Files Created

```
src/Microbot.Memory/
├── Microbot.Memory.csproj
├── MemoryManager.cs              # Main orchestrator
├── MemoryManagerFactory.cs       # Factory for creating instances
├── MemorySource.cs               # Enum: Memory, Sessions
├── MemorySearchResult.cs         # Search result model
├── MemoryStatus.cs               # Status model
├── Chunking/
│   ├── ChunkingOptions.cs
│   └── MarkdownChunker.cs        # Markdown-aware chunking
├── Data/
│   ├── MemoryDbContext.cs        # EF Core context
│   └── Entities/
│       ├── MemoryFile.cs
│       ├── MemoryChunk.cs
│       ├── EmbeddingCache.cs
│       └── MemoryMeta.cs
├── Embeddings/
│   ├── OpenAIEmbeddingProvider.cs
│   ├── AzureOpenAIEmbeddingProvider.cs
│   ├── OllamaEmbeddingProvider.cs
│   └── EmbeddingProviderFactory.cs
├── Interfaces/
│   ├── IMemoryManager.cs
│   ├── IEmbeddingProvider.cs
│   ├── ITextChunker.cs
│   ├── MemorySearchOptions.cs
│   ├── SyncOptions.cs
│   ├── SyncProgress.cs
│   └── SessionSummary.cs
├── Search/
│   ├── VectorSearch.cs           # Cosine similarity
│   └── HybridSearch.cs           # Combined vector + FTS5
├── Sessions/
│   ├── SessionTranscript.cs
│   ├── TranscriptEntry.cs
│   └── SessionManager.cs
├── Skills/
│   └── MemorySkill.cs            # Semantic Kernel plugin
└── Sync/
    └── MemorySyncService.cs      # FileSystemWatcher
```

### Configuration

Added to `MicrobotConfig`:

```csharp
public class MemoryConfig
{
    public bool Enabled { get; set; } = true;
    public string DatabasePath { get; set; } = "./memory/microbot-memory.db";
    public string MemoryFolder { get; set; } = "./memory";
    public string SessionsFolder { get; set; } = "./memory/sessions";
    public EmbeddingConfig Embedding { get; set; } = new();
    public ChunkingConfig Chunking { get; set; } = new();
    public MemorySearchConfig Search { get; set; } = new();
    public MemorySyncConfig Sync { get; set; } = new();
}
```

### Console Commands

Added `/memory` command with subcommands:
- `/memory status` - Show memory system status
- `/memory sync` - Sync memory index
- `/memory sync --force` - Force full re-index
- `/memory search <query>` - Search memory
- `/memory sessions` - List recent sessions
- `/memory save` - Save current session

### Agent Integration

- Memory initialized in `AgentService.InitializeAsync()`
- `MemorySkill` registered with Semantic Kernel
- Sessions tracked and saved automatically
- Memory context available to AI agent

## Key Technologies

| Component | Technology | Purpose |
|-----------|------------|---------|
| Database | SQLite + EF Core | Persistent storage |
| Full-text Search | SQLite FTS5 | BM25 text search |
| Vector Search | In-memory cosine | Semantic similarity |
| Tokenization | ML.Tokenizers (cl100k_base) | GPT-4 compatible token counting |
| Markdown Parsing | Markdig | Markdown-aware chunking |
| Embeddings | OpenAI/Azure/Ollama | Text embeddings |

## Usage

### Enable Memory System

Memory is enabled by default. To configure:

```json
{
  "memory": {
    "enabled": true,
    "memoryFolder": "./memory",
    "sessionsFolder": "./memory/sessions",
    "embedding": {
      "modelId": "text-embedding-3-small"
    },
    "chunking": {
      "maxTokens": 512,
      "overlapTokens": 50,
      "markdownAware": true
    },
    "search": {
      "maxResults": 10,
      "minScore": 0.5,
      "vectorWeight": 0.7,
      "textWeight": 0.3
    },
    "sync": {
      "enableFileWatching": true,
      "debounceMs": 1000
    }
  }
}
```

### Store Information

1. **Manual**: Create `.md` files in the `memory/` folder
2. **Via Agent**: Ask the agent to "remember" something
3. **Automatic**: Sessions are saved automatically

### Search Memory

1. **Via Command**: `/memory search <query>`
2. **Via Agent**: Ask the agent to "recall" or "search memory"
3. **Automatic**: Agent can search memory when relevant

## Future Enhancements

- [ ] Memory compaction/summarization
- [ ] Memory importance scoring
- [ ] Memory expiration/cleanup
- [ ] Memory export/import
- [ ] Memory visualization
- [ ] Multi-user memory isolation
