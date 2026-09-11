"""End-to-end tests using the real Linux terminal and all three processes."""
import errno
import json
import os
from pathlib import Path
import pty
import select
import signal
import subprocess
import tempfile
import time
import unittest

ROOT = Path(__file__).resolve().parents[1]


class Session:
    def __init__(self, directory, script=None, paused=True):
        self.directory = Path(directory)
        self.script = self.directory / 'main.py'
        if script is not None:
            self.script.write_text(script)
        self.master, slave = pty.openpty()
        self.process = subprocess.Popen(
            [str(ROOT / 'space-automation'), '--plain', '--script', str(self.script),
             '--save', str(self.directory / 'world.json'), *(['--paused'] if paused else [])],
            stdin=slave, stdout=slave, stderr=slave, cwd=ROOT, start_new_session=True)
        os.close(slave)
        self.output = ''
        self.cursor = 0
        self.until('Player runtime ready.')
        self.until('>>> ')
        self.children = self.child_pids()

    def child_pids(self):
        path = Path(f'/proc/{self.process.pid}/task/{self.process.pid}/children')
        return [int(value) for value in path.read_text().split()]

    def until(self, text, timeout=6):
        deadline = time.monotonic() + timeout
        while True:
            index = self.output.find(text, self.cursor)
            if index >= 0:
                result = self.output[self.cursor:index + len(text)]
                self.cursor = index + len(text)
                return result
            if time.monotonic() > deadline:
                raise AssertionError(f'Timed out awaiting {text!r}: {self.output}')
            if select.select([self.master], [], [], 0.1)[0]:
                try:
                    data = os.read(self.master, 65536)
                except OSError as exc:
                    if exc.errno == errno.EIO:
                        raise AssertionError(f'Process exited: {self.output}') from exc
                    raise
                self.output += data.decode(errors='replace')

    def send(self, text):
        os.write(self.master, (text + '\n').encode())

    def command(self, text):
        self.send(text)
        return self.until('>>> ')

    def close(self):
        if self.process.poll() is None:
            self.send(':quit')
            self.process.wait(timeout=7)
        os.close(self.master)


AUTOMATION = '''from expedition import station, Vector2
rover = None
enabled = True
startups = 0
updates = 0
def startup():
    global rover, startups
    rover = station.get_fleet()[0]
    startups += 1
def update(dt):
    global updates
    updates += 1
    if enabled:
        rover.move(Vector2(1, 0), 3)
def display_status():
    print('POSITION', rover.position, 'UPDATES', updates)
'''


