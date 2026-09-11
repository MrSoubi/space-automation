import argparse
import asyncio
from pathlib import Path
import tempfile
import unittest
from textual.widgets import RichLog, Static
from space_automation.tui import ConsoleApp, CommandInput


class TextualTests(unittest.IsolatedAsyncioTestCase):
    async def wait_for(self, condition, pilot):
        for _ in range(100):
            if condition():
                return
            await pilot.pause(0.05)
        self.fail('UI/backend condition timed out')

    async def test_console_status_history_multiline_and_recovery(self):
        with tempfile.TemporaryDirectory() as directory:
            args = argparse.Namespace(paused=True, session='Survey Alpha',
                                      script=Path(directory)/'main.py', save=Path(directory)/'world.json')
            app = ConsoleApp(args)
            async with app.run_test(size=(100, 30)) as pilot:
                await self.wait_for(lambda: app.backend.healthy, pilot)
                field = app.query_one(CommandInput)
                log = app.query_one(RichLog)

                async def submit(text):
                    field.value = text
                    await pilot.press('enter')
                    await pilot.pause(0.15)

                await submit('from expedition import Vector2')
                await submit('rover = station.get_fleet()[0]')
                await submit('rover.move(Vector2(3, 4), 2)')
                await submit(':step')
                await self.wait_for(lambda: app.tick == 1, pilot)
                self.assertIn('Survey Alpha', str(app.query_one(Static).render()))
                self.assertIn('Tick 1', str(app.query_one(Static).render()))
                await submit('rover.position')
                self.assertTrue(any('Vector2(x=1.2, y=1.6)' in line.text for line in log.lines))
                await pilot.press('up')
                self.assertEqual(field.value, 'rover.position')
                field.value = ''
                await submit('def report():')
                self.assertTrue(app.continuation)
                await submit('    print("[literal] result")')
                await submit('')
                await submit('report()')
                self.assertTrue(any(line.text == '[literal] result' for line in log.lines))
                self.assertLess(log.region.bottom, field.region.bottom)
                self.assertLessEqual(field.region.bottom, 30)
                # Background progress never touches an unfinished input draft.
                await submit(':resume')
                field.value = 'unfinished command'
                await self.wait_for(lambda: app.tick >= 2, pilot)
                self.assertEqual(field.value, 'unfinished command')
                await submit(':pause')
                # A blocked interpreter does not block the Textual event loop.
                await submit('while True: pass')
                await submit('')
                old_pid = app.backend.runtime.pid
                await submit(':restart')
                await self.wait_for(lambda: app.backend.healthy and app.backend.runtime.pid != old_pid, pilot)
                await pilot.resize_terminal(50, 15)
                self.assertLessEqual(field.region.bottom, 15)
                app.save_screenshot('/tmp/space-automation-tui.svg')
                await submit(':quit')
            self.assertIsNotNone(app.backend.simulation.returncode)
            self.assertIsNotNone(app.backend.runtime.returncode)
            self.assertTrue(args.save.exists())
