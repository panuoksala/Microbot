using Microsoft.EntityFrameworkCore;
using Microbot.Skills.Scheduling.Database.Entities;

namespace Microbot.Skills.Scheduling.Database;

/// <summary>
/// Entity Framework Core database context for schedule storage.
/// </summary>
public class ScheduleDbContext : DbContext
{
    private readonly string _databasePath;

    /// <summary>
    /// Schedules table.
    /// </summary>
    public DbSet<Schedule> Schedules { get; set; } = null!;

    /// <summary>
    /// Schedule executions table (history).
    /// </summary>
    public DbSet<ScheduleExecution> Executions { get; set; } = null!;

    /// <summary>
    /// Creates a new ScheduleDbContext with the specified database path.
    /// </summary>
    /// <param name="databasePath">Path to the SQLite database file.</param>
    public ScheduleDbContext(string databasePath)
    {
        _databasePath = databasePath;
    }

    /// <summary>
    /// Creates a new ScheduleDbContext with DbContextOptions.
    /// </summary>
    public ScheduleDbContext(DbContextOptions<ScheduleDbContext> options) : base(options)
    {
        _databasePath = string.Empty;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured && !string.IsNullOrEmpty(_databasePath))
        {
            // Ensure directory exists
            var directory = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            optionsBuilder.UseSqlite($"Data Source={_databasePath}");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Schedule entity
        modelBuilder.Entity<Schedule>(entity =>
        {
            entity.ToTable("schedules");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id")
                .ValueGeneratedOnAdd();

            entity.Property(e => e.Name)
                .HasColumnName("name")
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.Command)
                .HasColumnName("command")
                .IsRequired();

            entity.Property(e => e.Type)
                .HasColumnName("schedule_type")
                .HasConversion<string>()
                .IsRequired();

            entity.Property(e => e.CronExpression)
                .HasColumnName("cron_expression");

            entity.Property(e => e.OriginalExpression)
                .HasColumnName("original_expression");

            entity.Property(e => e.RunAt)
                .HasColumnName("run_at");

            entity.Property(e => e.Description)
                .HasColumnName("description");

            entity.Property(e => e.Enabled)
                .HasColumnName("enabled")
                .IsRequired()
                .HasDefaultValue(true);

            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired();

            entity.Property(e => e.UpdatedAt)
                .HasColumnName("updated_at")
                .IsRequired();

            entity.Property(e => e.LastRunAt)
                .HasColumnName("last_run_at");

            entity.Property(e => e.NextRunAt)
                .HasColumnName("next_run_at");

            entity.Property(e => e.RunCount)
                .HasColumnName("run_count")
                .IsRequired()
                .HasDefaultValue(0);

            // Indexes
            entity.HasIndex(e => e.Enabled)
                .HasDatabaseName("idx_schedules_enabled");

            entity.HasIndex(e => e.NextRunAt)
                .HasDatabaseName("idx_schedules_next_run");

            entity.HasIndex(e => e.Type)
                .HasDatabaseName("idx_schedules_type");
        });

        // Configure ScheduleExecution entity
        modelBuilder.Entity<ScheduleExecution>(entity =>
        {
            entity.ToTable("schedule_executions");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id")
                .ValueGeneratedOnAdd();

            entity.Property(e => e.ScheduleId)
                .HasColumnName("schedule_id")
                .IsRequired();

            entity.Property(e => e.StartedAt)
                .HasColumnName("started_at")
                .IsRequired();

            entity.Property(e => e.CompletedAt)
                .HasColumnName("completed_at");

            entity.Property(e => e.Status)
                .HasColumnName("status")
                .HasConversion<string>()
                .IsRequired();

            entity.Property(e => e.Result)
                .HasColumnName("result");

            entity.Property(e => e.ErrorMessage)
                .HasColumnName("error_message");

            // Foreign key relationship
            entity.HasOne(e => e.Schedule)
                .WithMany(s => s.Executions)
                .HasForeignKey(e => e.ScheduleId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes
            entity.HasIndex(e => e.ScheduleId)
                .HasDatabaseName("idx_executions_schedule");

            entity.HasIndex(e => e.StartedAt)
                .HasDatabaseName("idx_executions_started");
        });
    }

    /// <summary>
    /// Ensures the database is created and migrations are applied.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await Database.EnsureCreatedAsync(cancellationToken);
    }
}
