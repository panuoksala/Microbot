using System.Globalization;
using System.Text.RegularExpressions;
using Cronos;
using Microbot.Skills.Scheduling.Database.Entities;

namespace Microbot.Skills.Scheduling.Services;

/// <summary>
/// Parses schedule expressions (cron, natural language, and one-time).
/// </summary>
public static partial class ScheduleExpressionParser
{
    /// <summary>
    /// Result of parsing a schedule expression.
    /// </summary>
    public record ParsedSchedule(
        ScheduleType Type,
        string? CronExpression,
        DateTime? RunAt,
        string OriginalExpression);

    /// <summary>
    /// Parses a schedule expression and returns the parsed result.
    /// </summary>
    /// <param name="expression">The schedule expression to parse.</param>
    /// <param name="timeZone">The time zone for date/time calculations.</param>
    /// <returns>The parsed schedule information.</returns>
    /// <exception cref="ArgumentException">If the expression cannot be parsed.</exception>
    public static ParsedSchedule Parse(string expression, TimeZoneInfo timeZone)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            throw new ArgumentException("Schedule expression cannot be empty.", nameof(expression));
        }

        expression = expression.Trim();
        var originalExpression = expression;

        // Check for one-time schedule indicators
        if (expression.StartsWith("once ", StringComparison.OrdinalIgnoreCase))
        {
            var dateTime = ParseOneTimeExpression(expression[5..].Trim(), timeZone);
            return new ParsedSchedule(ScheduleType.Once, null, dateTime, originalExpression);
        }

        // Check for recurring schedule indicators
        if (expression.StartsWith("every ", StringComparison.OrdinalIgnoreCase))
        {
            var cron = ParseRecurringNaturalLanguage(expression);
            return new ParsedSchedule(ScheduleType.Recurring, cron, null, originalExpression);
        }

        // Try parsing as cron expression
        if (TryParseCron(expression, out _))
        {
            return new ParsedSchedule(ScheduleType.Recurring, expression, null, originalExpression);
        }

        // Try parsing as date/time (one-time)
        if (TryParseDateTime(expression, timeZone, out var dateTime2))
        {
            return new ParsedSchedule(ScheduleType.Once, null, dateTime2, originalExpression);
        }

        throw new ArgumentException($"Unable to parse schedule expression: {expression}");
    }

    /// <summary>
    /// Gets the next occurrence for a schedule.
    /// </summary>
    public static DateTime? GetNextOccurrence(Schedule schedule, DateTime from, TimeZoneInfo timeZone)
    {
        if (schedule.Type == ScheduleType.Once)
        {
            // One-time: return RunAt if not yet run, null otherwise
            return schedule.RunCount == 0 && schedule.RunAt > from ? schedule.RunAt : null;
        }

        // Recurring: use Cronos
        if (string.IsNullOrEmpty(schedule.CronExpression))
        {
            return null;
        }

        try
        {
            var cron = CronExpression.Parse(schedule.CronExpression);
            var utcFrom = TimeZoneInfo.ConvertTimeToUtc(from, timeZone);
            var nextUtc = cron.GetNextOccurrence(utcFrom, timeZone);
            return nextUtc.HasValue ? TimeZoneInfo.ConvertTimeFromUtc(nextUtc.Value, timeZone) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Validates a cron expression.
    /// </summary>
    public static bool TryParseCron(string expression, out CronExpression? cronExpression)
    {
        cronExpression = null;
        try
        {
            // Check if it looks like a cron expression (5 or 6 space-separated fields)
            var parts = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5 || parts.Length > 6)
            {
                return false;
            }

            cronExpression = CronExpression.Parse(expression);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Parses a one-time schedule expression.
    /// </summary>
    private static DateTime ParseOneTimeExpression(string expression, TimeZoneInfo timeZone)
    {
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);

        // Handle relative expressions: "in X hours/minutes/days"
        if (expression.StartsWith("in ", StringComparison.OrdinalIgnoreCase))
        {
            return ParseRelativeTime(expression[3..].Trim(), now);
        }

        // Handle "tomorrow at X"
        if (expression.StartsWith("tomorrow", StringComparison.OrdinalIgnoreCase))
        {
            return ParseTomorrowAt(expression, now, timeZone);
        }

        // Handle "on <day> at X"
        if (expression.StartsWith("on ", StringComparison.OrdinalIgnoreCase))
        {
            return ParseOnDayAt(expression[3..].Trim(), now, timeZone);
        }

        // Handle "at X" (today or tomorrow)
        if (expression.StartsWith("at ", StringComparison.OrdinalIgnoreCase))
        {
            return ParseAtTime(expression[3..].Trim(), now, timeZone);
        }

        // Try parsing as absolute date/time
        return ParseAbsoluteDateTime(expression, now, timeZone);
    }

    /// <summary>
    /// Parses relative time expressions like "2 hours", "30 minutes", "1 day".
    /// </summary>
    private static DateTime ParseRelativeTime(string expression, DateTime now)
    {
        var match = RelativeTimeRegex().Match(expression);
        if (!match.Success)
        {
            throw new ArgumentException($"Unable to parse relative time: {expression}");
        }

        var value = int.Parse(match.Groups["value"].Value);
        var unit = match.Groups["unit"].Value.ToLowerInvariant();

        return unit switch
        {
            "minute" or "minutes" or "min" or "mins" => now.AddMinutes(value),
            "hour" or "hours" or "hr" or "hrs" => now.AddHours(value),
            "day" or "days" => now.AddDays(value),
            "week" or "weeks" => now.AddDays(value * 7),
            _ => throw new ArgumentException($"Unknown time unit: {unit}")
        };
    }

    /// <summary>
    /// Parses "tomorrow at X" expressions.
    /// </summary>
    private static DateTime ParseTomorrowAt(string expression, DateTime now, TimeZoneInfo timeZone)
    {
        var tomorrow = now.Date.AddDays(1);

        // Extract time part
        var atIndex = expression.IndexOf(" at ", StringComparison.OrdinalIgnoreCase);
        if (atIndex >= 0)
        {
            var timePart = expression[(atIndex + 4)..].Trim();
            var time = ParseTimeOfDay(timePart);
            return tomorrow.Add(time);
        }

        // Default to 9am if no time specified
        return tomorrow.AddHours(9);
    }

    /// <summary>
    /// Parses "on <day> at X" expressions.
    /// </summary>
    private static DateTime ParseOnDayAt(string expression, DateTime now, TimeZoneInfo timeZone)
    {
        var atIndex = expression.IndexOf(" at ", StringComparison.OrdinalIgnoreCase);
        string dayPart;
        TimeSpan time;

        if (atIndex >= 0)
        {
            dayPart = expression[..atIndex].Trim();
            var timePart = expression[(atIndex + 4)..].Trim();
            time = ParseTimeOfDay(timePart);
        }
        else
        {
            dayPart = expression.Trim();
            time = TimeSpan.FromHours(9); // Default to 9am
        }

        var targetDate = ParseDayReference(dayPart, now);
        return targetDate.Add(time);
    }

    /// <summary>
    /// Parses "at X" expressions (today or tomorrow if time has passed).
    /// </summary>
    private static DateTime ParseAtTime(string expression, DateTime now, TimeZoneInfo timeZone)
    {
        var time = ParseTimeOfDay(expression);
        var result = now.Date.Add(time);

        // If the time has already passed today, schedule for tomorrow
        if (result <= now)
        {
            result = result.AddDays(1);
        }

        return result;
    }

    /// <summary>
    /// Parses an absolute date/time string.
    /// </summary>
    private static DateTime ParseAbsoluteDateTime(string expression, DateTime now, TimeZoneInfo timeZone)
    {
        // Try various date/time formats
        string[] formats =
        [
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd HH:mm",
            "yyyy-MM-dd",
            "MM/dd/yyyy HH:mm:ss",
            "MM/dd/yyyy HH:mm",
            "MM/dd/yyyy",
            "dd/MM/yyyy HH:mm:ss",
            "dd/MM/yyyy HH:mm",
            "dd/MM/yyyy",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mm"
        ];

        if (DateTime.TryParseExact(expression, formats, CultureInfo.InvariantCulture, 
            DateTimeStyles.None, out var result))
        {
            return result;
        }

        // Try general parsing
        if (DateTime.TryParse(expression, CultureInfo.InvariantCulture, 
            DateTimeStyles.None, out result))
        {
            return result;
        }

        throw new ArgumentException($"Unable to parse date/time: {expression}");
    }

    /// <summary>
    /// Tries to parse a string as a date/time.
    /// </summary>
    private static bool TryParseDateTime(string expression, TimeZoneInfo timeZone, out DateTime result)
    {
        result = default;
        try
        {
            var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);
            result = ParseAbsoluteDateTime(expression, now, timeZone);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Parses a time of day string like "9am", "14:30", "3:30pm".
    /// </summary>
    private static TimeSpan ParseTimeOfDay(string expression)
    {
        expression = expression.Trim().ToLowerInvariant();

        // Handle 12-hour format with am/pm
        var match = TimeOfDayRegex().Match(expression);
        if (match.Success)
        {
            var hour = int.Parse(match.Groups["hour"].Value);
            var minute = match.Groups["minute"].Success ? int.Parse(match.Groups["minute"].Value) : 0;
            var ampm = match.Groups["ampm"].Value.ToLowerInvariant();

            if (ampm == "pm" && hour != 12)
            {
                hour += 12;
            }
            else if (ampm == "am" && hour == 12)
            {
                hour = 0;
            }

            return new TimeSpan(hour, minute, 0);
        }

        // Handle 24-hour format
        var match24 = Time24HourRegex().Match(expression);
        if (match24.Success)
        {
            var hour = int.Parse(match24.Groups["hour"].Value);
            var minute = match24.Groups["minute"].Success ? int.Parse(match24.Groups["minute"].Value) : 0;
            return new TimeSpan(hour, minute, 0);
        }

        // Handle special times
        return expression switch
        {
            "noon" or "midday" => new TimeSpan(12, 0, 0),
            "midnight" => new TimeSpan(0, 0, 0),
            "morning" => new TimeSpan(9, 0, 0),
            "afternoon" => new TimeSpan(14, 0, 0),
            "evening" => new TimeSpan(18, 0, 0),
            "night" => new TimeSpan(21, 0, 0),
            _ => throw new ArgumentException($"Unable to parse time: {expression}")
        };
    }

    /// <summary>
    /// Parses a day reference like "monday", "friday", "next tuesday".
    /// </summary>
    private static DateTime ParseDayReference(string expression, DateTime now)
    {
        expression = expression.Trim().ToLowerInvariant();

        // Handle "next <day>"
        var isNext = expression.StartsWith("next ");
        if (isNext)
        {
            expression = expression[5..].Trim();
        }

        // Parse day of week
        var dayOfWeek = expression switch
        {
            "sunday" or "sun" => DayOfWeek.Sunday,
            "monday" or "mon" => DayOfWeek.Monday,
            "tuesday" or "tue" or "tues" => DayOfWeek.Tuesday,
            "wednesday" or "wed" => DayOfWeek.Wednesday,
            "thursday" or "thu" or "thur" or "thurs" => DayOfWeek.Thursday,
            "friday" or "fri" => DayOfWeek.Friday,
            "saturday" or "sat" => DayOfWeek.Saturday,
            _ => throw new ArgumentException($"Unable to parse day: {expression}")
        };

        // Calculate the next occurrence of this day
        var daysUntil = ((int)dayOfWeek - (int)now.DayOfWeek + 7) % 7;
        
        // If it's today and we want "next", add a week
        if (daysUntil == 0)
        {
            daysUntil = 7;
        }
        // If "next" was specified and it's this week, add a week
        else if (isNext && daysUntil < 7)
        {
            daysUntil += 7;
        }

        return now.Date.AddDays(daysUntil);
    }

    /// <summary>
    /// Parses recurring natural language expressions.
    /// </summary>
    private static string ParseRecurringNaturalLanguage(string expression)
    {
        expression = expression.Trim().ToLowerInvariant();

        // Remove "every " prefix
        if (expression.StartsWith("every "))
        {
            expression = expression[6..].Trim();
        }

        // Handle "X minutes/hours"
        var intervalMatch = IntervalRegex().Match(expression);
        if (intervalMatch.Success)
        {
            var value = int.Parse(intervalMatch.Groups["value"].Value);
            var unit = intervalMatch.Groups["unit"].Value;

            return unit switch
            {
                "minute" or "minutes" or "min" or "mins" => $"*/{value} * * * *",
                "hour" or "hours" or "hr" or "hrs" => $"0 */{value} * * *",
                _ => throw new ArgumentException($"Unknown interval unit: {unit}")
            };
        }

        // Handle "minute" (every minute)
        if (expression == "minute")
        {
            return "* * * * *";
        }

        // Handle "hour" (every hour)
        if (expression == "hour")
        {
            return "0 * * * *";
        }

        // Handle "day at X"
        var dayAtMatch = DayAtRegex().Match(expression);
        if (dayAtMatch.Success)
        {
            var time = ParseTimeOfDay(dayAtMatch.Groups["time"].Value);
            return $"{time.Minutes} {time.Hours} * * *";
        }

        // Handle "weekday at X"
        var weekdayAtMatch = WeekdayAtRegex().Match(expression);
        if (weekdayAtMatch.Success)
        {
            var time = ParseTimeOfDay(weekdayAtMatch.Groups["time"].Value);
            return $"{time.Minutes} {time.Hours} * * 1-5";
        }

        // Handle "weekend at X"
        var weekendAtMatch = WeekendAtRegex().Match(expression);
        if (weekendAtMatch.Success)
        {
            var time = ParseTimeOfDay(weekendAtMatch.Groups["time"].Value);
            return $"{time.Minutes} {time.Hours} * * 0,6";
        }

        // Handle "<day> at X" (e.g., "monday at 9am")
        var specificDayMatch = SpecificDayAtRegex().Match(expression);
        if (specificDayMatch.Success)
        {
            var day = specificDayMatch.Groups["day"].Value;
            var time = ParseTimeOfDay(specificDayMatch.Groups["time"].Value);
            var dayNum = GetDayNumber(day);
            return $"{time.Minutes} {time.Hours} * * {dayNum}";
        }

        // Handle "week on <day> at X"
        var weekOnDayMatch = WeekOnDayAtRegex().Match(expression);
        if (weekOnDayMatch.Success)
        {
            var day = weekOnDayMatch.Groups["day"].Value;
            var time = ParseTimeOfDay(weekOnDayMatch.Groups["time"].Value);
            var dayNum = GetDayNumber(day);
            return $"{time.Minutes} {time.Hours} * * {dayNum}";
        }

        // Handle "month on the Xth at Y"
        var monthOnDayMatch = MonthOnDayAtRegex().Match(expression);
        if (monthOnDayMatch.Success)
        {
            var dayOfMonth = int.Parse(monthOnDayMatch.Groups["day"].Value);
            var time = ParseTimeOfDay(monthOnDayMatch.Groups["time"].Value);
            return $"{time.Minutes} {time.Hours} {dayOfMonth} * *";
        }

        throw new ArgumentException($"Unable to parse recurring expression: {expression}");
    }

    /// <summary>
    /// Gets the cron day number for a day name.
    /// </summary>
    private static int GetDayNumber(string day)
    {
        return day.ToLowerInvariant() switch
        {
            "sunday" or "sun" => 0,
            "monday" or "mon" => 1,
            "tuesday" or "tue" or "tues" => 2,
            "wednesday" or "wed" => 3,
            "thursday" or "thu" or "thur" or "thurs" => 4,
            "friday" or "fri" => 5,
            "saturday" or "sat" => 6,
            _ => throw new ArgumentException($"Unknown day: {day}")
        };
    }

    // Regex patterns
    [GeneratedRegex(@"^(?<value>\d+)\s*(?<unit>minutes?|mins?|hours?|hrs?|days?|weeks?)$", RegexOptions.IgnoreCase)]
    private static partial Regex RelativeTimeRegex();

    [GeneratedRegex(@"^(?<hour>\d{1,2})(?::(?<minute>\d{2}))?\s*(?<ampm>am|pm)$", RegexOptions.IgnoreCase)]
    private static partial Regex TimeOfDayRegex();

    [GeneratedRegex(@"^(?<hour>\d{1,2}):(?<minute>\d{2})$")]
    private static partial Regex Time24HourRegex();

    [GeneratedRegex(@"^(?<value>\d+)\s*(?<unit>minutes?|mins?|hours?|hrs?)$", RegexOptions.IgnoreCase)]
    private static partial Regex IntervalRegex();

    [GeneratedRegex(@"^day\s+at\s+(?<time>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex DayAtRegex();

    [GeneratedRegex(@"^weekday\s+at\s+(?<time>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex WeekdayAtRegex();

    [GeneratedRegex(@"^weekend\s+at\s+(?<time>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex WeekendAtRegex();

    [GeneratedRegex(@"^(?<day>monday|tuesday|wednesday|thursday|friday|saturday|sunday|mon|tue|tues|wed|thu|thur|thurs|fri|sat|sun)\s+at\s+(?<time>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex SpecificDayAtRegex();

    [GeneratedRegex(@"^week\s+on\s+(?<day>monday|tuesday|wednesday|thursday|friday|saturday|sunday|mon|tue|tues|wed|thu|thur|thurs|fri|sat|sun)\s+at\s+(?<time>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex WeekOnDayAtRegex();

    [GeneratedRegex(@"^month\s+on\s+the\s+(?<day>\d{1,2})(?:st|nd|rd|th)?\s+at\s+(?<time>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex MonthOnDayAtRegex();
}
