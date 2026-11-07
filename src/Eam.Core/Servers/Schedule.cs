using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Eam.Core.Servers;

/// <summary>
/// When a scheduled task runs. Two forms, stored as JSON in the server database:
/// <c>{"type":"daily","times":["04:00","16:00"],"days":[1,3,5]}</c> (days: 0 = Sunday … 6 = Saturday, empty = every day)
/// or <c>{"type":"interval","minutes":90}</c>. Times are the local time of the machine running EAM.
/// </summary>
public sealed class Schedule
{
    [JsonPropertyName("type")] public string Type { get; set; } = "daily";
    [JsonPropertyName("times")] public List<string> Times { get; set; } = [];
    [JsonPropertyName("days")] public List<int> Days { get; set; } = [];
    [JsonPropertyName("minutes")] public int Minutes { get; set; }

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static Schedule Parse(string json)
    {
        var s = JsonSerializer.Deserialize<Schedule>(string.IsNullOrWhiteSpace(json) ? "{}" : json, Json) ?? new Schedule();
        s.Validate();
        return s;
    }

    public string ToJson() => JsonSerializer.Serialize(this);

    /// <summary>Throws <see cref="FormatException"/> with a short code when the schedule cannot run.</summary>
    public void Validate()
    {
        if (Type == "interval")
        {
            if (Minutes is < 5 or > 60 * 24 * 31) throw new FormatException("invalid_interval");
            return;
        }
        if (Type != "daily") throw new FormatException("invalid_schedule");
        if (Times.Count is 0 or > 48) throw new FormatException("invalid_times");
        foreach (var t in Times) _ = ParseTime(t);
        if (Days.Any(d => d is < 0 or > 6)) throw new FormatException("invalid_days");
    }

    private static TimeSpan ParseTime(string t)
    {
        if (TimeSpan.TryParseExact(t?.Trim(), [@"hh\:mm", @"h\:mm"], CultureInfo.InvariantCulture, out var ts) && ts < TimeSpan.FromDays(1)) return ts;
        throw new FormatException("invalid_times");
    }

    /// <summary>First run strictly after <paramref name="afterLocal"/> (local time).</summary>
    public DateTime NextAfter(DateTime afterLocal)
    {
        if (Type == "interval") return afterLocal.AddMinutes(Minutes);
        var times = Times.Select(ParseTime).OrderBy(x => x).ToList();
        for (var day = 0; day <= 8; day++)
        {
            var date = afterLocal.Date.AddDays(day);
            if (Days.Count > 0 && !Days.Contains((int)date.DayOfWeek)) continue;
            foreach (var t in times)
            {
                var at = date + t;
                if (at > afterLocal) return at;
            }
        }
        return DateTime.MaxValue;
    }
}
