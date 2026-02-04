# Scheduling Skill Implementation Plan

## Overview

This document describes the implementation plan for a task scheduling skill in Microbot. The scheduling skill allows users to schedule commands to run at specific times or intervals (e.g., daily, weekly) or as one-time tasks at a specific date/time. Schedules are stored in an internal SQLite database and can be managed via console commands.

## Status: 🔲 PLANNED

## Requirements

1. **Schedule Storage**: Store schedules in an internal SQLite database
2. **Schedule Management**: Commands to view, add, and remove schedules
3. **Auto-generated IDs**: Each schedule has an auto-generated ID
4. **Command-based Operations**: Use ID for operations like `/schedule remove 1`
5. **Recurring Schedules**: Support for various schedule patterns (daily, weekly, etc.)
6. **One-time Schedules**: Support for running a task once at a specific date/time
7. **Skill Integration**: Implement as a Semantic Kernel skill

## Architecture

### Component Diagram

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        Microbot.Skills.Scheduling                        │
├─────────────────────────────────────────────────────────────────────────┤
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────────────┐  │
│  │ ScheduleSkill   │  │ ScheduleService │  │ ScheduleExecutor        │  │
│  │ (SK Plugin)     │  │ (CRUD Ops)      │  │ (Background Runner)     │  │
│  └────────┬────────┘  └────────┬────────┘  └───────────┬─────────────┘  │
│           │                    │                        │                │
│  ┌────────┴────────────────────┴────────────────────────┴──────────────┐ │
│  │                         Data Layer                                   │ │
│  │  ┌──────────────────────────────────────────────────────────────┐   │ │
│  │  │ ScheduleDbContext (EF Core)                                   │   │ │
│  │  │ - Schedule (id, name, command, cron, enabled, last_run, etc.) │   │ │
│  │  │ - ScheduleExecution (execution history)                       │   │ │
│  │  └──────────────────────────────────────────────────────────────┘   │ │
│  └─────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────┘
```

### Data Flow

1. **Adding a Schedule**:
   - User issues command: `/schedule add "Check emails" "every day at 9am" "Check my inbox for important emails"`
   - ScheduleService parses the schedule expression and creates a Schedule entity
   - Schedule is stored in SQLite database with auto-generated ID
   - Confirmation returned to user with the schedule ID

2. **Executing Schedules**:
   - ScheduleExecutor runs as a background service
   - Periodically checks for due schedules (every minute)
   - When a schedule is due, sends the command to the agent
   - Records execution in ScheduleExecution table
   - Updates last_run timestamp on Schedule

3. **Managing Schedules**:
   - `/schedule list` - Shows all schedules with IDs
   - `/schedule remove <id>` - Removes schedule by ID
   - `/schedule enable <id>` - Enables a disabled schedule
   - `/schedule disable <id>` - Disables a schedule without removing it

## Database Schema

### Schedule Table

```sql
CREATE TABLE schedules (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    command TEXT NOT NULL,
    schedule_type TEXT NOT NULL,        -- 'recurring' or 'once'
    cron_expression TEXT,               -- NULL for one-time schedules
    run_at TEXT,                        -- Specific datetime for one-time schedules
    description TEXT,
    enabled INTEGER NOT NULL DEFAULT 1,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    last_run_at TEXT,
    next_run_at TEXT,
    run_count INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX idx_schedules_enabled ON schedules(enabled);
CREATE INDEX idx_schedules_next_run ON schedules(next_run_at);
CREATE INDEX idx_schedules_type ON schedules(schedule_type);
```

### ScheduleExecution Table (History)

```sql
CREATE TABLE schedule_executions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    schedule_id INTEGER NOT NULL,
    started_at TEXT NOT NULL,
    completed_at TEXT,
    status TEXT NOT NULL,  -- 'running', 'completed', 'failed', 'timeout'
    result TEXT,
    error_message TEXT,
    FOREIGN KEY (schedule_id) REFERENCES schedules(id) ON DELETE CASCADE
);

