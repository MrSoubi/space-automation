using System.Collections;
using SpaceAutomation.Game;
using SpaceAutomation.Persistence;
using System.Numerics;
namespace SpaceAutomation.Server;

// Immutable snapshot served by GET /state: the world as one published value.
// HTTP threads read this; only the game thread ever rebuilds it.
public sealed record ServerState(long Tick, bool Running, IReadOnlyList<IReadOnlyDictionary<string, object?>> Objects);

// Projects the world into public JSON shapes by walking the same [Observed]
// declarations the save system persists — one source of truth, no drift.
// Unlike the save codec there are no $type wrappers: vectors are {x, y},
// records are their observed fields, sequences are arrays. New equipment
// becomes visible here without any API-side edits.
public static class StateProjection
{
    public static ServerState Snapshot(World world, bool running)
    {
        var objects = new List<IReadOnlyDictionary<string, object?>>();
        var ordered = world.Objects.Values.OrderBy(obj => obj.Id, StringComparer.Ordinal);
        foreach (var obj in ordered)
        {
            if (!obj.IsPlayerVisible)
            {
                continue; // e.g. a mineral nobody has scanned yet: in the save, not in the API
            }

            objects.Add(Project(obj));
        }

        return new ServerState(world.Tick, running, objects.ToArray());
    }

    public static IReadOnlyDictionary<string, object?> Project(GameObject obj)
    {
        var result = new Dictionary<string, object?> { ["type"] = Model.ObjectKey(obj) };

        foreach (var property in Model.Observations(obj.GetType()))
        {
            result[Model.Name(property)] = Encode(property.GetValue(obj));
        }

        // Objects with secrets (minerals and their samples) adjust the fields.
        obj.AdjustObservation(result);
        return result;
    }

    // Also used for grid reports, whose values are plain data.
    public static object? Encode(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is Vector2 v)
        {
            return new Dictionary<string, object?> { ["x"] = v.X, ["y"] = v.Y };
        }

        var type = value.GetType();

        if (Model.Values.Values.Contains(type))
        {
            var fields = new Dictionary<string, object?>();

            foreach (var property in Model.Observations(type))
            {
                fields[Model.Name(property)] = Encode(property.GetValue(value));
            }

            return fields;
        }

        if (value is string text)
        {
            return text;
        }

        if (value is bool boolean)
        {
            return boolean;
        }

        if (value is double or int or long or float)
        {
            return value;
        }

        if (value is IDictionary map)
        {
            var fields = new Dictionary<string, object?>();

            foreach (DictionaryEntry entry in map)
            {
                string? key = entry.Key as string;
                if (key is null)
                {
                    throw new InvalidDataException("Observed dictionary keys must be strings");
                }

                fields[key] = Encode(entry.Value);
            }

            return fields;
        }

        if (value is IEnumerable sequence)
        {
            var items = new List<object?>();

            foreach (var item in sequence)
            {
                items.Add(Encode(item));
            }

            return items;
        }

        throw new InvalidDataException($"Unsupported observed value type: {type}");
    }
}
