"""Counter history tests: avoid inventing monthly history or counting assets twice."""
import importlib.util
from pathlib import Path
from datetime import datetime, timezone
import unittest
import xml.etree.ElementTree as ET

spec = importlib.util.spec_from_file_location('download_stats', Path(__file__).resolve().parents[1] / 'scripts/Update-DownloadStats.py')
stats = importlib.util.module_from_spec(spec); spec.loader.exec_module(stats)
NOW = datetime(2026, 10, 11, 12, tzinfo=timezone.utc)
REPO = 'Bouneh/statuslookout'

def release(count=3, identifier=1, created='2026-10-10T00:00:00Z', name='StatusLookout-win-x64.exe'):
    return {'tag_name': 'v0.3.0', 'draft': False, 'prerelease': False,
            'assets': [{'id': identifier, 'name': name, 'download_count': count, 'created_at': created}]}

class DownloadStatisticsTests(unittest.TestCase):
    def test_new_asset_current_month_has_known_monthly_downloads(self):
        state = stats.observe(None, [release()], NOW, REPO)
        self.assertEqual(state['months']['2026-10']['x64'], 3)
        self.assertEqual(state['before_tracking'], 0)

    def test_older_assets_are_a_baseline_not_fabricated_history(self):
        state = stats.observe(None, [release(created='2026-09-01T00:00:00Z')], NOW, REPO)
        self.assertEqual(state['months']['2026-10']['x64'], 0)
        self.assertEqual(state['before_tracking'], 3)
        self.assertNotIn('2026-09', state['months'])

    def test_month_boundary_records_new_deltas_not_lifetime_counts(self):
        first = stats.observe(None, [release()], NOW, REPO)
        later = stats.observe(first, [release(8)], datetime(2026, 11, 1, 0, 17, tzinfo=timezone.utc), REPO)
        self.assertEqual(later['months']['2026-10']['x64'], 3)
        self.assertEqual(later['months']['2026-11']['x64'], 5)
        self.assertEqual(first['assets']['1']['count'], 3)  # Caller data remains intact.

    def test_duplicate_pages_and_document_downloads_are_excluded(self):
        rows = [release(), release(), release(100, 2, name='LICENSE'), release(100, 3, name='SHA256SUMS-win-x64.txt')]
        state = stats.observe(None, rows, NOW, REPO)
        self.assertEqual(state['github_total'], 3)
        self.assertEqual(state['stable_release_count'], 1)

    def test_counter_regression_does_not_double_count_later(self):
        first = stats.observe(None, [release(8)], NOW, REPO)
        regressed = stats.observe(first, [release(6)], NOW, REPO)
        restored = stats.observe(regressed, [release(8)], NOW, REPO)
        self.assertEqual(restored['months']['2026-10']['x64'], 8)

    def test_deleted_assets_preserve_already_observed_history(self):
        first = stats.observe(None, [release()], NOW, REPO)
        later = stats.observe(first, [], NOW, REPO)
        self.assertEqual(later['github_total'], 0)
        self.assertEqual(later['months']['2026-10']['x64'], 3)

    def test_legacy_executables_count_but_prereleases_do_not(self):
        legacy = release(2, name='UptimeKite-win-arm64.exe')
        preview = release(99, 2); preview['prerelease'] = True
        state = stats.observe(None, [legacy, preview], NOW, REPO)
        self.assertEqual(state['months']['2026-10']['arm64'], 2)
        self.assertEqual(state['github_total'], 2)

    def test_missing_month_has_no_observations_and_svg_is_valid(self):
        state = stats.observe(None, [release()], NOW, REPO)
        state = stats.observe(state, [release(5)], datetime(2026, 12, 1, tzinfo=timezone.utc), REPO)
        chart = stats.render_chart(state)
        self.assertIn('No observations recorded for 2026-11', chart)
        ET.fromstring(chart); ET.fromstring(stats.render_badge(10000))

    def test_history_cannot_be_reused_for_another_repository(self):
        state = stats.observe(None, [release()], NOW, REPO)
        with self.assertRaises(ValueError): stats.observe(state, [release()], NOW, 'someone/else')

if __name__ == '__main__': unittest.main()
