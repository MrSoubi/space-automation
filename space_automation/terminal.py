"""Foreground supervisor. Never evaluates player Python."""
import argparse
from collections import deque
import codeop
import os
import queue
from pathlib import Path
import secrets
import selectors
import signal
import socket
import subprocess
import sys
import tempfile
import time
from .protocol import VERSION, Client, Decoder, pack

ROOT = Path(__file__).resolve().parent.parent
STARTER = '''from expedition import station\n\ndef startup():\n    pass\n\ndef update(dt):\n    pass\n'''


def stop(process):
    if process is None:
        return
    if process.poll() is None:
        process.terminate()
        try:
            process.wait(timeout=1)
        except subprocess.TimeoutExpired:
            process.kill()
    process.wait(timeout=2)


class Terminal:
    def __init__(self, args, frontend=None):
        self.args = args
        self.frontend = frontend
        self.tick = 0
        self.selector = selectors.PollSelector()
        self.actions = deque()
        self.sources = []
        self.input_buffer = b''
        self.compiler = codeop.CommandCompiler()
        self.busy = None
        self.request_id = 0
        self.runtime = self.simulation = None
        self.channel = self.client = None
        self.paused = args.paused
        self.healthy = False
        self.exiting = False
        self.eof = False
        self.next_tick = time.monotonic() + 1
        self.tty = sys.stdin.isatty()
        self.returncode = 0

    def output(self, text):
        if self.frontend is not None:
            self.frontend.events.put(('output', text))
            return
        sys.stdout.write(text)
        sys.stdout.flush()

    def prompt(self):
        if self.frontend is not None:
            self.frontend.events.put(('prompt', bool(self.sources)))
            return
        if not self.eof:
            self.output('... ' if self.sources else '>>> ')

    def launch(self, module, *arguments, pass_fds=()):
        process = subprocess.Popen([sys.executable, '-B', '-m', module, *map(str, arguments)],
                                   cwd=ROOT, pass_fds=pass_fds, stdin=subprocess.DEVNULL,
                                   stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                   start_new_session=True)
        self.selector.register(process.stdout, selectors.EVENT_READ, ('diagnostic', process))
        return process

    def start_runtime(self):
        if self.channel is not None:
            self.selector.unregister(self.channel)
            self.channel.close()
            self.channel = None
        stop(self.runtime)
        parent, child = socket.socketpair(socket.AF_UNIX, socket.SOCK_STREAM)
        self.runtime = self.launch('space_automation.runtime', child.fileno(), self.socket_path,
                                   self.token, self.args.script, pass_fds=(child.fileno(),))
        child.close()
        self.channel = parent
        self.decoder = Decoder()
        self.selector.register(parent, selectors.EVENT_READ, ('runtime', None))
        self.healthy = False
        self.send('startup')

    def send(self, operation, sources=None, update=False):
        self.request_id += 1
        self.busy = {'id': self.request_id, 'op': operation, 'update': update,
                     'console': bool(sources)}
        self.channel.sendall(pack({**self.busy, 'sources': sources or []}))

    def control(self, line):
        if line == ':pause':
            self.paused = True
            self.output('Paused (after any active step).\n')
        elif line == ':resume':
            if self.healthy:
                self.paused = False
                self.next_tick = time.monotonic() + 1
                self.output('Running.\n')
            else:
                self.output('Runtime needs :restart before resuming.\n')
        elif line == ':step':
            if self.healthy and self.paused:
                self.send('execute', update=True)
            else:
                self.output('Step requires a paused, healthy runtime.\n')
        elif line == ':restart':
            self.paused = True
            self.actions.clear()
            self.sources.clear()
            self.start_runtime()
            self.output('Restarting player runtime; world preserved.\n')
        elif line == ':quit':
            self.exiting = True
        else:
            self.output(f'Unknown session control: {line}\n')

    def line(self, line):
        if line.startswith(':'):
            # These controls must work even while a callback is stuck.
            if self.busy and not self.actions and line in (':restart', ':quit', ':pause'):
                self.control(line)
            else:
                self.actions.append(('control', line))
            return
        self.sources.append(line)
        source = '\n'.join(self.sources)
        try:
            complete = self.compiler(source, '<console>', 'single') is not None
        except (SyntaxError, ValueError, OverflowError):
            complete = True  # The runtime reports the traceback in player context.
        if complete:
            self.actions.append(('python', source))
            self.sources.clear()
        else:
            self.prompt()

    def read_input(self):
        data = os.read(sys.stdin.fileno(), 4096)
        if not data:
            self.eof = True
            self.selector.unregister(sys.stdin)
            if self.input_buffer:
                self.line(self.input_buffer.decode('utf-8', errors='replace'))
                self.input_buffer = b''
            if self.sources:
                self.output('Incomplete input discarded at EOF.\n')
                self.sources.clear()
            self.actions.append(('control', ':quit'))
            return
        self.input_buffer += data
        while b'\n' in self.input_buffer:
            line, self.input_buffer = self.input_buffer.split(b'\n', 1)
            self.line(line.decode('utf-8', errors='replace').rstrip('\r'))

    def runtime_message(self, message):
        if message['type'] == 'hello':
            if message['version'] != VERSION:
                raise RuntimeError('Runtime protocol version mismatch')
        elif message['type'] == 'output':
            self.output(message['text'])
        elif message['type'] == 'done':
            if not self.busy or message['id'] != self.busy['id']:
                raise RuntimeError('Unexpected runtime completion')
            operation = self.busy
            self.busy = None
            if message['failed']:
                self.paused = True
                if message['lifecycle_failed']:
                    self.healthy = False
                self.output('Player execution failed; paused.\n')
            else:
                if operation['op'] == 'startup':
                    self.healthy = True
                    self.output('Player runtime ready.\n')
                if operation['update']:
                    snapshot = self.client.call('advance')
                    self.tick = snapshot['tick']
                    if self.frontend is not None:
                        self.frontend.events.put(('tick', self.tick))
                    if self.paused:
                        self.output(f"Tick {snapshot['tick']}.\n")
                self.next_tick = time.monotonic() + 1
            if operation['op'] == 'startup' or not operation['update'] or operation['console'] or self.paused or message['failed']:
                self.prompt()

    def dispatch(self):
        if self.busy or self.exiting:
            return
        if (self.actions and self.actions[0][0] == 'python'
                and not self.paused and self.healthy and time.monotonic() < self.next_tick):
            return
        if self.actions:
            kind, content = self.actions.popleft()
            if kind == 'control':
                self.control(content)
                if not self.busy and not self.exiting:
                    self.prompt()
            else:
                sources = [content]
                while self.actions and self.actions[0][0] == 'python':
                    sources.append(self.actions.popleft()[1])
                self.send('execute', sources, update=not self.paused and self.healthy)
        elif not self.paused and self.healthy and time.monotonic() >= self.next_tick:
            self.send('execute', update=True)

    def run(self):
        self.args.script = self.args.script.resolve()
        self.args.save = self.args.save.resolve()
        self.args.script.parent.mkdir(parents=True, exist_ok=True)
        if not self.args.script.exists():
            # Exclusive creation protects existing player work.
            with self.args.script.open('x') as script:
                script.write(STARTER)
        with tempfile.TemporaryDirectory(prefix='space-automation-') as directory:
            self.socket_path = Path(directory) / 'simulation.sock'
            self.token = secrets.token_hex(24)
            try:
                self.simulation = self.launch('space_automation.simulation', self.socket_path,
                                              self.args.save, self.token)
                deadline = time.monotonic() + 5
                while not self.socket_path.exists():
                    if self.simulation.poll() is not None:
                        diagnostic = self.simulation.stdout.read().decode(errors='replace')
                        raise RuntimeError(diagnostic.strip() or 'Simulation failed to start')
                    if time.monotonic() > deadline:
                        raise RuntimeError('Simulation startup timed out')
                    time.sleep(0.01)
                self.client = Client(self.socket_path, self.token, 'supervisor')
                self.tick = self.client.call('snapshot')['tick']
                if self.frontend is None:
                    self.selector.register(sys.stdin, selectors.EVENT_READ, ('stdin', None))
                else:
                    self.frontend.events.put(('tick', self.tick))
                self.output('Space Automation — ' + ('paused' if self.paused else 'running') + '\n')
                self.start_runtime()
                while not self.exiting:
                    try:
                        if self.frontend is not None:
                            for _ in range(100):
                                try:
                                    line = self.frontend.commands.get_nowait()
                                except queue.Empty:
                                    break
                                if line in (':quit', ':restart', ':pause'):
                                    self.control(line)
                                else:
                                    self.line(line)
                        for key, _ in self.selector.select(timeout=0.05):
                            kind, process = key.data
                            if kind == 'stdin':
                                self.read_input()
                            elif kind == 'diagnostic':
                                data = os.read(key.fileobj.fileno(), 4096)
                                if data:
                                    self.output(data.decode(errors='replace'))
                                else:
                                    self.selector.unregister(key.fileobj)
                                    key.fileobj.close()
                            elif kind == 'runtime':
                                if key.fileobj is not self.channel:
                                    continue
                                data = key.fileobj.recv(65536)
                                if not data:
                                    self.selector.unregister(key.fileobj)
                                    key.fileobj.close()
                                    self.channel = None
                                    self.busy = None
                                    self.healthy = False
                                    self.paused = True
                                    self.output('Player runtime disconnected; use :restart.\n')
                                else:
                                    for message in self.decoder.feed(data):
                                        self.runtime_message(message)
                        if self.simulation.poll() is not None:
                            raise RuntimeError('Simulation stopped; player execution terminated')
                        self.dispatch()
                    except KeyboardInterrupt:
                        self.paused = True
                        self.output('\nPaused. Use :restart to recover blocked player code, or :quit.\n')
                        self.prompt()
            except (OSError, RuntimeError, EOFError, ValueError) as exc:
                self.output(f'Application error: {exc}\n')
                self.returncode = 1
            finally:
                stop(self.runtime)
                if self.client is not None:
                    try:
                        self.client.call('save')
                        self.output('World saved.\n')
                    except (OSError, RuntimeError, EOFError) as exc:
                        self.output(f'Could not save world: {exc}\n')
                        self.returncode = 1
                    self.client.close()
                if self.channel is not None:
                    self.channel.close()
                stop(self.simulation)
                for key in list(self.selector.get_map().values()):
                    if key.fileobj is not sys.stdin:
                        key.fileobj.close()
                self.selector.close()
        return self.returncode


def main():
    parser = argparse.ArgumentParser(description='Space Automation — Linux programming sandbox')
    parser.add_argument('--plain', action='store_true', help='use the basic line interpreter')
    parser.add_argument('--session', default='Expedition', help='session label displayed in the status bar')
    parser.add_argument('--paused', action='store_true', help='initialize without advancing time')
    parser.add_argument('--script', type=Path, default=ROOT / 'player/main.py')
    parser.add_argument('--save', type=Path, default=ROOT / '.space-automation/world.json')
    args = parser.parse_args()
    if not args.plain and sys.stdin.isatty() and sys.stdout.isatty():
        try:
            from .tui import ConsoleApp
        except ModuleNotFoundError as exc:
            if exc.name != 'textual':
                raise
            parser.exit(1, 'Textual is missing. Install requirements.txt in .venv or use --plain.\n')
        app = ConsoleApp(args)
        signal.signal(signal.SIGTERM, lambda *_: app.bridge.commands.put(':quit'))
        return app.run() or 0
    terminal = Terminal(args)
    signal.signal(signal.SIGTERM, lambda *_: setattr(terminal, 'exiting', True))
    return terminal.run()
