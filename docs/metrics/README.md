# Release download statistics

The README chart is generated from GitHub's public release asset counters by [Update-DownloadStats.py](../../scripts/Update-DownloadStats.py). Its raw history is kept in [downloads.json](downloads.json).

## What is counted

- Stable release `.exe` files for x64, x86 and ARM64, including both installers (`StatusLookout-Setup-win-*.exe`) and portable executables.
- The original UptimeKite executable names remain included after the project rename.
- License files, checksums, source archives, drafts and prereleases are excluded.
- Downloads are events, **not unique users or installations**. They can include automated checks.

## Monthly history

GitHub exposes a cumulative `download_count` for each release asset, not a timestamped download event log. The workflow takes a daily snapshot and adds each observed positive counter difference to that snapshot's UTC month. Downloads around month boundaries or between missed runs can consequently be assigned to the month when they are observed.

For an asset first seen in the month it was created, its existing downloads are known to belong to that same month and are included. Older unseen assets become an unallocated `before_tracking` baseline. The graph does not invent older monthly totals. Months without samples are shown as a dash, not a zero. Deleted assets retain their previously recorded monthly contributions; the **GitHub total** card reflects the currently published executable assets.

Counters use asset IDs and a high-water mark so replacing files, deleting releases or receiving temporarily lower counters does not count the same increases twice.

## Refreshing

[Download statistics](../../.github/workflows/download-stats.yml) runs daily at **00:17 UTC**, after a successful Windows release workflow, and through **Actions → Download statistics → Run workflow**. It commits the history, SVG chart and executable download badge together. No app telemetry or external analytics service is used.

GitHub can delay scheduled jobs and disable schedules after prolonged repository inactivity. If updates stop, enable and run the workflow manually. See [GitHub's schedule documentation](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#schedule).

Run locally with Python 3.9 or newer:

```powershell
python scripts/Update-DownloadStats.py
python -m unittest discover -s tests -p test_download_stats.py
```

Sources: [GitHub release asset API](https://docs.github.com/en/rest/releases/assets#get-a-release-asset), [release listing API](https://docs.github.com/en/rest/releases/releases#list-releases).
