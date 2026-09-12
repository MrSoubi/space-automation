namespace SpaceAutomation.Game.Resources;

// Material taken from a mineral node and carried in a rover's cargo bay or
// waiting in an analysis facility. Like a node it references MineralData by
// id; the definition stays hidden from players until its type has been
// analyzed. The measured weight is always visible — feeling the mass is how
// an unanalyzed sample leaks density hints, which is intended.
[ValueType]
public class MineralSample
{
    [Observed("id", "Stable sample identifier.")]
    public string Id { get; set; } = "";

    [Observed("source", "Id of the mineral node this material came from.")]
    public string Source { get; set; } = "";

    [Observed("data", "The mineral data id; players see the data only after analysis.")]
    public string DataKey { get; set; } = "";

    [Observed("volume", "Material volume in litres.")]
    public float Volume { get; set; }

    public MineralData Data => MineralCatalog.Get(DataKey);

    // Kilograms, as a scale reports it: volume times density.
    public float Weight => Volume * Data.Density;

    // How this sample appears in /state. The mineral definition rides along
    // only once its type has been analyzed.
    public Dictionary<string, object?> Observe(bool identified)
    {
        var view = new Dictionary<string, object?>
        {
            ["id"] = Id,
            ["source"] = Source,
            ["volume"] = Volume,
            ["weight"] = Weight,
            ["identified"] = identified,
        };

        if (identified)
        {
            view["data"] = Data.Observe();
        }

        return view;
    }

    // The save codec runs this after restoring a sample.
    public void Validate()
    {
        Rules.Require(!string.IsNullOrWhiteSpace(Id), "Sample needs an id");
        Rules.Require(!string.IsNullOrWhiteSpace(Source), "Sample needs a source node");
        Rules.Require(!string.IsNullOrWhiteSpace(DataKey), "Sample needs mineral data");
        Rules.Require(Volume > 0, "Sample volume must be positive");
    }
}
