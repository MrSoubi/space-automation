#!/usr/bin/env python3
"""Space Automation player script: the fun part.

Transport lives in api.py; this file is pure behavior — survey the sector,
then drive the rover to the first mineral the scan reveals. Replace it with
whatever you are actually trying to accomplish.
"""

import math

from api import Game


def main():
    game = Game()
    print(f"connected — tick {game.state['tick']}, {len(game.state['objects'])} visible objects")

    for state in game.ticks():
        rover = game.object("rover-1")
        scanner = game.object("scanner-1")

        if rover is None or scanner is None:
            print("this script expects rover-1 and scanner-1 (start a fresh save to get them)")
            return

        # Phase 1: survey the sector once, then wait for the scan to finish.
        if not scanner.scanned:
            if scanner.ticks_remaining == 0:
                print(f"tick {state['tick']}: starting a survey scan")
                game.scan(scanner.id)

            continue

        # Phase 2: drive to the first mineral the scan revealed.
        minerals = game.objects("mineral")

        if not minerals:
            print(f"tick {state['tick']}: the scan found nothing — nothing to drive to")
            return

        target = minerals[0]

        # The remaining offset as a plain (x, y) tuple.
        offset = (target.position.x - rover.position.x, target.position.y - rover.position.y)
        distance = math.hypot(offset)

        if distance <= 0.5:
            print(f"tick {state['tick']}: arrived at {target.id} — mission complete")
            return

        direction = (offset[0] / distance, offset[1] / distance)
        speed = min(rover.speed_limit, distance)  # never overshoot the target

        result = game.move(rover.id, direction, speed)

        if result["accepted"]:
            print(f"tick {state['tick']}: heading to {target.id}, {distance:.1f} m to go")
        else:
            print(f"tick {state['tick']}: move rejected ({result.get('reason')})")


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("bye")