class IntegrationTests(unittest.TestCase):
    def test_live_namespace_conflicts_steps_restart_and_save(self):
        with tempfile.TemporaryDirectory() as directory:
            session = Session(directory, AUTOMATION)
            try:
                self.assertEqual(len(session.children), 2)
                self.assertIn('accepted=True', session.command('rover.move(Vector2(3, 4), 2)'))
                self.assertIn('movement_already_requested', session.command('rover.move(Vector2(1, 0), 3)'))
                self.assertIn('Tick 1.', session.command(':step'))
                self.assertIn('Vector2(x=1.2, y=1.6)', session.command('display_status()'))
                session.command('enabled = False')
                session.command(':step')
                self.assertIn('Vector2(x=1.2, y=1.6)', session.command('display_status()'))
                self.assertIn('True', session.command('rover is station.get_fleet()[0]'))
                # Custom multiline function and its live global assignment.
                session.send('def change():')
                session.until('... ')
                session.send('    global enabled; enabled = True')
                session.until('... ')
                session.send('')
                session.until('>>> ')
                session.command('change()')
                session.command(':step')
                self.assertIn('Vector2(x=4.2, y=1.6)', session.command('rover.position'))
                session.command('enabled = False')
                session.command('rover.move(Vector2(0, 1), 100)')
            finally:
                session.close()
            self.assertTrue(all(not Path(f'/proc/{pid}').exists() for pid in session.children))
            restored = Session(directory)
            try:
                self.assertIn('movement_already_requested', restored.command('rover.move(Vector2(1, 0), 1)'))
                restored.command('enabled = False')
                restored.command(':step')
                self.assertIn('Vector2(x=4.2, y=4.6)', restored.command('rover.position'))
                self.assertIn('1', restored.command('startups'))
            finally:
                restored.close()

    def test_exception_and_infinite_loop_recovery(self):
        with tempfile.TemporaryDirectory() as directory:
            session = Session(directory, AUTOMATION)
            try:
                self.assertIn('ZeroDivisionError', session.command('1 / 0'))
                session.command('enabled = False')
                session.send('while True: pass\n')
                session.until('... ')
                time.sleep(0.1)
                session.send(':restart')
                session.until('Player runtime ready.')
                session.until('>>> ')
                self.assertIn('0', session.command('station.tick'))
                session.command("exec('def update(dt):\\n    raise RuntimeError(\"broken\")')")
                self.assertIn('broken', session.command(':step'))
                self.assertIn('needs :restart', session.command(':resume'))
                session.command(':restart')
                self.assertIn('0', session.command('station.tick'))
            finally:
                session.close()

    def test_typing_does_not_stop_simulation(self):
        with tempfile.TemporaryDirectory() as directory:
            session = Session(directory, AUTOMATION, paused=False)
            try:
                os.write(session.master, b'# unfinished input')
                time.sleep(2.3)
                session.send('')
                session.until('>>> ', timeout=4)
                session.command(':pause')
                result = session.command('print("COUNT", station.tick)')
                import re
                match = re.search(r'COUNT (\d+)', result)
                self.assertIsNotNone(match, result)
                self.assertGreaterEqual(int(match[1]), 2)
            finally:
                session.close()

    def test_piped_input_and_corrupt_save(self):
        with tempfile.TemporaryDirectory() as directory:
            script = Path(directory) / 'main.py'
            save = Path(directory) / 'world.json'
            command = [str(ROOT / 'space-automation'), '--paused', '--script', str(script),
                       '--save', str(save)]
            result = subprocess.run(command, input='from expedition import Vector2\nr = station.get_fleet()[0]\nr.move(Vector2(3, 4), 2)\n:step\nr.position\n:quit\n',
                                    text=True, capture_output=True, timeout=8)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn('Vector2(x=1.2, y=1.6)', result.stdout)
            save.write_text('{broken')
            result = subprocess.run(command, input=':quit\n', text=True, capture_output=True, timeout=8)
            self.assertNotEqual(result.returncode, 0)
            self.assertEqual(save.read_text(), '{broken')

    def test_hung_update_recovery_and_quit(self):
        with tempfile.TemporaryDirectory() as directory:
            session = Session(directory, AUTOMATION)
            try:
                session.command("exec('def update(dt):\\n    while True: pass')")
                session.send(':step')
                time.sleep(0.2)
                session.send(':restart')
                session.until('Player runtime ready.')
                session.until('>>> ')
                self.assertIn('0', session.command('station.tick'))
                session.command("exec('def update(dt):\\n    while True: pass')")
                session.send(':step')
                time.sleep(0.2)
            finally:
                session.close()
            self.assertEqual(json.loads((Path(directory) / 'world.json').read_text())['tick'], 0)

    def test_second_equipment_type_works_over_same_transport(self):
        from game.objects.solar_panel import SolarPanel
        from space_automation.world import World
        with tempfile.TemporaryDirectory() as directory:
            world = World(objects=[SolarPanel(id='panel-1', energy_per_tick=2.5)])
            world.save(Path(directory) / 'world.json')
            session = Session(directory)
            try:
                session.command('from expedition import SolarPanel')
                session.command('panel = station.get_objects(SolarPanel)[0]')
                self.assertIn('accepted=True', session.command('panel.set_enabled(False)'))
                session.command(':step')
                self.assertIn('0.0', session.command('panel.energy_generated'))
                session.command('panel.set_enabled(True)')
                session.command(':step')
                self.assertIn('2.5', session.command('panel.energy_generated'))
                self.assertIn('unknown_command', session.command(
                    'panel._call("update", 1)'))
            finally:
                session.close()
