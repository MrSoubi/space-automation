"""Infrastructure shared by all game objects; gameplay belongs in game/."""
from .objects import GameObject, command, observed

__all__ = ['GameObject', 'command', 'observed']