CREATE INDEX idx_executions_schedule ON schedule_executions(schedule_id);
CREATE INDEX idx_executions_started ON schedule_executions(started_at);
```

## Entity Models

### Schedule Entity

```csharp
namespace Microbot.Skills.Scheduling.Database.Entities;

public class Schedule
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public ScheduleType Type { get; set; } = ScheduleType.Recurring;
    public string? CronExpression { get; set; }      // For recurring schedules
    public DateTime? RunAt { get; set; }              // For one-time schedules
    public string? Description { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastRunAt { get; set; }
    public DateTime? NextRunAt { get; set; }
    public int RunCount { get; set; }
    
    public ICollection<ScheduleExecution> Executions { get; set; } = [];
    
    /// <summary>
    /// Returns true if this is a one-time schedule that has already run.
    /// </summary>
    public bool IsCompleted => Type == ScheduleType.Once && RunCount > 0;
}

public enum ScheduleType
{
    /// <summary>
    /// Recurring schedule using cron expression.
    /// </summary>
    Recurring,
    
    /// <summary>
    /// One-time schedule that runs once at a specific date/time.
    /// </summary>
    Once
}
```

### ScheduleExecution Entity

```csharp
namespace Microbot.Skills.Scheduling.Database.Entities;

public class ScheduleExecution
{
    public int Id { get; set; }
    public int ScheduleId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public ExecutionStatus Status { get; set; }
    public string? Result { get; set; }
    public string? ErrorMessage { get; set; }
    
    public Schedule Schedule { get; set; } = null!;
}

public enum ExecutionStatus
{
    Running,
    Completed,
    Failed,
    Timeout
}
```

## Schedule Expression Parsing

Support cron expressions, natural language for recurring schedules, and specific date/time for one-time schedules:

### Recurring Schedules (Cron Expression Support)
- Standard 5-field cron: `minute hour day month weekday`
- Examples:
  - `0 9 * * *` - Every day at 9:00 AM
  - `0 9 * * 1` - Every Monday at 9:00 AM
  - `0 */2 * * *` - Every 2 hours
  - `30 8 1 * *` - 8:30 AM on the 1st of every month

### Recurring Schedules (Natural Language → Cron)
- `every day at 9am` → `0 9 * * *`
- `every monday at 10:30` → `30 10 * * 1`
- `every hour` → `0 * * * *`
- `every 30 minutes` → `*/30 * * * *`
- `every week on friday at 5pm` → `0 17 * * 5`
- `every month on the 1st at noon` → `0 12 1 * *`

### One-Time Schedules (Specific Date/Time)
- `once at 2024-02-15 14:30` → Runs once at Feb 15, 2024 2:30 PM
- `once tomorrow at 9am` → Runs once tomorrow at 9:00 AM
- `once on friday at 3pm` → Runs once on the next Friday at 3:00 PM
- `once in 2 hours` → Runs once 2 hours from now
- `once at 5pm` → Runs once today at 5:00 PM (or tomorrow if already past)
- `2024-02-15 14:30` → Detected as one-time (no "every" keyword)

### Detection Logic
The parser determines schedule type based on:
1. If expression starts with "once" → One-time schedule
2. If expression starts with "every" → Recurring schedule
3. If expression is a valid cron pattern → Recurring schedule
4. If expression is a date/time without "every" → One-time schedule

### Implementation

```csharp
// NuGet: Cronos
using Cronos;

public class ScheduleExpressionParser
{
    public record ParsedSchedule(
        ScheduleType Type,
        string? CronExpression,
        DateTime? RunAt,
        string OriginalExpression);
    
