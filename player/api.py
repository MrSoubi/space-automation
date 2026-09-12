#!/usr/bin/env python3
"""The Space Automation client library: game feel, no plumbing.

Import this from your own scripts:

    from api import Game

    game = Game()
    for state in game.ticks():
        rover = game.object("rover-1")
        game.move(rover.id, (1, 0), rover.speed_limit)

The model in one breath: the server ticks once per second on its own clock.
refresh() fetches the world snapshot; object()/objects() give you views into
that snapshot; move()/scan()/... submit commands and return the server's
answer, {"accepted": true} or {"accepted": false, "reason": "..."} — a
rejection is gameplay information, not an error. ticks() is the main loop
helper: it polls quietly and yields exactly once per new simulation tick.

The server never waits for you: if your code is slow, you miss ticks. A
missing object (None from object(), absent from objects()) usually means it
is hidden — like an undiscovered mineral.

No dependencies: plain Python standard library.
"""

import json
import os
import time
import urllib.error
import urllib.request
from collections import namedtuple

# Override with the SPACE_AUTOMATION_URL environment variable when needed,
# for example after starting the server with a different --port.
DEFAULT_URL = os.environ.get("SPACE_AUTOMATION_URL", "http://127.0.0.1:8377")

# A position or a direction: v.x and v.y. Do the arithmetic yourself on
# plain tuples — (target.position - rover.position) does not work.
Vec = namedtuple("Vec", ["x", "y"])


class GameError(RuntimeError):
    """The server could not be reached, or answered something unexpected."""


class Obj:
    """Read-only view of one object in a snapshot.

    Fields appear as attributes: rover.position (a Vec), rover.speed_limit,
    scanner.scanned, mineral.discovered. Grab objects inside the ticks loop —
    a view shows the world as of the refresh that created it. .raw is the
    plain dictionary underneath.
    """

    def __init__(self, data):
        self._data = data

    @property
    def raw(self):
        return self._data

    def __getattr__(self, name):
        try:
            value = self._data[name]
        except KeyError:
            raise AttributeError(f"{self!r} has no field {name!r}") from None

        if isinstance(value, dict) and "x" in value and "y" in value:
            return Vec(value["x"], value["y"])

        if isinstance(value, list):
            return list(value)  # a copy: the snapshot stays read-only

        return value

    def __repr__(self):
        return f"<{self._data.get('type')} {self._data.get('id')}>"


class Game:
    """One connection to a running Space Automation server."""

    def __init__(self, url=DEFAULT_URL):
        self.url = url.rstrip("/")
        self._state = None
        self.refresh()  # connect immediately: a missing server fails right here

    # ----- reads ---------------------------------------------------------------

    def refresh(self):
        """Fetch the world snapshot; object()/objects()/state reflect it."""
        self._state = self._get("/state")
        return self._state

    @property
    def state(self):
        """The latest snapshot: {"tick": ..., "running": ..., "objects": [...]}."""
        return self._state

    def object(self, id):
        """The object with this id, or None if it is missing or hidden."""
        for obj in self._state["objects"]:
            if obj.get("id") == id:
                return Obj(obj)

        return None

    def objects(self, type=None):
        """All visible objects, optionally filtered by type ("rover", ...)."""
        return [Obj(obj) for obj in self._state["objects"]
                if type is None or obj.get("type") == type]

    # ----- commands ------------------------------------------------------------
    # Every command returns the server's answer: {"accepted": true} or
    # {"accepted": false, "reason": "..."} — check result["accepted"].

    def move(self, id, direction, speed):
        """Request a move. Direction is any (x, y) pair; speed in meters per tick."""
        return self.command(f"/objects/{id}/move", {
            "direction": {"x": direction[0], "y": direction[1]},
            "speed": speed,
        })

    def scan(self, id):
        """Start a survey scan with a scanner."""
        return self.command(f"/objects/{id}/scan")

    def collect(self, id, target):
        """Take one unit of material from a collectable object."""
        return self.command(f"/objects/{id}/collect", {"target": target})

    def pause(self):
        return self.command("/session/pause")

    def resume(self):
        return self.command("/session/resume")

    def step(self):
        """Advance exactly one tick. Only meaningful while paused."""
        return self.command("/session/step")

    def save(self):
        return self.command("/session/save")

    def command(self, path, body=None):
        """Submit any POST command by path — the escape hatch for new routes."""
        return self._post(path, body or {})

    # ----- the main loop helper ---------------------------------------------------

    def ticks(self, poll_seconds=0.15):
        """Yield the state once per new simulation tick, forever.

        While the game is paused nothing is yielded: the loop simply waits.
        Missing a tick (slow code, a breakpoint) is silent — the next yield
        is just the newest tick.
        """
        last_tick = None
        while True:
            state = self.refresh()
            if state["tick"] != last_tick:
                last_tick = state["tick"]
                yield state
            else:
                time.sleep(poll_seconds)

    # ----- transport -----------------------------------------------------------

    def _get(self, path):
        try:
            with urllib.request.urlopen(self.url + path, timeout=5) as response:
                return json.load(response)
        except urllib.error.HTTPError as error:
            raise GameError(f"server error on {path}: {error.code} {error.reason}") from None
        except urllib.error.URLError as error:
            raise GameError(f"cannot reach the server at {self.url}: {error.reason}") from None

    def _post(self, path, body):
        request = urllib.request.Request(
            self.url + path,
            data=json.dumps(body).encode(),
            headers={"Content-Type": "application/json"},
            method="POST",
        )
        try:
            with urllib.request.urlopen(request, timeout=5) as response:
                return json.load(response)
        except urllib.error.HTTPError as error:
            # 404 unknown_object / 400 unsupported_object / 400 invalid_body
            # carry the standard rejection shape: return them like any answer.
            try:
                answer = json.loads(error.read())
            except ValueError:
                answer = None
            if isinstance(answer, dict) and "accepted" in answer:
                return answer
            raise GameError(f"server error on {path}: {error.code} {error.reason}") from None
        except urllib.error.URLError as error:
            raise GameError(f"cannot reach the server at {self.url}: {error.reason}") from None
