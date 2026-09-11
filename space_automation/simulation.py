"""Simulation child: serve requests while player callbacks run elsewhere."""
import argparse
import os
import socketserver
import threading
from .protocol import VERSION, pack, receive
from .world import World
from .engine.commands import CommandDispatcher


class Server(socketserver.ThreadingUnixStreamServer):
    daemon_threads = True

    def __init__(self, path, save, token):
        self.world = World.load(save)
        self.commands = CommandDispatcher(self.world)
        self.save_path = save
        self.token = token
        self.lock = threading.Lock()
        super().__init__(path, Handler)
        os.chmod(path, 0o600)


class Handler(socketserver.BaseRequestHandler):
    def handle(self):
        role = None
        while True:
            try:
                message = receive(self.request)
                identifier = message.get('id')
                operation = message.get('op')
                try:
                    with self.server.lock:
                        if role is None:
                            if (operation != 'hello' or message.get('version') != VERSION
                                    or message.get('token') != self.server.token
                                    or message.get('role') not in ('supervisor', 'runtime')):
                                raise ValueError('Invalid protocol handshake')
                            role = message['role']
                            result = {'version': VERSION}
                        elif operation == 'snapshot':
                            result = self.server.world.snapshot()
                        elif operation == 'command' and role == 'runtime':
                            result = self.server.commands.execute(**{key: message.get(key) for key in
                                ('tick', 'object_id', 'method', 'args', 'kwargs')})
                        elif operation == 'advance' and role == 'supervisor':
                            result = self.server.world.advance()
                        elif operation == 'save' and role == 'supervisor':
                            self.server.world.save(self.server.save_path)
                            result = True
                        else:
                            raise ValueError('Unknown or unauthorized protocol operation')
                    response = {'id': identifier, 'result': result}
                except Exception as exc:
                    response = {'id': identifier, 'error': str(exc)}
                self.request.sendall(pack(response))
            except (EOFError, OSError, ValueError, AttributeError):
                return


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('socket')
    parser.add_argument('save')
    parser.add_argument('token')
    args = parser.parse_args()
    with Server(args.socket, args.save, args.token) as server:
        server.serve_forever(poll_interval=0.1)


if __name__ == '__main__':
    main()