    public static ParsedSchedule Parse(string expression, TimeZoneInfo timeZone)
    {
        expression = expression.Trim();
        
        // Check for one-time schedule indicators
        if (expression.StartsWith("once ", StringComparison.OrdinalIgnoreCase))
        {
            var dateTime = ParseOneTimeExpression(expression[5..], timeZone);
            return new ParsedSchedule(ScheduleType.Once, null, dateTime, expression);
        }
        
        // Check for recurring schedule indicators
        if (expression.StartsWith("every ", StringComparison.OrdinalIgnoreCase))
        {
            var cron = ParseRecurringNaturalLanguage(expression);
            return new ParsedSchedule(ScheduleType.Recurring, cron, null, expression);
        }
        
        // Try parsing as cron expression
        if (TryParseCron(expression, out var cronExpr))
        {
            return new ParsedSchedule(ScheduleType.Recurring, expression, null, expression);
        }
        
        // Try parsing as date/time (one-time)
        if (TryParseDateTime(expression, timeZone, out var dateTime))
        {
            return new ParsedSchedule(ScheduleType.Once, null, dateTime, expression);
        }
        
        throw new ArgumentException($"Unable to parse schedule expression: {expression}");
    }
    
    private static DateTime ParseOneTimeExpression(string expression, TimeZoneInfo timeZone)
    {
        // Handle relative expressions
        if (expression.StartsWith("in ", StringComparison.OrdinalIgnoreCase))
        {
            return ParseRelativeTime(expression[3..]);
        }
        
        // Handle "tomorrow at X"
        if (expression.StartsWith("tomorrow", StringComparison.OrdinalIgnoreCase))
        {
            return ParseTomorrowAt(expression, timeZone);
        }
        
        // Handle "on <day> at X"
        if (expression.StartsWith("on ", StringComparison.OrdinalIgnoreCase))
        {
            return ParseOnDayAt(expression[3..], timeZone);
        }
        
        // Handle "at X" (today or tomorrow)
        if (expression.StartsWith("at ", StringComparison.OrdinalIgnoreCase))
        {
            return ParseAtTime(expression[3..], timeZone);
        }
        
        // Try parsing as absolute date/time
        return ParseAbsoluteDateTime(expression, timeZone);
    }
    
    public static DateTime? GetNextOccurrence(Schedule schedule, DateTime from, TimeZoneInfo timeZone)
    {
        if (schedule.Type == ScheduleType.Once)
        {
            // One-time: return RunAt if not yet run, null otherwise
            return schedule.RunCount == 0 ? schedule.RunAt : null;
        }
        
        // Recurring: use Cronos
        var cron = CronExpression.Parse(schedule.CronExpression!);
        return cron.GetNextOccurrence(from, timeZone);
    }
}
```

## Console Commands

### Command Structure

| Command | Description |
|---------|-------------|
| `/schedule` | Show help for schedule commands |
| `/schedule list` | List all schedules with IDs, status, and next run time |
| `/schedule add <name> <schedule> <command>` | Add a new schedule |
| `/schedule remove <id>` | Remove a schedule by ID |
| `/schedule enable <id>` | Enable a disabled schedule |
| `/schedule disable <id>` | Disable a schedule without removing |
| `/schedule run <id>` | Manually run a schedule immediately |
| `/schedule history [id]` | Show execution history (optionally for specific schedule) |

### Command Examples

```
/schedule list
┌────┬─────────────────┬──────────────────┬──────────┬─────────┬─────────────────────┐
│ ID │ Name            │ Schedule         │ Type     │ Status  │ Next Run            │
├────┼─────────────────┼──────────────────┼──────────┼─────────┼─────────────────────┤
│ 1  │ Morning Emails  │ every day at 9am │ Recurring│ Enabled │ 2024-02-05 09:00:00 │
│ 2  │ Weekly Report   │ every monday 10am│ Recurring│ Enabled │ 2024-02-05 10:00:00 │
│ 3  │ Backup Reminder │ every friday 5pm │ Recurring│ Disabled│ -                   │
│ 4  │ Deploy Release  │ once at 2024-02-1│ One-time │ Pending │ 2024-02-15 14:00:00 │
│ 5  │ Meeting Prep    │ once tomorrow 9am│ One-time │ Done    │ -                   │
└────┴─────────────────┴──────────────────┴──────────┴─────────┴─────────────────────┘

