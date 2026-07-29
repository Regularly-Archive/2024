using System.Collections;
using System.Text.Json;

namespace InsightaAI.Agent.Abstractions;

/// <summary>
/// Reads and validates tool arguments against a tool's JSON schema.
/// </summary>
internal sealed class ToolArgumentReader
{
    private readonly JsonElement _properties;
    private readonly HashSet<string> _required;
    private readonly IDictionary<string, object> _arguments;

    public ToolArgumentReader(JsonElement schema, IDictionary<string, object> arguments)
    {
        if (schema.ValueKind != JsonValueKind.Object ||
            !schema.TryGetProperty("properties", out _properties) ||
            _properties.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Tool schema must define an object-valued properties member.", nameof(schema));
        }

        _required = schema.TryGetProperty("required", out var required) &&
            required.ValueKind == JsonValueKind.Array
            ? required.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!)
                .ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        _arguments = arguments;

        foreach (var key in arguments.Keys)
        {
            if (!_properties.TryGetProperty(key, out _))
                throw new ArgumentException($"Parameter '{key}' is not declared in the tool schema.");
        }
    }

    public string GetString(string key, string? defaultValue = null)
    {
        if (!TryGetString(key, out var value))
            return GetMissingValue(key, defaultValue);

        return value!;
    }

    /// <summary>
    /// Attempts to read a string value. Missing non-required values return false.
    /// </summary>
    public bool TryGetString(string key, out string? value)
    {
        var property = EnsureType(key, "string");
        if (!TryGetValue(key, out var argument))
        {
            if (_required.Contains(key))
                throw new ArgumentException($"Missing required parameter: {key}");

            value = null;
            return false;
        }

        value = argument switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => throw TypeError(key, "a string")
        };

        ValidateEnum(key, property, value!);
        return true;
    }

    public bool GetBoolean(string key, bool defaultValue = false)
    {
        EnsureType(key, "boolean");
        if (!TryGetValue(key, out var argumentValue))
            return defaultValue;

        return argumentValue switch
        {
            bool boolean => boolean,
            JsonElement { ValueKind: JsonValueKind.True } => true,
            JsonElement { ValueKind: JsonValueKind.False } => false,
            _ => throw TypeError(key, "a boolean")
        };
    }

    public int GetInt32(string key, int defaultValue = 0)
    {
        EnsureType(key, "integer");
        if (!TryGetValue(key, out var argumentValue))
            return defaultValue;

        return argumentValue switch
        {
            sbyte integer => integer,
            byte integer => integer,
            short integer => integer,
            ushort integer => integer,
            int integer => integer,
            uint integer when integer <= int.MaxValue => (int)integer,
            long integer when integer is >= int.MinValue and <= int.MaxValue => (int)integer,
            ulong integer when integer <= int.MaxValue => (int)integer,
            decimal number when number is >= int.MinValue and <= int.MaxValue && decimal.Truncate(number) == number => (int)number,
            double number when number is >= int.MinValue and <= int.MaxValue && Math.Truncate(number) == number => (int)number,
            float number when number is >= int.MinValue and <= int.MaxValue && MathF.Truncate(number) == number => (int)number,
            JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt32(out var integer) => integer,
            _ => throw TypeError(key, "an integer")
        };
    }

    public string[] GetStringArray(string key)
    {
        return TryGetStringArray(key, out var value) ? value! : Array.Empty<string>();
    }

    /// <summary>
    /// Attempts to read a string array. Missing non-required values return false.
    /// </summary>
    public bool TryGetStringArray(string key, out string[]? value)
    {
        var property = EnsureType(key, "array");
        if (!property.TryGetProperty("items", out var items) ||
            !items.TryGetProperty("type", out var itemType) ||
            itemType.GetString() != "string")
        {
            throw new ArgumentException($"Parameter '{key}' must be declared as an array of strings in the tool schema.");
        }

        if (!TryGetValue(key, out var argumentValue))
        {
            if (_required.Contains(key))
                throw new ArgumentException($"Missing required parameter: {key}");
            value = null;
            return false;
        }

        if (argumentValue is JsonElement { ValueKind: JsonValueKind.Array } jsonArray)
        {
            value = ReadJsonStringArray(key, jsonArray);
            return true;
        }

        // ToolRegistry converts JSON arrays to object[], while direct callers often use string[].
        if (argumentValue is IEnumerable enumerable && argumentValue is not string)
        {
            var values = enumerable.Cast<object?>().ToArray();
            if (values.Any(item => item is not string))
                throw TypeError(key, "an array of strings");

            value = values.Cast<string>()
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToArray();
            return true;
        }

        throw TypeError(key, "an array of strings");
    }

    private JsonElement EnsureType(string key, string expectedType)
    {
        if (!_properties.TryGetProperty(key, out var property))
            throw new ArgumentException($"Parameter '{key}' is not declared in the tool schema.");

        if (!property.TryGetProperty("type", out var type) || type.GetString() != expectedType)
        {
            throw new ArgumentException(
                $"Parameter '{key}' is declared as '{type.GetString() ?? "unknown"}' in the tool schema, not '{expectedType}'.");
        }

        return property;
    }

    private bool TryGetValue(string key, out object value)
    {
        return _arguments.TryGetValue(key, out value!) && value != null;
    }

    private string GetMissingValue(string key, string? defaultValue)
    {
        if (_required.Contains(key))
            throw new ArgumentException($"Missing required parameter: {key}");
        if (defaultValue == null)
            throw new ArgumentException($"Parameter '{key}' was not provided and has no default value.");
        return defaultValue;
    }

    private static string[] ReadJsonStringArray(string key, JsonElement jsonArray)
    {
        if (jsonArray.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
            throw TypeError(key, "an array of strings");

        return jsonArray.EnumerateArray()
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToArray();
    }

    private static void ValidateEnum(string key, JsonElement property, string value)
    {
        if (!property.TryGetProperty("enum", out var values) || values.ValueKind != JsonValueKind.Array)
            return;

        if (!values.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && item.GetString() == value))
            throw new ArgumentException($"Parameter '{key}' must be one of: {string.Join(", ", values.EnumerateArray().Select(item => item.GetString()))}.");
    }

    private static ArgumentException TypeError(string key, string expectedType) =>
        new($"Parameter '{key}' must be {expectedType}.");
}
