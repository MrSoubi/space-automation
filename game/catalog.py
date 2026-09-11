"""Register object types here, once. Keys are persistent save-format identifiers."""
from game.objects.rover import Rover
from game.objects.solar_panel import SolarPanel

OBJECT_TYPES = {
    'rover': Rover,
    'solar_panel': SolarPanel,
}
