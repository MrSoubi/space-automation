using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using SpaceAutomation.Game;
using System.Numerics;
namespace SpaceAutomation.Persistence;

public static class ValueCodec
{
    public static JsonNode? Encode(object? value)
    {
        if (value is null)
        {
            return null;
        }
        if (value is Vector2 v)
        {
            return new JsonObject { ["$type"] = "Vector2", ["x"] = v.X, ["y"] = v.Y };
        }
        if (value is CommandResult r)
        {
            return new JsonObject { ["$type"] = "CommandResult", ["accepted"] = r.Accepted, ["reason"] = r.Reason };
        }
        if (value is float number && !float.IsFinite(number))
        {
            string representation;
            if (float.IsNaN(number))
            {
                representation = "nan";
            }
            else if (number > 0)
            {
                representation = "inf";
            }
            else
            {
                representation = "-inf";
            }

            return new JsonObject { ["$type"] = "Float", ["value"] = representation };
        }
        var type = value.GetType();
        if (Model.Values.Values.Contains(type))
        {
            var fields = new JsonObject();
            foreach (var property in Model.Observations(type))
            {
                fields[Model.Name(property)] = Encode(property.GetValue(value));
            }
            return new JsonObject { ["$type"] = type.Name, ["fields"] = fields };
        }
        if (value is string text)
        {
            return JsonValue.Create(text);
        }
        if (value is bool boolean)
        {
            return JsonValue.Create(boolean);
        }
        if (value is float f)
        {
            return JsonValue.Create(f);
        }
        if (value is int i)
        {
            return JsonValue.Create(i);
        }
        if (value is long l)
        {
            return JsonValue.Create(l);
        }
        if (value is IDictionary dictionary)
        {
            var result = new JsonObject();
            foreach (DictionaryEntry pair in dictionary)
            {
                result.Add((string)pair.Key, Encode(pair.Value));
            }
            return result;
        }
        if (value is IEnumerable sequence)
        {
            var array = new JsonArray();
            foreach (var item in sequence)
            {
                array.Add(Encode(item));
            }
            return array;
        }
        throw new InvalidDataException($"Unsupported value type: {type}");
    }
    public static object? Decode(JsonNode? node, Type type, bool nullable = false)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (node is null)
        {
            if (nullable || underlying is not null)
            {
                return null;
            }
            throw new InvalidDataException("Unexpected null");
        }
        if (underlying is not null)
        {
            type = underlying;
        }
        if (type == typeof(string))
        {
            return node.GetValue<string>();
        }
        if (type == typeof(bool))
        {
            return node.GetValue<bool>();
        }
        if (type == typeof(float))
        {
            var special = node as JsonObject;
            if (special is not null)
            {
                var typeNode = special["$type"];
                if (typeNode is not null && typeNode.GetValue<string>() == "Float")
                {
                    var valueNode = special["value"];
                    if (valueNode is null)
                    {
                        throw new InvalidDataException("Missing float tag value");
                    }

                    string representation = valueNode.GetValue<string>();
                    switch (representation)
                    {
                        case "nan":
                            return float.NaN;
                        case "inf":
                            return float.PositiveInfinity;
                        case "-inf":
                            return float.NegativeInfinity;
                        default:
                            throw new InvalidDataException("Invalid float tag");
                    }
                }
            }
            // Read JSON numbers as floats regardless of their in-memory numeric type.
            return node.Deserialize<float>();
        }
        if (type == typeof(long))
        {
            return node.GetValue<long>();
        }
        if (type == typeof(int))
        {
            return node.GetValue<int>();
        }
        if (type == typeof(Vector2))
        {
            var vector = node as JsonObject;
            if (vector is null || vector.Count != 3)
            {
                throw new InvalidDataException("Invalid Vector2");
            }

            var typeNode = vector["$type"];
            if (typeNode is null || typeNode.GetValue<string>() != "Vector2")
            {
                throw new InvalidDataException("Invalid Vector2 type");
            }

            var xNode = vector["x"];
            var yNode = vector["y"];
            if (xNode is null || yNode is null)
            {
                throw new InvalidDataException("Vector2 needs x and y");
            }

            float x = xNode.Deserialize<float>();
            float y = yNode.Deserialize<float>();
            var v = new Vector2(x, y);
            return v;
        }
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            if (node is not JsonArray array)
            {
                throw new InvalidDataException("Expected array");
            }
            var list = Activator.CreateInstance(type) as IList;
            if (list is null)
            {
                throw new InvalidDataException($"Cannot create list: {type}");
            }

            Type itemType = type.GenericTypeArguments[0];
            foreach (var item in array)
            {
                object? value = Decode(item, itemType);
                list.Add(value);
            }
            return list;
        }
        if (Model.Values.Values.Contains(type))
        {
            var record = node as JsonObject;
            if (record is null || record.Count != 2)
            {
                throw new InvalidDataException("Invalid component value");
            }

            var typeNode = record["$type"];
            if (typeNode is null || typeNode.GetValue<string>() != type.Name)
            {
                throw new InvalidDataException("Invalid component type");
            }

            var fields = record["fields"] as JsonObject;
            if (fields is null)
            {
                throw new InvalidDataException("Component value needs fields");
            }

            object? value = Activator.CreateInstance(type);
            if (value is null)
            {
                throw new InvalidDataException($"Cannot create component value: {type}");
            }

            RestoreProperties(value, fields, Model.Observations(type));
            var validate = type.GetMethod("Validate");
            if (validate is not null)
            {
                validate.Invoke(value, null);
            }
            return value;
        }
        throw new InvalidDataException($"Unsupported value type: {type}");
    }
    public static void RestoreProperties(object value, JsonObject fields, IEnumerable<PropertyInfo> properties)
    {
        var map = properties.ToDictionary(Model.Name);
        foreach (var field in fields)
        {
            if (!map.TryGetValue(field.Key, out var property))
            {
                throw new InvalidDataException($"Unknown saved field: {field.Key}");
            }
            var context = new NullabilityInfoContext();
            var nullability = context.Create(property);
            bool nullable = nullability.WriteState == NullabilityState.Nullable;
            object? restoredValue = Decode(field.Value, property.PropertyType, nullable);
            property.SetValue(value, restoredValue);
        }
    }
}
