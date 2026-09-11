#!/usr/bin/env python3
"""Space Automation reference client.

The game is an HTTP API: this file is just one way to play it, in plain
Python with no dependencies. Rewrite it in any language — the server treats
every client equally.

The canonical loop: read /state, decide, submit commands. Commands answer
immediately ({accepted: true} or {accepted: false, reason: "..."}), but their
effects happen over simulation time. The server ticks once per second and
never waits for this program: if we are slow, we simply miss ticks.
"""

import json
import time
import urllib.error
import urllib.request

SERVER = "http://127.0.0.1:8377"
POLL_SECONDS = 0.25


def get(path):
    with urllib.request.urlopen(SERVER + path, timeout=5) as response:
        return json.load(response)


def post(path, body):
    request = urllib.request.Request(
        SERVER + path,
        data=json.dumps(body).encode(),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(request, timeout=5) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        # 404 unknown object, 400 unsupported command or malformed body.
        return json.load(error)


def objects_of_type(state, type_name):
    return [obj for obj in state["objects"] if obj["type"] == type_name]


def command(path, body):
    result = post(path, body)
    if not result.get("accepted"):
        print(f"rejected {path}: {result.get('reason')}")
    return result


def main():
    print(f"connecting to {SERVER}")
    last_tick = None

    while True:
        state = get("/state")

        if state["tick"] != last_tick:
            last_tick = state["tick"]
            print(f"tick {state['tick']} — {len(state['objects'])} objects")

        # Example autopilot: drive the first rover east at half speed.
        # Replace this with whatever you are actually trying to accomplish.
        rovers = objects_of_type(state, "rover")

        if rovers:
            rover = rovers[0]
            command(
                f"/objects/{rover['id']}/move",
                {"direction": {"x": 1, "y": 0}, "speed": rover["max_speed"] / 2},
            )

        time.sleep(POLL_SECONDS)


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("bye")
