using SpaceAutomation.Game.Energy;
namespace SpaceAutomation.Game.Objects;

[GameType("rover")]
public class Rover : GameObject, IEnergyNode, IMobile, IComponentHost
{
    [Observed("speed_limit", "Chassis speed limit in meters per tick.")]
    public double SpeedLimit { get; set; } = 3;

    [Observed("last_move_result")]
    public string? LastMoveResult { get; private set; }

    [Saved("_movement")]
    public Vector2? PendingMovement { get; private set; }

    // The rover is a grid node but not installable equipment: it composes an
    // energy port instead of inheriting EnergyEquipment, so it has no installed_in.
    [Observed("energy", "Port connections and installed component IDs.")]
    public EnergyPort Energy { get; set; } = new();

    public bool HasPendingMovement => PendingMovement is not null;

    // Explicit properties: these resolve real component objects in this world.
    public Battery? Battery => World.Find<Battery>(Energy.StorageId);
    public Motor? Motor => World.Find<Motor>(Energy.ConsumerId);
    public PortableSolarPanel? SolarPanel => World.Find<PortableSolarPanel>(Energy.SourceId);

    [Observed("max_speed", "Effective chassis/motor speed cap.", Persist = false)]
    public double MaxSpeed => Motor is { } motor ? Math.Min(SpeedLimit, motor.MaxSpeed) : 0;

    public CommandResult Move(Vector2 direction,
                              double speed)
    {
        if (!direction.IsFinite || direction == Vector2.Zero){
            return CommandResult.Reject("invalid_direction");
        }
            
        if (!Rules.Nonnegative(speed)){
            return CommandResult.Reject("invalid_speed");
        }

        if (PendingMovement is not null){
            return CommandResult.Reject("movement_already_requested");
        }

        if (Energy.Connections.Count > 0){
            return CommandResult.Reject("connected_to_grid");
        }

        if (Motor is not { } motor){
            return CommandResult.Reject("missing_motor");
        }

        PendingMovement = direction.Normalized() * Math.Min(speed, MaxSpeed);
        motor.Consumer.Demand = speed > 0 ? motor.EnergyPerMove : 0;

        return CommandResult.Ok();
    }

    public CommandResult ReplaceComponent(string slot,
                                          string? component_id)
    {
        if (slot is not ("battery" or "motor" or "solar_panel")){
            return CommandResult.Reject("invalid_slot");
        }

        if (PendingMovement is not null){
            return CommandResult.Reject("movement_pending");
        }

        if (Energy.Connections.Count > 0){
            return CommandResult.Reject("connected_to_grid");
        }

        string? oldId = slot switch { "battery" => Energy.StorageId, "motor" => Energy.ConsumerId, _ => Energy.SourceId };

        EnergyEquipment? replacement = null;

        if (component_id is not null)
        {
            replacement = World.Find<EnergyEquipment>(component_id);
            bool compatible = slot switch { "battery" => replacement is Battery, "motor" => replacement is Motor, _ => replacement is PortableSolarPanel };
            if (!compatible){
                return CommandResult.Reject("incompatible_component");
            }

            if (component_id == oldId){
                return CommandResult.Ok();
            }

            if (replacement!.InstalledIn is not null){
                return CommandResult.Reject("component_installed");
            }

            if (replacement.Energy.Connections.Count > 0){
                return CommandResult.Reject("component_connected");
            }

            if (replacement.Position != Position){
                return CommandResult.Reject("component_out_of_reach");
            }
        }
        if (World.Find<EnergyEquipment>(oldId) is { } old) {
            old.InstalledIn = null; old.Position = Position;
        }

        switch (slot) {
            case "battery": 
                Energy.StorageId = component_id;
                break;
            case "motor":
                Energy.ConsumerId = component_id;
                break;
            default:
                Energy.SourceId = component_id;
                break;
        }

        if (replacement is not null){
            replacement.InstalledIn = Id;
        }

        return CommandResult.Ok();
    }

    public CommandResult Connect(string other_id) => World.Energy.Connect(Id, other_id);
    public CommandResult Disconnect(string other_id) => World.Energy.Disconnect(Id, other_id);
    public Dictionary<string, object?> GridStatus() => World.Energy.GridFor(Id).ToDictionary();

    public override void PrepareTick()
    {
        if (Motor is { } motor){
            motor.Consumer.Demand = PendingMovement is { } move && move != Vector2.Zero ? motor.EnergyPerMove : 0;
        }
    }

    public override void Update()
    {
        if (PendingMovement is { } movement)
        {
            if (Motor is { } motor && motor.Consumer.Received >= motor.Consumer.Demand){
                Position += movement;
                LastMoveResult = "moved";
            }
            else {
                LastMoveResult = "not_enough_energy";
            }
            PendingMovement = null;
        }
        if (Motor is { } drive){
            drive.Consumer.Demand = 0;
        }

        foreach (var id in new[] {Energy.StorageId, Energy.SourceId, Energy.ConsumerId }){
            if (World.Find<EnergyEquipment>(id) is { } part){
                part.Position = Position;
            }
        }
            
    }
    public void ValidateComponents()
    {
        Rules.Require(Energy.StorageId is null || Battery is not null, "Incompatible rover battery");
        Rules.Require(Energy.ConsumerId is null || Motor is not null, "Incompatible rover motor");
        Rules.Require(Energy.SourceId is null || SolarPanel is not null, "Incompatible rover panel");
        Rules.Require(PendingMovement is null || Motor is not null, "Pending movement requires a motor");
    }
    
    public override void ValidateState()
    {
        base.ValidateState();
        Rules.Require(Energy is not null, "Energy port required");
        Energy!.Validate();
        Rules.Require(Rules.Nonnegative(SpeedLimit) && SpeedLimit > 0, "Invalid speed limit");
        Rules.Require(PendingMovement is null || (PendingMovement.Value.IsFinite && PendingMovement.Value.Length <= SpeedLimit * (1 + 1e-12)), "Invalid pending movement");
        Rules.Require(LastMoveResult is null or "moved" or "not_enough_energy", "Invalid movement result");
    }
}