# Add a recurring schedule
/schedule add "Morning Emails" "every day at 9am" "Check my inbox and summarize important emails"
✓ Schedule created with ID: 6
  Name: Morning Emails
  Type: Recurring
  Schedule: every day at 9am (0 9 * * *)
  Command: Check my inbox and summarize important emails
  Next run: 2024-02-05 09:00:00

# Add a one-time schedule
/schedule add "Deploy Release" "once at 2024-02-15 14:00" "Remind me to deploy the release to production"
✓ Schedule created with ID: 7
  Name: Deploy Release
  Type: One-time
  Scheduled for: 2024-02-15 14:00:00
  Command: Remind me to deploy the release to production

# Add a one-time schedule with relative time
/schedule add "Follow Up" "once in 2 hours" "Follow up on the email I sent to John"
✓ Schedule created with ID: 8
  Name: Follow Up
  Type: One-time
  Scheduled for: 2024-02-04 16:30:00 (in 2 hours)
  Command: Follow up on the email I sent to John

# Add a one-time schedule for tomorrow
/schedule add "Morning Review" "once tomorrow at 8am" "Review yesterday's work and plan today"
✓ Schedule created with ID: 9
  Name: Morning Review
  Type: One-time
  Scheduled for: 2024-02-05 08:00:00
  Command: Review yesterday's work and plan today

/schedule remove 3
✓ Schedule 'Backup Reminder' (ID: 3) has been removed.

/schedule disable 2
✓ Schedule 'Weekly Report' (ID: 2) has been disabled.

/schedule run 1
Running schedule 'Morning Emails'...
[Agent executes the command and shows response]
```

## ScheduleSkill (Semantic Kernel Plugin)

The AI agent can also manage schedules through natural conversation:

```csharp
namespace Microbot.Skills.Scheduling;

public class ScheduleSkill
{
    private readonly IScheduleService _scheduleService;
    
    [KernelFunction("list_schedules")]
    [Description("List all scheduled tasks, both recurring and one-time")]
    public async Task<string> ListSchedulesAsync(
        [Description("Filter by status: 'all', 'enabled', 'disabled', 'pending' (one-time not yet run), 'completed' (one-time already run)")] string? status = "all",
        [Description("Filter by type: 'all', 'recurring', 'once'")] string? type = "all",
        CancellationToken cancellationToken = default)
    {
        // Implementation
    }
    
    [KernelFunction("create_recurring_schedule")]
    [Description("Create a recurring scheduled task that runs repeatedly at specified times")]
    public async Task<string> CreateRecurringScheduleAsync(
        [Description("Name for the schedule")] string name,
        [Description("When to run repeatedly (e.g., 'every day at 9am', 'every monday at 10:30', 'every hour', or cron expression like '0 9 * * *')")] string schedule,
        [Description("The command/task to execute when the schedule runs")] string command,
        [Description("Optional description of what this schedule does")] string? description = null,
        CancellationToken cancellationToken = default)
    {
        // Implementation
    }
    
    [KernelFunction("create_onetime_schedule")]
    [Description("Create a one-time scheduled task that runs once at a specific date/time")]
    public async Task<string> CreateOneTimeScheduleAsync(
        [Description("Name for the schedule")] string name,
        [Description("When to run once (e.g., 'tomorrow at 9am', 'in 2 hours', '2024-02-15 14:00', 'on friday at 3pm')")] string when,
        [Description("The command/task to execute when the schedule runs")] string command,
        [Description("Optional description of what this schedule does")] string? description = null,
        CancellationToken cancellationToken = default)
    {
        // Implementation - prepends "once " to the when expression if not present
    }
    
    [KernelFunction("remove_schedule")]
    [Description("Remove a scheduled task by its ID")]
    public async Task<string> RemoveScheduleAsync(
        [Description("The ID of the schedule to remove")] int scheduleId,
        CancellationToken cancellationToken = default)
    {
        // Implementation
    }
    
