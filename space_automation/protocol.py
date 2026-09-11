"""Private, versioned, length-prefixed JSON protocol."""
import json
import socket
import struct
import threading

VERSION = 1
MAX_MESSAGE = 8 * 1024 * 1024


def pack(message):
    payload = json.dumps(message, allow_nan=False).encode('utf-8')
    if len(payload) > MAX_MESSAGE:
        raise ValueError('Protocol message too large')
    return struct.pack('!I', len(payload)) + payload


def receive(sock):
    def exact(count):
        result = bytearray()
        while len(result) < count:
            part = sock.recv(count - len(result))
            if not part:
                raise EOFError('Connection closed')
            result.extend(part)
        return result
    size = struct.unpack('!I', exact(4))[0]
    if size > MAX_MESSAGE:
        raise ValueError('Protocol message too large')
    return json.loads(exact(size))


class Decoder:
    def __init__(self):
        self.buffer = bytearray()

    def feed(self, data):
        self.buffer.extend(data)
        messages = []
        while len(self.buffer) >= 4:
            size = struct.unpack('!I', self.buffer[:4])[0]
            if size > MAX_MESSAGE:
                raise ValueError('Protocol message too large')
            if len(self.buffer) < size + 4:
                break
            messages.append(json.loads(self.buffer[4:size + 4]))
            del self.buffer[:size + 4]
        return messages


class Client:
    def __init__(self, path, token, role):
        self.socket = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        self.socket.settimeout(5)
        self.socket.connect(str(path))
        self.counter = 0
        self.lock = threading.Lock()
        self.call('hello', version=VERSION, token=token, role=role)

    def call(self, operation, **arguments):
        with self.lock:
            self.counter += 1
            self.socket.sendall(pack({'id': self.counter, 'op': operation, **arguments}))
            response = receive(self.socket)
            if response.get('id') != self.counter:
                raise RuntimeError('Mismatched protocol response')
            if 'error' in response:
                raise RuntimeError(response['error'])
            return response['result']

    def close(self):
        self.socket.close()
