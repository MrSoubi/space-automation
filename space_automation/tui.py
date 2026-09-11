"""Textual presentation for the existing three-process supervisor."""
import queue
import threading
import traceback
from rich.text import Text
from textual import events
from textual.app import App, ComposeResult
from textual.binding import Binding
from textual.widgets import Input, RichLog, Static
from .terminal import Terminal


class Bridge:
    """Thread-safe messages; UI widgets are only touched by Textual's thread."""
    def __init__(self):
        self.commands = queue.SimpleQueue()
        self.events = queue.SimpleQueue()


class CommandInput(Input):
    """Editable command line with per-session history and multiline submission."""
    def __init__(self):
        super().__init__(placeholder='Enter Python or a :session command', id='command')
        self.history = []
        self.index = 0
        self.draft = ''

    def remember(self, value):
        if value and (not self.history or self.history[-1] != value):
            self.history.append(value)
        self.index = len(self.history)
        self.draft = ''

    def on_key(self, event: events.Key):
        if event.key not in ('up', 'down') or not self.history:
            return
        event.prevent_default()
        event.stop()
        if self.index == len(self.history):
            self.draft = self.value
        delta = -1 if event.key == 'up' else 1
        self.index = max(0, min(len(self.history), self.index + delta))
        self.value = self.draft if self.index == len(self.history) else self.history[self.index]
        self.cursor_position = len(self.value)


class ConsoleApp(App[int]):
    """A scrollable transcript, a session/tick bar, and a fixed bottom input."""
    TITLE = 'Space Automation'
    ENABLE_COMMAND_PALETTE = False
    BINDINGS = [
        Binding('ctrl+c', 'pause_session', 'Pause', priority=True),
        Binding('ctrl+q', 'quit_session', 'Save and quit', priority=True),
    ]
    CSS = '''
    Screen { background: #0c111b; color: #dce6f2; }
    #output {
        height: 1fr;
        border: round #293c51;
        margin: 1 2 0 2;
        padding: 0 1;
        background: #0f1723;
        scrollbar-color: #416880;
    }
    #status {
        height: 1;
        margin: 1 2 0 2;
        padding: 0 1;
        background: #18283a;
        color: #93bccd;
    }
    #command {
        height: 3;
        margin: 0 2 1 2;
        border: tall #36576c;
        background: #101c2b;
        color: #e6f0fa;
    }
    #command:focus { border: tall #64c6d0; }
    '''

    def __init__(self, args):
        super().__init__()
        self.args = args
        self.bridge = Bridge()
        self.backend = Terminal(args, self.bridge)
        self.thread = None
        self.partial = ''
        self.continuation = False
        self.tick = 0
        self.finished = False
        self.closing = False

    def compose(self) -> ComposeResult:
        yield RichLog(id='output', min_width=1, wrap=True, highlight=False, markup=False, max_lines=10000)
        yield Static(id='status', markup=False)
        yield CommandInput()

    def on_mount(self):
        self.query_one('#output', RichLog).border_title = 'CONSOLE'
        self.refresh_status()
        self.query_one(CommandInput).focus()
        self.thread = threading.Thread(target=self.run_backend, name='simulation-supervisor')
        self.thread.start()
        self.set_interval(0.03, self.drain_events)

    def run_backend(self):
        result = 1
        try:
            result = self.backend.run()
        except BaseException:
            self.bridge.events.put(('output', traceback.format_exc()))
        finally:
            self.bridge.events.put(('finished', result))

    def refresh_status(self):
        self.query_one('#status', Static).update(Text(f' {self.args.session}  ·  Tick {self.tick:,}'))

    def write_output(self, text):
        # print() often arrives as separate text/newline messages. Preserve lines
        # and render literal output (including brackets), never Rich markup.
        self.partial += text
        log = self.query_one('#output', RichLog)
        while '\n' in self.partial:
            line, self.partial = self.partial.split('\n', 1)
            log.write(Text.from_ansi(line.rstrip('\r')))

    def flush_partial(self):
        if self.partial:
            self.query_one('#output', RichLog).write(Text.from_ansi(self.partial))
            self.partial = ''

    def drain_events(self):
        # Bound work per frame so output floods cannot starve input/recovery.
        for _ in range(500):
            try:
                kind, value = self.bridge.events.get_nowait()
            except queue.Empty:
                break
            if kind == 'output':
                self.write_output(value)
            elif kind == 'tick':
                self.tick = value
                self.refresh_status()
            elif kind == 'prompt':
                self.flush_partial()
                self.continuation = value
                self.query_one(CommandInput).placeholder = (
                    '... continuation — submit an empty line to finish' if value
                    else 'Enter Python or a :session command')
            elif kind == 'finished':
                self.finished = True
                self.flush_partial()
                if self.closing or value == 0:
                    self.exit(value)
                else:
                    self.query_one(CommandInput).placeholder = 'Session failed — Ctrl+Q to close'

    def on_input_submitted(self, event: Input.Submitted):
        if self.finished:
            return
        value = event.value
        field = self.query_one(CommandInput)
        field.remember(value)
        field.value = ''
        self.flush_partial()
        prefix = '... ' if self.continuation and not value.startswith(':') else '>>> '
        self.query_one('#output', RichLog).write(Text(prefix + value, style='#64c6d0'))
        if value == ':quit':
            self.closing = True
        self.bridge.commands.put(value)

    def action_pause_session(self):
        self.bridge.commands.put(':pause')

    def action_quit_session(self):
        self.closing = True
        if self.finished:
            self.exit(self.backend.returncode)
        else:
            self.bridge.commands.put(':quit')

    async def on_unmount(self):
        # Covers normal exit and headless test/UI exceptions without orphaning
        # simulation processes. The supervisor handles bounded child shutdown.
        if self.thread is not None and self.thread.is_alive():
            self.bridge.commands.put(':quit')
            import asyncio
            await asyncio.to_thread(self.thread.join, 12)
