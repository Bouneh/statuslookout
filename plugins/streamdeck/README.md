# StatusLookout for Stream Deck

Your monitored cloud services, directly on your Stream Deck keys.

## Requirements

- Windows 10 22H2 / Windows 11.
- Elgato Stream Deck software **7.1 or newer** (Node.js 24 is supplied by Stream Deck).
- **StatusLookout 0.5.0 or newer** running on the same Windows account.
- A Stream Deck with display keys, or Stream Deck Mobile. These actions use keys; dials, touch strips and pedals are not supported.

The first release was tested with Stream Deck software 7.4.2 and an original Stream Deck. No Marketplace account is needed to install the GitHub package.

## Install

1. Update and launch the [StatusLookout desktop app](https://github.com/Bouneh/statuslookout/releases/latest).
2. Download **StatusLookout-streamdeck-0.1.0.streamDeckPlugin** from that release.
3. Double-click the package and accept installation in Stream Deck.
4. Find **StatusLookout** in the action list, then drag an action to a free key.
5. For service actions, choose a monitored service in the key's settings.

The service list comes from your desktop watchlist, including custom services and their logos. Add services in StatusLookout before selecting them here.

## Actions

| Action | Display | Press |
| --- | --- | --- |
| Service status | Logo, service name, status colour and alert state | Open the official status page |
| Overall status | Number watched; affected `!` and unavailable `?` counts | Open StatusLookout |
| Refresh services | Refresh / checking indicator | Trigger a check of the existing watchlist |
| Snooze service alerts | Service and snooze time | Start a 30-minute / 1-hour snooze; press again to end it |

Monitoring and history continue while snoozed. Permanent muting is independent, so ending a snooze does not unmute a permanently muted service.

Green means operational, amber degraded, red outage, purple maintenance and grey unavailable or disconnected. When monitoring is paused, keys show **Paused**. Unselected or removed services cannot appear healthy. Simulated app data is labelled **DEMO**.

Key settings provide **English**, **Français** and **Follow StatusLookout**. English is the default for key text and settings; the action list follows Stream Deck's language. The plugin draws its own key labels, so Stream Deck's custom title is hidden.

## Connection and privacy

The plugin reads a shared snapshot every two seconds. Provider polling still belongs to StatusLookout, with the interval configured in the app. There are no duplicate provider checks or plugin analytics.

Communication uses a named pipe scoped to the current Windows account, with Windows `CurrentUserOnly` access checks. No web server, firewall port, pairing token or administrator permissions are required. Closing StatusLookout makes the keys show **Offline**; they reconnect automatically when it starts again.

The pipe supports only snapshot, refresh, dashboard, status-page and snooze commands. URLs come from validated, monitored services; callers cannot supply arbitrary programs or status URLs.

## Develop

Install Node.js 24 or newer, then from this folder:

```powershell
npm ci
npm run typecheck
npm test
npm run build
npm run validate
npm run link
npm run restart
npm run pack
```

`link` installs a development link to `com.bouneh.statuslookout.sdPlugin`. If the category does not appear, quit Stream Deck completely and reopen it normally. Generated plugin code goes into `bin/`; dependencies and logs are excluded from Git. Releases include a standalone package with the bundled JavaScript and dependency licenses.

The CLI `restart` command requires Stream Deck developer mode. For a normal installation, quit and reopen Stream Deck to reload changed plugin code and translations.

For a hardware check, add Overall status and Service status to free keys, verify a custom service logo, press Overview to open the app, and test Refresh and Snooze. Test snooze twice so it is ended afterwards. Automated tests cover disconnected states, missing services, languages, snoozing, SVG escaping, UTF-8 framing, protocol compatibility and bounded local commands.

The plugin uses the [official Elgato SDK](https://docs.elgato.com/streamdeck/sdk/introduction/getting-started/). Distributed under MIT; see the packaged third-party notices.