    [KernelFunction("enable_schedule")]
    [Description("Enable a disabled scheduled task")]
    public async Task<string> EnableScheduleAsync(
        [Description("The ID of the schedule to enable")] int scheduleId,
        CancellationToken cancellationToken = default)
    {
        // Implementation
    }
    
    [KernelFunction("disable_schedule")]
    [Description("Disable a scheduled task without removing it")]
    public async Task<string> DisableScheduleAsync(
        [Description("The ID of the schedule to disable")] int scheduleId,
        CancellationToken cancellationToken = default)
    {
        // Implementation
    }
    
    [KernelFunction("get_schedule_history")]
    [Description("Get execution history for a scheduled task")]
    public async Task<string> GetScheduleHistoryAsync(
        [Description("The ID of the schedule")] int scheduleId,
        [Description("Maximum number of history entries to return")] int maxEntries = 10,
        CancellationToken cancellationToken = default)
    {
        // Implementation
    }
}
```

## Background Scheduler Service

The scheduler runs as a background service that checks for due schedules:

```csharp
namespace Microbot.Skills.Scheduling.Services;

public class ScheduleExecutorService : IDisposable
{
    private readonly IScheduleService _scheduleService;
    private readonly Func<string, CancellationToken, Task<string>> _executeCommand;
    private readonly ILogger<ScheduleExecutorService>? _logger;
    private readonly Timer _timer;
    private readonly TimeZoneInfo _timeZone;
    private bool _isRunning;
    
    public ScheduleExecutorService(
        IScheduleService scheduleService,
        Func<string, CancellationToken, Task<string>> executeCommand,
        TimeZoneInfo? timeZone = null,
        ILogger<ScheduleExecutorService>? logger = null)
    {
        _scheduleService = scheduleService;
        _executeCommand = executeCommand;
        _timeZone = timeZone ?? TimeZoneInfo.Local;
        _logger = logger;
        
        // Check every minute
        _timer = new Timer(CheckSchedules, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
    }
    
    private async void CheckSchedules(object? state)
    {
        if (_isRunning) return;
        _isRunning = true;
        
        try
        {
            var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone);
            var dueSchedules = await _scheduleService.GetDueSchedulesAsync(now);
            
            foreach (var schedule in dueSchedules)
            {
                await ExecuteScheduleAsync(schedule);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error checking schedules");
        }
        finally
        {
            _isRunning = false;
        }
    }
    
    private async Task ExecuteScheduleAsync(Schedule schedule)
    {
        var execution = await _scheduleService.StartExecutionAsync(schedule.Id);
        
        try
        {
            _logger?.LogInformation("Executing schedule {Id}: {Name}", schedule.Id, schedule.Name);
            
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            var result = await _executeCommand(schedule.Command, cts.Token);
            
            await _scheduleService.CompleteExecutionAsync(execution.Id, result);
            _logger?.LogInformation("Schedule {Id} completed successfully", schedule.Id);
        }
        catch (OperationCanceledException)
        {
            await _scheduleService.FailExecutionAsync(execution.Id, ExecutionStatus.Timeout, "Execution timed out");
            _logger?.LogWarning("Schedule {Id} timed out", schedule.Id);
        }
        catch (Exception ex)
        {
            await _scheduleService.FailExecutionAsync(execution.Id, ExecutionStatus.Failed, ex.Message);
            _logger?.LogError(ex, "Schedule {Id} failed", schedule.Id);
        }
    }
    
    public void Dispose()
    {
        _timer.Dispose();
    }
}
```

## Configuration

Add to `MicrobotConfig`:

```csharp
/// <summary>
/// Configuration for the scheduling skill.
/// </summary>
public class SchedulingConfig
{
    /// <summary>
    /// Whether the scheduling skill is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>
    /// Path to the SQLite database file for schedules.
    /// </summary>
    public string DatabasePath { get; set; } = "./schedules.db";
    
