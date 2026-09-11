using System.Reflection;
using SpaceAutomation.Game;

namespace SpaceAutomation.Persistence;

public static class Model
{
    public static readonly Dictionary<string, Type> Objects = DiscoverGameObjectTypes();
    public static readonly Dictionary<string, Type> Values = DiscoverValueTypes();

    private static Dictionary<string, Type> DiscoverGameObjectTypes()
    {
        var objects = new Dictionary<string, Type>();
        var types = typeof(GameObject).Assembly.GetTypes();
        foreach (var type in types)
        {
            var attribute = type.GetCustomAttribute<GameTypeAttribute>();
            if (attribute is not null)
            {
                objects.Add(attribute.Name, type);
            }
        }

        return objects;
    }

    private static Dictionary<string, Type> DiscoverValueTypes()
    {
        var values = new Dictionary<string, Type>();
        var types = typeof(GameObject).Assembly.GetTypes();
        foreach (var type in types)
        {
            var attribute = type.GetCustomAttribute<SpaceAutomation.Game.ValueTypeAttribute>();
            if (attribute is not null)
            {
                values.Add(type.Name, type);
            }
        }

        return values;
    }

    public static string ObjectKey(GameObject obj)
    {
        var type = obj.GetType();
        foreach (var entry in Objects)
        {
            if (entry.Value == type)
            {
                return entry.Key;
            }
        }

        throw new InvalidOperationException($"Unregistered game object type: {type}");
    }

    public static IEnumerable<PropertyInfo> Observations(Type type)
    {
        var properties = new List<PropertyInfo>();
        foreach (var property in type.GetProperties())
        {
            if (property.GetCustomAttribute<ObservedAttribute>() is not null)
            {
                properties.Add(property);
            }
        }

        return properties;
    }

    public static IEnumerable<PropertyInfo> Saved(Type type)
    {
        var properties = new List<PropertyInfo>();
        foreach (var property in type.GetProperties())
        {
            if (property.GetCustomAttribute<SavedAttribute>() is not null)
            {
                properties.Add(property);
                continue;
            }

            var observed = property.GetCustomAttribute<ObservedAttribute>();
            if (observed is not null && observed.Persist)
            {
                properties.Add(property);
            }
        }

        return properties;
    }

    public static string Name(PropertyInfo property)
    {
        var saved = property.GetCustomAttribute<SavedAttribute>();
        if (saved is not null)
        {
            return saved.Name;
        }

        var observed = property.GetCustomAttribute<ObservedAttribute>();
        if (observed is not null)
        {
            return observed.Name;
        }

        throw new InvalidOperationException($"Property has no saved or observed name: {property.Name}");
    }
}
