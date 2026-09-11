using System.Collections;
using System.Reflection;
using System.Text.Json.Nodes;
using SpaceAutomation.Game;
namespace SpaceAutomation.Persistence;

public static class ValueCodec
{
    public static JsonNode? Encode(object? value)
    {
        if (value is null) return null;
        if (value is Vector2 v) return new JsonObject { ["$type"] = "Vector2", ["x"] = v.X, ["y"] = v.Y };
        if (value is CommandResult r) return new JsonObject { ["$type"] = "CommandResult", ["accepted"] = r.Accepted, ["reason"] = r.Reason };
        if (value is double number && !double.IsFinite(number))
            return new JsonObject { ["$type"] = "Float", ["value"] = double.IsNaN(number) ? "nan" : number > 0 ? "inf" : "-inf" };
        var type = value.GetType();
        if (Model.Values.Values.Contains(type))
        {
            var fields = new JsonObject();
            foreach (var property in Model.Observations(type)) fields[Model.Name(property)] = Encode(property.GetValue(value));
            return new JsonObject { ["$type"] = type.Name, ["fields"] = fields };
        }
        if (value is string text) return JsonValue.Create(text);
        if (value is bool boolean) return JsonValue.Create(boolean);
        if (value is double d) return JsonValue.Create(d);
        if (value is int i) return JsonValue.Create(i);
        if (value is long l) return JsonValue.Create(l);
        if (value is IDictionary dictionary)
        {
            var result = new JsonObject();
            foreach (DictionaryEntry pair in dictionary) result.Add((string)pair.Key, Encode(pair.Value));
            return result;
        }
        if (value is IEnumerable sequence)
        {
            var array = new JsonArray();
            foreach (var item in sequence) array.Add(Encode(item));
            return array;
        }
        throw new InvalidDataException($"Unsupported value type: {type}");
    }
    public static object? Decode(JsonNode? node, Type type, bool nullable = false)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (node is null)
        {
            if (nullable || underlying is not null) return null;
            throw new InvalidDataException("Unexpected null");
        }
        type = underlying ?? type;
        if (type == typeof(string)) return node.GetValue<string>();
        if (type == typeof(bool)) return node.GetValue<bool>();
        if (type == typeof(double))
        {
            if (node is JsonObject special && special["$type"]?.GetValue<string>() == "Float")
                return special["value"]?.GetValue<string>() switch { "nan" => double.NaN, "inf" => double.PositiveInfinity, "-inf" => double.NegativeInfinity, _ => throw new InvalidDataException("Invalid float tag") };
            // JSON numbers can be backed by int, long or double depending on where
            // they came from; accept any so 1 decodes like 1.0 for float parameters.
            if (node is JsonValue number && number.TryGetValue<double>(out var d)) return d;
            if (node is JsonValue longNumber && longNumber.TryGetValue<long>(out var l)) return l;
            if (node is JsonValue intNumber && intNumber.TryGetValue<int>(out var i)) return i;
            return node.GetValue<double>();
        }
        if (type == typeof(long)) return node.GetValue<long>();
        if (type == typeof(int)) return node.GetValue<int>();
        if (type == typeof(Vector2))
        {
            if (node is not JsonObject vector || vector.Count != 3 || vector["$type"]?.GetValue<string>() != "Vector2") throw new InvalidDataException("Invalid Vector2");
            var v = new Vector2(vector["x"]!.GetValue<double>(), vector["y"]!.GetValue<double>());
            if (!v.IsFinite) throw new InvalidDataException("Nonfinite vector");
            return v;
        }
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            if (node is not JsonArray array) throw new InvalidDataException("Expected array");
            var list = (IList)Activator.CreateInstance(type)!;
            foreach (var item in array) list.Add(Decode(item, type.GenericTypeArguments[0]));
            return list;
        }
        if (Model.Values.Values.Contains(type))
        {
            if (node is not JsonObject record || record.Count != 2 || record["$type"]?.GetValue<string>() != type.Name || record["fields"] is not JsonObject fields)
                throw new InvalidDataException("Invalid component value");
            var value = Activator.CreateInstance(type)!;
            RestoreProperties(value, fields, Model.Observations(type));
            type.GetMethod("Validate")?.Invoke(value, null);
            return value;
        }
        throw new InvalidDataException($"Unsupported value type: {type}");
    }
    public static void RestoreProperties(object value, JsonObject fields, IEnumerable<PropertyInfo> properties)
    {
        var map = properties.ToDictionary(Model.Name);
        foreach (var field in fields)
        {
            if (!map.TryGetValue(field.Key, out var property)) throw new InvalidDataException($"Unknown saved field: {field.Key}");
            var nullable = new NullabilityInfoContext().Create(property).WriteState == NullabilityState.Nullable;
            property.SetValue(value, Decode(field.Value, property.PropertyType, nullable));
        }
    }
}
