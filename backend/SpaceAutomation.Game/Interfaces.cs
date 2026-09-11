using SpaceAutomation.Game;

public interface IMovable{
    CommandResult Move(Vector2 direction, float speed);
}
