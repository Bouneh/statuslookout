# Changelog

## 0.7.1 — 2026-10-27

- Add OVHcloud (six domains), Hetzner, Supabase, Render, Mistral AI, Groq, Hugging Face, Resend, Tailscale and Streamlabs: 1,107 catalog entries, all with bundled logos.
- Component selection for Hetzner systems (including child systems), Hugging Face resources and Streamlabs products. Streamlabs excludes unrelated Logitech incidents.
- Discover and monitor Supabase components using its complete feed rather than its truncated compatibility summary.
- Reject missing components, malformed feeds and unknown states instead of presenting them as operational.

## 0.7.0 — 2026-10-16

- Independent notification sounds for outages, degraded performance, recovery, maintenance and updates.
- Four original notification presets, the Windows default and a silent option.
- Per-event WAV import, preview buttons and volume for presets/custom sounds.
- Imported PCM WAV files are validated, limited to 10 seconds/2 MB, and copied into local app data.
- Sound choices persist locally; exports preserve presets and volume but reset local custom files to the Windows sound.
- Custom sounds silence Windows toast audio to prevent double playback; a quiet popup replaces tray balloon fallback for custom or muted alerts.

## 0.6.0 — 2026-10-13

### Added

- 1,092 built-in services with recorded coverage of all 1,048 entries in the pinned stts catalog, including AWS, Apple, Adobe, Azure and Salesforce.
- Collapsible provider families with whole-family selection, partial selection indicators and search.
- Work and Personal monitoring lists, plus custom lists with create, rename, delete and preference transfer.
- Automatic update checks after launch and every 24 hours, one notification per newer release, a download offer and an opt-out in About.
- OpenAI component selection using its complete public component feed, including Codex products omitted from the compatibility summary.
- Stream Deck plugin 0.2.0: full catalog and custom services, collapsible provider families, search and automatic monitoring of a selected service.

### Fixed

- Windows startup registration now follows the current portable executable when an existing registration points to an older path.
- Expanded catalog families scroll in pixels so every child remains reachable.
- Scoped OpenAI checks use the same full component feed as the picker and report unavailable data as Unknown.
- AWS public feed decoding and product/region scoping.

### Upgrade

- Existing service selections migrate to Work; Personal starts empty. Custom services and alert settings are preserved.
- Only the active monitoring list is monitored. Alert settings, component scopes and custom service definitions are shared.
- Update downloads are requested by the user and verified against GitHub's SHA-256 digest; replacing the portable executable remains manual.
- Some provider pages block automated requests or have retired endpoints. These services remain in the catalog and display Unavailable.
