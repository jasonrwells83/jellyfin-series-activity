using System.Text.Json;
using Jellyfin.Data.Enums;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SeriesActivity;

public sealed class ActivityService : BackgroundService
{
    private readonly ILibraryManager _library;
    private readonly IUserManager _users;
    private readonly IUserDataManager _userData;
    private readonly ISessionManager _sessions;
    private readonly ILogger<ActivityService> _log;
    private readonly string _statePath;
    private readonly object _gate = new();
    private readonly Dictionary<string, PlaybackClock> _clocks = [];
    private ActivityState _state;
    private CatalogSeries[] _catalog = [];
    private Library[] _libraries = [];
    private DateTime? _scannedUtc;
    private string? _scanError;
    private string? _storageError;
    private bool _scanning;
    private bool _refresh = true;
    private bool _dirty;
    private int _progress;

    public ActivityService(ILibraryManager library, IUserManager users, IUserDataManager userData,
        ISessionManager sessions, IApplicationPaths paths, ILogger<ActivityService> log)
    {
        _library = library; _users = users; _userData = userData; _sessions = sessions; _log = log;
        _statePath = Path.Combine(paths.DataPath, "series-activity", "activity.json");
        try
        {
            _state = File.Exists(_statePath)
                ? JsonSerializer.Deserialize<ActivityState>(File.ReadAllText(_statePath))
                  ?? throw new InvalidDataException("Empty activity state")
                : new();
            if (_state.Version != 1) throw new InvalidDataException("Unsupported state version");
        }
        catch (Exception ex)
        {
            // Never overwrite an unreadable history file with an empty one.
            _state = new();
            _storageError = "Saved history could not be loaded. It has been preserved; see the server log.";
            _log.LogError(ex, "Series Activity could not load {StatePath}", _statePath);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _sessions.PlaybackStart += OnStart;
        _sessions.PlaybackProgress += OnProgress;
        _sessions.PlaybackStopped += OnStop;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(false);
            while (!stoppingToken.IsCancellationRequested)
            {
                bool scan;
                lock (_gate)
                {
                    scan = _refresh || _scannedUtc < DateTime.UtcNow.AddHours(-6);
                    _refresh = false;
                    foreach (var key in _clocks.Where(p => p.Value.LastUtc < DateTime.UtcNow.AddHours(-2)).Select(p => p.Key).ToArray())
                        _clocks.Remove(key);
                }
                if (scan) Scan(stoppingToken);
                Save();
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            _sessions.PlaybackStart -= OnStart;
            _sessions.PlaybackProgress -= OnProgress;
            _sessions.PlaybackStopped -= OnStop;
            Save();
        }
    }

    private static string SessionKey(PlaybackProgressEventArgs e) =>
        $"{e.PlaySessionId ?? e.Session?.Id ?? e.DeviceId}:{e.Item?.Id}";
    private void OnStart(object? sender, PlaybackProgressEventArgs e)
    {
        if (e.Item is not Episode) return;
        lock (_gate) _clocks[SessionKey(e)] = new(DateTime.UtcNow, e.IsPaused);
    }
    private void OnProgress(object? sender, PlaybackProgressEventArgs e) => Observe(e, false);
    private void OnStop(object? sender, PlaybackStopEventArgs e) => Observe(e, true);
    private void Observe(PlaybackProgressEventArgs e, bool stop)
    {
        try
        {
            if (e.Item is not Episode episode || episode.SeriesId == Guid.Empty || e.IsAutomated) return;
            var now = DateTime.UtcNow;
            lock (_gate)
            {
                var key = SessionKey(e);
                if (!_clocks.TryGetValue(key, out var clock)) _clocks[key] = clock = new(now, e.IsPaused);
                if (clock.Observe(now, e.IsPaused) && !e.IsPaused)
                {
                    var series = EnsureSeries(episode.SeriesId, now);
                    foreach (var user in e.Users)
                        series.Viewers[user.Id] = new(now, EpisodeLabel(episode));
                    _dirty = true;
                }
                if (stop) _clocks.Remove(key);
            }
        }
        catch (Exception ex) { _log.LogError(ex, "Series Activity playback update failed"); }
    }

    private static string EpisodeLabel(Episode e) => $"S{e.ParentIndexNumber:00} E{e.IndexNumber:00} · {e.Name}";
    private SeriesState EnsureSeries(Guid id, DateTime now)
    {
        if (!_state.Series.TryGetValue(id, out var entry))
        {
            _state.Series[id] = entry = new() { FirstSeenUtc = now };
            _dirty = true;
        }
        return entry;
    }

    public void RequestRefresh() { lock (_gate) { if (!_scanning) _refresh = true; } }
    public bool SetKeep(Guid id, bool keep)
    {
        lock (_gate)
        {
            if (!_state.Series.TryGetValue(id, out var entry)) return false;
            entry.Keep = keep; _dirty = true;
        }
        Save();
        return true;
    }

    private void Scan(CancellationToken token)
    {
        lock (_gate) { _scanning = true; _progress = 0; _scanError = null; }
        try
        {
            var users = _users.GetUsers().ToArray();
            var result = new List<CatalogSeries>();
            var libs = new List<Library>();
            var now = DateTime.UtcNow;
            foreach (var folder in _library.GetVirtualFolders())
            {
                token.ThrowIfCancellationRequested();
                if (!Guid.TryParse(folder.ItemId, out var libraryId)) continue;
                var shows = _library.GetItemList(new InternalItemsQuery
                {
                    ParentId = libraryId, Recursive = true,
                    IncludeItemTypes = [BaseItemKind.Series], IsVirtualItem = false,
                    GroupByPresentationUniqueKey = false
                }).OfType<Series>().ToArray();
                if (shows.Length == 0 && folder.CollectionType != CollectionTypeOptions.tvshows) continue;
                libs.Add(new(libraryId, folder.Name));
                foreach (var show in shows)
                {
                    token.ThrowIfCancellationRequested();
                    DateTime trackingStart;
                    lock (_gate) { EnsureSeries(show.Id, now); trackingStart = _state.TrackingStartedUtc; }
                    var episodes = _library.GetItemList(new InternalItemsQuery
                    {
                        ParentId = show.Id, Recursive = true,
                        IncludeItemTypes = [BaseItemKind.Episode], IsVirtualItem = false,
                        GroupByPresentationUniqueKey = false
                    }).OfType<Episode>().ToArray();
                    Dictionary<Guid, Viewer> history;
                    lock (_gate) history = new(_state.Series[show.Id].ExistingHistory);
                    foreach (var episode in episodes)
                    {
                        token.ThrowIfCancellationRequested();
                        foreach (var user in users)
                        {
                            var data = _userData.GetUserData(user, episode);
                            // Only bootstrap from dates before installation. After that, actual playback
                            // events must meet the threshold; manual watched toggles cannot reset activity.
                            if (data?.LastPlayedDate is not DateTime last || last > trackingStart) continue;
                            var utc = DateTime.SpecifyKind(last, DateTimeKind.Utc);
                            if (!history.TryGetValue(user.Id, out var existing) || utc > existing.LastActivityUtc)
                                history[user.Id] = new(user.Id, user.Username, utc, EpisodeLabel(episode), "Existing Jellyfin history");
                        }
                    }
                    var uniqueFiles = episodes.GroupBy(e => string.IsNullOrWhiteSpace(e.Path) ? e.Id.ToString() : e.Path, StringComparer.Ordinal).Select(g => g.First()).ToArray();
                    lock (_gate) { _state.Series[show.Id].ExistingHistory = history; _dirty = true; }
                    result.Add(new(show.Id, libraryId, folder.Name, show.Name, show.Path ?? "",
                        episodes.Length, uniqueFiles.Sum(e => Math.Max(0, e.Size ?? 0)),
                        uniqueFiles.Count(e => !e.Size.HasValue), history));
                    lock (_gate) _progress++;
                }
            }
            lock (_gate)
            {
                _catalog = result.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToArray();
                _libraries = libs.OrderBy(l => l.Name).ToArray();
                _scannedUtc = DateTime.UtcNow;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _log.LogError(ex, "Series Activity library scan failed");
            lock (_gate) _scanError = "Library scan failed. Previous results are retained; check the Jellyfin server log and try Refresh.";
        }
        finally { lock (_gate) _scanning = false; }
    }

    public Report GetReport(int inactiveDays, int graceDays)
    {
        var users = _users.GetUsers().ToDictionary(u => u.Id, u => u.Username);
        var now = DateTime.UtcNow;
        lock (_gate)
        {
            var rows = _catalog.Select(s =>
            {
                var state = _state.Series[s.Id];
                var viewers = Rules.MergeViewers(s.History.Values, state.Viewers, users);
                DateTime? last = viewers.Length > 0 ? viewers[0].LastActivityUtc : null;
                return new SeriesRow(s.Id, s.LibraryId, s.Library, s.Name, s.Path, s.Episodes,
                    s.KnownBytes, s.UnknownSizes, state.FirstSeenUtc, state.Keep, last,
                    last.HasValue ? Math.Max(0, (int)(now - last.Value).TotalDays) : null,
                    Rules.Status(now, state.FirstSeenUtc, last, inactiveDays, graceDays), viewers);
            }).ToArray();
            return new(_state.TrackingStartedUtc, _scannedUtc, _scanning || _refresh,
                _scanError, _storageError, _progress, _libraries, rows);
        }
    }

    private void Save()
    {
        lock (_gate)
        {
            if (!_dirty || _storageError?.StartsWith("Saved history") == true) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
                var temp = _statePath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(_state));
                File.Move(temp, _statePath, true);
                _dirty = false; _storageError = null;
            }
            catch (Exception ex)
            {
                _storageError = "Activity cannot currently be saved. Check write permissions and free space in the Jellyfin data folder.";
                _log.LogError(ex, "Series Activity history write failed");
            }
        }
    }
}
