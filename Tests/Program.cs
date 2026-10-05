using Jellyfin.Plugin.SeriesActivity;

var now = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
var count = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
Check(Rules.Status(now, now.AddYears(-1), now.AddDays(-1), 90, 14) == "Active", "Recent episode keeps whole series active");
Check(Rules.Status(now, now, now.AddDays(-100), 90, 14) == "Stagnant", "Historical inactivity visible on first install");
Check(Rules.Status(now, now.AddYears(-1), now.AddDays(-90), 90, 14) == "Active", "Inclusive inactivity boundary");
Check(Rules.Status(now, now, null, 90, 14) == "New / observing", "Unknown history receives observation period");
Check(Rules.Status(now, now.AddDays(-14), null, 90, 14) == "No recorded activity", "Unknown history never called never-watched");
Check(Rules.Status(now, now, null, 90, 0) == "No recorded activity", "Zero-day observation supported");
var a = Guid.NewGuid(); var b = Guid.NewGuid(); var removed = Guid.NewGuid();
var users = new Dictionary<Guid,string> { [a] = "Alex", [b] = "Sam" };
var legacy = new[] { new Viewer(a,"Alex",now.AddDays(-200),"S01 E01","Existing Jellyfin history"),new Viewer(removed,"Removed",now,"Episode","Existing Jellyfin history") };
var recorded = new Dictionary<Guid,WatchStamp> { [b] = new(now.AddDays(-1),"S03 E07"),[a] = new(now.AddDays(-3),"S02 E02") };
var viewers = Rules.MergeViewers(legacy, recorded, users);
Check(viewers.Length == 2 && viewers[0].UserId == b, "Any user's newest episode determines series activity");
Check(viewers[1].Evidence == "Recorded playback", "Recorded playback replaces older imported history");
Check(Rules.MergeViewers(legacy, [], users).Length == 1, "Deleted users omitted from reports");
var clock = new PlaybackClock(now,false);
Check(!clock.Observe(now.AddSeconds(20),false), "Brief play excluded");
Check(!clock.Observe(now.AddSeconds(40),false), "40 seconds excluded");
Check(clock.Observe(now.AddSeconds(60),false), "60 seconds qualifies");
var paused = new PlaybackClock(now,false);
paused.Observe(now.AddSeconds(30),true); paused.Observe(now.AddSeconds(60),true); paused.Observe(now.AddSeconds(90),false);
Check(paused.Seconds == 0, "Paused intervals do not accumulate");
var gap = new PlaybackClock(now,false);
Check(!gap.Observe(now.AddMinutes(10),false) && gap.Seconds == 0,"Disconnected sessions do not accumulate wall time");
Check(!gap.Observe(now.AddMinutes(9),false),"Backward clock does not add activity");
var today = DateOnly.FromDateTime(now);
var premiere = new EpisodeAirDate(today.AddDays(-30), "S01 E01", 1, 1);
var finale = new EpisodeAirDate(today.AddDays(-1), "S01 E10", 1, 10);
var future = new EpisodeAirDate(today.AddDays(10), "S02 E01", 2, 1);
Check(Rules.LatestAiredEpisode([future, finale, premiere], now) == finale,
    "Newest aired date selected across episodes; future season excluded");
Check(Rules.LatestAiredEpisode([], now) is null, "Missing air dates remain unknown");
Check(Rules.LatestAiredEpisode([future], now) is null, "Future-only dates are not reported as aired");
var releaseToday = new EpisodeAirDate(today, "S02 E01", 2, 1);
Check(Rules.LatestAiredEpisode([premiere, releaseToday], now) == releaseToday,
    "Calendar date today is included");
var batchEarlier = finale with { Episode = "S01 E09", EpisodeNumber = 9 };
Check(Rules.LatestAiredEpisode([batchEarlier, finale], now) == finale,
    "Same-day batch release picks highest episode number");
Console.WriteLine($"{count} checks passed");
