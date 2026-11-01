import json
from pathlib import Path
import unittest
from patch_mapctl import render, PRELUDE, PATCHED_WORLD

HERE = Path(__file__).resolve().parent
SOURCE = HERE.parents[2] / 'cushion-wars-a/deploy/server/mapctl'

class DeploymentTest(unittest.TestCase):
    def test_known_launcher_gets_only_one_optional_map_plugin_argument(self):
        text = render(SOURCE.read_text(encoding='utf-8'))
        self.assertEqual(text.count(PRELUDE), 1)
        self.assertEqual(text.count(PATCHED_WORLD), 1)
        self.assertIn('plugin_args=(--add-plugin-dir "$MAPS/$id/plugins")', text)
        self.assertIn('-Dcw.map="$id"', text)

    def test_reapplying_recorded_delta_is_idempotent(self):
        text = render(SOURCE.read_text(encoding='utf-8'))
        self.assertEqual(render(text), text)

    def test_unknown_launcher_is_never_overwritten(self):
        with self.assertRaises(ValueError):
            render(SOURCE.read_text(encoding='utf-8') + '# external change\n')

    def test_menu_keeps_existing_maps_and_adds_cross_era(self):
        menu = json.loads((HERE / 'menu/data/cw_menu/dialog/menu.json').read_text(encoding='utf-8'))
        commands = {action['action']['command'] for action in menu['actions']}
        self.assertEqual(commands, {'/maps goto cushion-wars', '/maps goto minigames', '/maps goto cross-era', '/maps list'})
        tag = json.loads((HERE / 'menu/data/minecraft/tags/dialog/pause_screen_additions.json').read_text())
        self.assertFalse(tag['replace'])
        self.assertEqual(tag['values'], ['cw_menu:menu'])

if __name__ == '__main__':
    unittest.main()
