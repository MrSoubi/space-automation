using System.Reflection;
using SpaceAutomation.Game;
namespace SpaceAutomation.Persistence;

public static class Model
{
    public static readonly Dictionary<string, Type> Objects = typeof(GameObject).Assembly.GetTypes()
        .Where(t => t.GetCustomAttribute<GameTypeAttribute>() is not null)
        .ToDictionary(t => t.GetCustomAttribute<GameTypeAttribute>()!.Name);
    public static readonly Dictionary<string, Type> Values = typeof(GameObject).Assembly.GetTypes()
        .Where(t => t.GetCustomAttribute<SpaceAutomation.Game.ValueTypeAttribute>() is not null).ToDictionary(t => t.Name);
    public static string ObjectKey(GameObject obj) => Objects.First(x => x.Value == obj.GetType()).Key;
    public static IEnumerable<PropertyInfo> Observations(Type type) => type.GetProperties().Where(p => p.GetCustomAttribute<ObservedAttribute>() is not null);
    public static IEnumerable<PropertyInfo> Saved(Type type) => type.GetProperties().Where(p =>
        p.GetCustomAttribute<SavedAttribute>() is not null || p.GetCustomAttribute<ObservedAttribute>() is { Persist: true });
    public static string Name(PropertyInfo property) => property.GetCustomAttribute<SavedAttribute>()?.Name ?? property.GetCustomAttribute<ObservedAttribute>()!.Name;
}
