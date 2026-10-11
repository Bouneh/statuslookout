# Contributing to StatusLookout

Thanks for helping make cloud status monitoring useful on Windows.

## Development

Use Windows and the .NET 10 SDK. Run `dotnet build StatusLookout.sln -c Release`, then `dotnet run --project tests/StatusLookout.Tests -c Release`. Use `--demo` to preview the dashboard without changing local user preferences.

## Add a service

Add a definition to `src/StatusLookout.Core/Catalog/services.json`:

```json
{
  "id": "statuspage.Example",
  "name": "Example",
  "category": "Developer services",
  "provider": "statuspage",
  "statusUrl": "https://status.example.com",
  "apiUrl": "https://status.example.com/api/v2/summary.json"
}
```

Use a unique, stable ID and public HTTPS endpoints without credentials. Follow the existing category names. Optional `components` entries contain Statuspage component IDs or stable Google product IDs.

Validate a real response, including incident states where possible. Public endpoints may return a rate-limit response or challenge page; these must become Unknown, not Operational. Do not add private endpoints or expose API keys.

## Add an adapter

Implement parsing in `StatusParser`. Keep the public snapshot model and status meanings consistent. Add tests for healthy, degraded, outage, maintenance, malformed payloads and provider schema changes.

Transport failures belong to Unknown. Never replace a last confirmed state with a network error. Test notification transitions as well as parsing.

## UI and documentation

The application has its own visual identity. Keep changes accessible and readable on dark backgrounds. Check the actual WPF layout with `--preview` and label simulated screenshots as demo data. Document supported features accurately.

## Pull requests

Explain the user-visible change, link a related issue if relevant, and include the validation performed. The CI build and offline tests must pass. Live endpoint audits are manual because external outages and rate limits must not make deterministic CI fail.

## Service logos and update controls

Bundled logos are sourced from a pinned stts commit. Refresh them with `python scripts/Sync-ServiceLogos.py --ref <full-commit-sha>`, then check attribution and render the Windows preview. Avoid runtime logo lookups.

Update downloads must remain restricted to this repository, use the matching architecture, verify SHA-256 before publishing the file and clean up partial downloads. Add behavioral tests for changes to version parsing or download handling.