    /// <summary>
    /// How often to check for due schedules (in seconds).
    /// </summary>
    public int CheckIntervalSeconds { get; set; } = 60;
    
    /// <summary>
    /// Maximum execution time for a scheduled task (in seconds).
    /// </summary>
    public int ExecutionTimeoutSeconds { get; set; } = 600;
    
    /// <summary>
    /// Maximum number of execution history entries to keep per schedule.
    /// </summary>
    public int MaxHistoryEntries { get; set; } = 100;
    
    /// <summary>
    /// Whether to run missed schedules on startup.
    /// </summary>
    public bool RunMissedOnStartup { get; set; } = false;
}
```

## Project Structure

```
src/Microbot.Skills.Scheduling/
├── Microbot.Skills.Scheduling.csproj
├── ScheduleSkill.cs                    # Semantic Kernel plugin
├── Database/
│   ├── ScheduleDbContext.cs            # EF Core context
│   └── Entities/
│       ├── Schedule.cs
│       └── ScheduleExecution.cs
├── Models/
│   ├── ScheduleInfo.cs                 # DTO for schedule display
│   └── ExecutionInfo.cs                # DTO for execution display
├── Services/
│   ├── IScheduleService.cs             # Interface for schedule operations
│   ├── ScheduleService.cs              # CRUD operations
│   ├── ScheduleExecutorService.cs      # Background scheduler
│   └── ScheduleExpressionParser.cs     # Cron/natural language parsing
└── Extensions/
    └── ServiceCollectionExtensions.cs  # DI registration
```

## Files to Create/Modify

### New Files

| File | Description |
|------|-------------|
| `src/Microbot.Skills.Scheduling/Microbot.Skills.Scheduling.csproj` | Project file |
| `src/Microbot.Skills.Scheduling/ScheduleSkill.cs` | Semantic Kernel plugin |
| `src/Microbot.Skills.Scheduling/Database/ScheduleDbContext.cs` | EF Core context |
| `src/Microbot.Skills.Scheduling/Database/Entities/Schedule.cs` | Schedule entity |
| `src/Microbot.Skills.Scheduling/Database/Entities/ScheduleExecution.cs` | Execution entity |
| `src/Microbot.Skills.Scheduling/Models/ScheduleInfo.cs` | Schedule DTO |
| `src/Microbot.Skills.Scheduling/Models/ExecutionInfo.cs` | Execution DTO |
| `src/Microbot.Skills.Scheduling/Services/IScheduleService.cs` | Service interface |
| `src/Microbot.Skills.Scheduling/Services/ScheduleService.cs` | Service implementation |
| `src/Microbot.Skills.Scheduling/Services/ScheduleExecutorService.cs` | Background scheduler |
| `src/Microbot.Skills.Scheduling/Services/ScheduleExpressionParser.cs` | Expression parser |
| `src/Microbot.Skills/Loaders/SchedulingSkillLoader.cs` | Skill loader |

### Modified Files

| File | Changes |
|------|---------|
| `Microbot.slnx` | Add Microbot.Skills.Scheduling project |
| `src/Microbot.Core/Models/MicrobotConfig.cs` | Add SchedulingConfig |
| `src/Microbot.Skills/SkillManager.cs` | Add scheduling skill loading |
| `src/Microbot.Console/Program.cs` | Add /schedule command handlers |
| `src/Microbot.Console/Services/ConsoleUIService.cs` | Add schedule display methods |
| `src/Microbot.Console/Services/AgentService.cs` | Initialize scheduler service |
| `AGENTS.md` | Update with scheduling skill info |

## NuGet Dependencies

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="9.0.0" />
  <PackageReference Include="Cronos" Version="0.8.4" />
  <PackageReference Include="Microsoft.SemanticKernel" Version="1.70.0" />
</ItemGroup>
```

## Implementation Phases

### Phase 1: Core Infrastructure
1. Create project structure
2. Implement database entities and context
3. Implement ScheduleService for CRUD operations
4. Add SchedulingConfig to MicrobotConfig

### Phase 2: Expression Parsing
1. Implement cron expression parsing with Cronos
2. Implement natural language parsing
3. Add next occurrence calculation

### Phase 3: Console Commands
1. Add /schedule command handlers to Program.cs
2. Implement display methods in ConsoleUIService
3. Test all console commands

### Phase 4: Semantic Kernel Integration
1. Implement ScheduleSkill with KernelFunction methods
2. Create SchedulingSkillLoader
3. Register with SkillManager

### Phase 5: Background Execution
1. Implement ScheduleExecutorService
2. Integrate with AgentService
3. Handle execution results and history

### Phase 6: Testing and Documentation
1. Test all functionality
2. Update AGENTS.md
3. Update implementation-plan.md

## Usage Examples

### Via Console Commands

```
User: /schedule add "Daily Standup Prep" "every day at 8:30am" "Summarize my calendar for today and list any urgent emails"

Microbot: ✓ Schedule created with ID: 1
  Name: Daily Standup Prep
  Schedule: every day at 8:30am (30 8 * * *)
  Command: Summarize my calendar for today and list any urgent emails
  Next run: 2024-02-05 08:30:00

User: /schedule list

┌────┬────────────────────┬─────────────────────┬─────────┬─────────────────────┐
│ ID │ Name               │ Schedule            │ Status  │ Next Run            │
├────┼────────────────────┼─────────────────────┼─────────┼─────────────────────┤
│ 1  │ Daily Standup Prep │ every day at 8:30am │ Enabled │ 2024-02-05 08:30:00 │
└────┴────────────────────┴─────────────────────┴─────────┴─────────────────────┘
```

### Via Natural Conversation

```
User: Can you schedule a task to check my YouTrack issues every Monday morning at 9am?

Microbot: I'll create a recurring schedule for that.

[Calls create_recurring_schedule function]

I've created a new recurring schedule:
- **Name**: Weekly YouTrack Review
- **Type**: Recurring
- **Schedule**: Every Monday at 9:00 AM (0 9 * * 1)
- **Task**: Check YouTrack issues and summarize any updates or new assignments
- **ID**: 2

The first run will be on Monday, February 5th at 9:00 AM. You can manage this schedule using:
- `/schedule list` - View all schedules
- `/schedule disable 2` - Temporarily disable this schedule
- `/schedule remove 2` - Remove this schedule

---

User: Remind me tomorrow at 3pm to call John about the project proposal.

Microbot: I'll create a one-time reminder for that.

[Calls create_onetime_schedule function]

I've created a one-time schedule:
- **Name**: Call John
- **Type**: One-time
- **Scheduled for**: February 5, 2024 at 3:00 PM
- **Task**: Remind me to call John about the project proposal
- **ID**: 3

I'll remind you tomorrow at 3:00 PM. After it runs, the schedule will be marked as completed.

---

User: In 2 hours, remind me to check on the deployment status.

Microbot: I'll set a reminder for 2 hours from now.

[Calls create_onetime_schedule function]

I've created a one-time schedule:
- **Name**: Check Deployment
- **Type**: One-time
- **Scheduled for**: February 4, 2024 at 4:30 PM (in 2 hours)
- **Task**: Check on the deployment status
- **ID**: 4

I'll remind you at 4:30 PM today.
```

## Security Considerations

1. **Command Validation**: Validate that scheduled commands don't contain malicious content
2. **Execution Limits**: Enforce timeout limits on scheduled executions
3. **History Cleanup**: Automatically clean up old execution history
4. **Rate Limiting**: Prevent too many schedules from running simultaneously

## Future Enhancements

- [ ] Schedule dependencies (run schedule B after schedule A completes)
- [ ] Schedule groups/categories
- [ ] Email/notification on schedule completion or failure
- [ ] Schedule templates for common tasks
- [ ] Import/export schedules
- [ ] Web UI for schedule management
- [ ] Schedule analytics and reporting
