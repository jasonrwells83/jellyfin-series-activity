# Series Activity — Jellyfin 12.1

A small administrator-only Jellyfin Server plugin. One row per TV series, combining playback across episodes and users. No client plugin or additional container is required on your Unraid server.

Features: library and status filters, custom inactivity cutoff (default 90 days), observation period for missing history (default 14 days), latest activity and viewers, estimated episode bytes, persistent Keep marks, CSV export, and six-hour library scans.

## Install through Jellyfin's plugin page

The provided local repository runs on your Windows PC. Keep it on the same network as Unraid until installation finishes. Node.js is required on the PC (already available on the build PC).

1. Run `Start-Series-Activity-Repository.cmd` from the extracted outputs folder if the repository is not already running. It prints a repository URL. Keep the ZIP beside the script.
2. In Jellyfin, go to **Dashboard → Plugins → Manage Repositories**, add a repository named **Series Activity**, and paste that URL.
3. Return to Plugins, choose **Available** or **All**, find **Series Activity**, and install version **1.0.0.0**.
4. Restart Jellyfin when convenient. Open **Plugins → Series Activity → Settings**.

The repository is local, not publicly hosted, and must be reachable from Unraid. It serves only the manifest and plugin ZIP. No firewall rules are changed by the script. After installation, the plugin operates without the repository server. Removing the repository entry will avoid catalog refresh errors when the PC/server is off; this does not uninstall the plugin. The plugin does not download automatic updates from any public repository.

## Manual alternative

Extract `Jellyfin.Plugin.SeriesActivity.dll` from the ZIP into a new `SeriesActivity` subfolder inside Jellyfin's existing plugins directory, then restart Jellyfin. Use the actual plugins directory in your container's persistent appdata. On the official image it is normally `/config/plugins`; LinuxServer's image normally uses `/config/data/plugins`. The host paths depend on your Unraid volume mappings. Copy the file, not the ZIP, for manual installation. No .NET SDK is needed on Unraid.

## How to read it

- **Active:** at least one current user has activity within the selected number of days.
- **Stagnant:** the latest known activity across all current users is older than the cutoff.
- **New / observing:** no dated activity, and the plugin has not yet observed the series for the selected grace period.
- **No recorded activity:** no dated activity after that observation period. This does not prove nobody has ever watched it.
- **Keep:** a retention marker. The series remains visible, but is excluded from the stagnant size total.

Expand **Viewer details** to see each user's latest activity and episode. These are account names, not necessarily individual people if accounts are shared. Stagnant shows appear immediately when older dated history is available. To view all shows without recorded activity immediately, set the observation period to zero.

## Data and limits

New activity requires at least 60 seconds accumulated between non-paused client progress reports. Seeks do not add watch time. Intervals longer than 45 seconds between reports are ignored, and server-generated progress events are ignored. A client that does not report progress often enough may be undercounted. Playback continuing during a server restart has to requalify. No tracking happens while this plugin or server is stopped.

Existing Jellyfin episode `LastPlayedDate` values older than initial plugin startup are imported and labelled **Existing Jellyfin history**. Manual watched changes and external sync may have affected those dates. This plugin cannot reconstruct a full historical session log and does not import another plugin's database. After installation, new manual watched toggles are not used as playback evidence. Only the latest date per current user per series is reported; deleted users are excluded. Re-creating an account loses its association; a library rescan that changes series IDs starts a new series record.

Episode size uses Jellyfin's known file sizes, deduplicated by exact path within the series. Sidecar files, some alternate versions, and extra video parts may not be included. Missing sizes are labelled partial. It is not an exact folder size or a promise of recoverable disk space.

Activity and Keep marks are stored in `series-activity/activity.json` under Jellyfin's **data directory**, written atomically about every five seconds and at orderly shutdown. Unreadable history is preserved rather than overwritten, and a report warning is displayed. Keep this file in ordinary Jellyfin/appdata backups. Catalog contents rebuild at startup and every six hours. The report refreshes every 30 seconds while open. Playback data stays on your server.

All report and update endpoints require Jellyfin administrator privileges. The plugin has no delete endpoint and does not modify media, watched flags, or Jellyfin's database schema.

## Build

Requires .NET SDK 10.0. Build from this source directory:

```sh
dotnet publish SeriesActivity/SeriesActivity.csproj -c Release -o publish
dotnet run --project Tests/Tests.csproj -c Release
```

The only required installed binary is `Jellyfin.Plugin.SeriesActivity.dll`. Package references are pinned to Jellyfin 12.1.0. Do not assume compatibility with other server versions.

## Verification

See `VALIDATION.md` in the deliverables for test scope and limitations. The included rule tests are dependency-free. Verification was conducted against the official Jellyfin 12.1 Docker image using synthetic media and accounts, not the user's Unraid server.

## License and references

GPL-3.0-only; see LICENSE. Uses Jellyfin's plugin APIs and configuration-page conventions.

- https://github.com/jellyfin/jellyfin/tree/v12.1
- https://github.com/jellyfin/jellyfin-plugin-template
- https://jellyfin.org/docs/general/server/plugins/
- https://raw.githubusercontent.com/linuxserver/docker-jellyfin/master/root/etc/s6-overlay/s6-rc.d/svc-jellyfin/run
