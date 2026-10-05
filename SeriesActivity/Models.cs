namespace Jellyfin.Plugin.SeriesActivity;

public sealed class ActivityState
{
    public int Version { get; set; } = 1;
    public DateTime TrackingStartedUtc { get; set; } = DateTime.UtcNow;
    public Dictionary<Guid, SeriesState> Series { get; set; } = [];
}

public sealed class SeriesState
{
    public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;
    public bool Keep { get; set; }
    public Dictionary<Guid, WatchStamp> Viewers { get; set; } = [];
    public Dictionary<Guid, Viewer> ExistingHistory { get; set; } = [];
}

public sealed record WatchStamp(DateTime LastWatchedUtc, string Episode);
public sealed record Viewer(Guid UserId, string Name, DateTime LastActivityUtc, string Episode, string Evidence);
public sealed record Library(Guid Id, string Name);
public sealed record EpisodeAirDate(DateOnly Date, string Episode, int? SeasonNumber, int? EpisodeNumber);
public sealed record CatalogSeries(Guid Id, Guid LibraryId, string Library, string Name, string Path,
    int Episodes, long KnownBytes, int UnknownSizes, Dictionary<Guid, Viewer> History,
    EpisodeAirDate? LatestAiredEpisode, int MissingAirDates);
public sealed record SeriesRow(Guid Id, Guid LibraryId, string Library, string Name, string Path,
    int Episodes, long KnownBytes, int UnknownSizes, DateTime FirstSeenUtc, bool Keep,
    DateTime? LastActivityUtc, int? DaysInactive, string Status, Viewer[] Viewers,
    EpisodeAirDate? LatestAiredEpisode, int MissingAirDates);
public sealed record Report(DateTime TrackingStartedUtc, DateTime? ScannedUtc, bool Scanning,
    string? Error, string? StorageError, int Progress, Library[] Libraries, SeriesRow[] Series);

public static class Rules
{
    public static EpisodeAirDate? LatestAiredEpisode(IEnumerable<EpisodeAirDate> episodes, DateTime nowUtc) =>
        episodes.Where(e => e.Date <= DateOnly.FromDateTime(nowUtc))
            .OrderByDescending(e => e.Date)
            .ThenByDescending(e => e.SeasonNumber)
            .ThenByDescending(e => e.EpisodeNumber)
            .ThenBy(e => e.Episode, StringComparer.Ordinal)
            .FirstOrDefault();

    public static string Status(DateTime now, DateTime firstSeen, DateTime? lastActivity,
        int inactiveDays, int graceDays)
    {
        if (lastActivity >= now.AddDays(-inactiveDays)) return "Active";
        if (lastActivity.HasValue) return "Stagnant";
        if (firstSeen > now.AddDays(-graceDays)) return "New / observing";
        return "No recorded activity";
    }

    public static Viewer[] MergeViewers(IEnumerable<Viewer> legacy,
        Dictionary<Guid, WatchStamp> recorded, IReadOnlyDictionary<Guid, string> users)
    {
        var result = legacy.Where(v => users.ContainsKey(v.UserId))
            .Select(v => v with { Name = users[v.UserId] }).ToDictionary(v => v.UserId);
        foreach (var (id, stamp) in recorded)
        {
            if (!users.TryGetValue(id, out var name)) continue;
            if (!result.TryGetValue(id, out var old) || stamp.LastWatchedUtc >= old.LastActivityUtc)
                result[id] = new(id, name, stamp.LastWatchedUtc, stamp.Episode, "Recorded playback");
        }
        return result.Values.OrderByDescending(v => v.LastActivityUtc).ToArray();
    }
}

// Uses elapsed time between non-paused client reports, not media position (which can jump on seek).
public sealed class PlaybackClock
{
    public DateTime LastUtc { get; private set; }
    public bool WasPaused { get; private set; }
    public double Seconds { get; private set; }
    public PlaybackClock(DateTime now, bool paused) { LastUtc = now; WasPaused = paused; }
    public bool Observe(DateTime now, bool paused)
    {
        var elapsed = (now - LastUtc).TotalSeconds;
        if (!WasPaused && !paused && elapsed > 0 && elapsed <= 45) Seconds += elapsed;
        LastUtc = now;
        WasPaused = paused;
        return Seconds >= 60;
    }
}
