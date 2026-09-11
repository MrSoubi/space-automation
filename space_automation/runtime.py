"""Execute all player Python serially inside the main module's live namespace."""
import argparse
import importlib.util
import io
import socket
import sys
import threading
import traceback
from pathlib import Path
from expedition import station
from .protocol import VERSION, Client, pack, receive


class Output(io.TextIOBase):
    def __init__(self, emit):
        self.emit = emit

    def write(self, text):
        if text:
            self.emit({'type': 'output', 'text': str(text)})
        return len(text)

    def flush(self):
        pass

    @property
    def encoding(self):
        return 'utf-8'


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('fd', type=int)
    parser.add_argument('socket')
    parser.add_argument('token')
    parser.add_argument('script')
    args = parser.parse_args()
    channel = socket.socket(fileno=args.fd)
    lock = threading.Lock()

    def emit(message):
        with lock:
            channel.sendall(pack(message))

    sys.stdout = sys.stderr = Output(emit)
    client = Client(args.socket, args.token, 'runtime')
    station._transport = client.call
    script = Path(args.script).resolve()
    sys.path.insert(0, str(script.parent))
    spec = importlib.util.spec_from_file_location('main', script)
    module = importlib.util.module_from_spec(spec)
    sys.modules['main'] = module
    # Disable bytecode writes so immediate edits cannot hit timestamp-based caches.
    sys.dont_write_bytecode = True
    emit({'type': 'hello', 'version': VERSION})
    try:
        while True:
            request = receive(channel)
            failed = False
            lifecycle = False
            try:
                station._refresh(client.call('snapshot'))
                if request['op'] == 'startup':
                    lifecycle = True
                    # Compile source directly: always read the latest main.py.
                    exec(compile(script.read_text(), str(script), 'exec'), module.__dict__)
                    for name in ('startup', 'update'):
                        if not callable(module.__dict__.get(name)):
                            raise TypeError(f'main.py must define callable {name}()')
                    module.startup()
                else:
                    for source in request['sources']:
                        exec(compile(source, '<console>', 'single'), module.__dict__, module.__dict__)
                    if request['update']:
                        lifecycle = True
                        module.update(1.0)
            except BaseException:
                failed = True
                traceback.print_exc()
            emit({'type': 'done', 'id': request['id'], 'failed': failed,
                  'lifecycle_failed': failed and lifecycle})
    except (EOFError, BrokenPipeError, ConnectionResetError):
        pass
    finally:
        client.close()
        channel.close()


if __name__ == '__main__':
    main()
