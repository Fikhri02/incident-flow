using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using IncidentFlow.BuildingBlocks.Contracts;

namespace IncidentFlow.Contracts.Tests;

/// <summary>
/// Finds the message contracts and describes the JSON a publisher puts on the wire for each one:
/// every property name with its JSON type (plus a format for ids and timestamps, and "?" when the
/// field may be null). The shape comes from really serializing a fully populated sample with the
/// System.Text.Json web defaults, so naming policy, attributes and converters all count.
/// </summary>
public static class ContractShape
{
    public static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    // The old envelope record is being removed (MassTransit's own envelope replaces it); it is not a message.
    private static readonly HashSet<string> NotMessages = ["IntegrationEvent"];

    public static IReadOnlyList<Type> MessageContracts { get; } = typeof(ErrorSignalReceived).Assembly
        .GetExportedTypes()
        .Where(t => t.Namespace == typeof(ErrorSignalReceived).Namespace)
        .Where(t => t is { IsClass: true, IsAbstract: false } && !NotMessages.Contains(t.Name))
        .OrderBy(t => t.Name, StringComparer.Ordinal)
        .ToList();

    public static Type Find(string name) => MessageContracts.Single(t => t.Name == name);

    /// <summary>Property name (as serialized) to type descriptor, in serialization order.</summary>
    public static IReadOnlyDictionary<string, string> Describe(Type contract)
    {
        var json = JsonSerializer.SerializeToElement(Sample(contract), contract, Wire);
        if (json.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"{contract.Name} does not serialize to a JSON object.");

        var clrByJsonName = contract.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(JsonName, StringComparer.Ordinal);
        var nullability = new NullabilityInfoContext();

        var shape = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in json.EnumerateObject())
        {
            var clr = clrByJsonName[property.Name];
            var nullable = nullability.Create(clr).ReadState == NullabilityState.Nullable;
            shape[property.Name] = DescribeValue(property.Value, clr.PropertyType) + (nullable ? "?" : "");
        }

        return shape;
    }

    /// <summary>A sample with every constructor parameter set (no nulls), deterministic per contract.</summary>
    public static object Sample(Type contract)
    {
        var constructor = contract.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        var arguments = constructor.GetParameters()
            .Select((p, i) => SampleValue(p.ParameterType, $"{contract.Name}.{p.Name}", i))
            .ToArray();
        return constructor.Invoke(arguments);
    }

    private static string JsonName(PropertyInfo property) =>
        property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
        ?? Wire.PropertyNamingPolicy?.ConvertName(property.Name)
        ?? property.Name;

    private static string DescribeValue(JsonElement value, Type clrType)
    {
        var type = Nullable.GetUnderlyingType(clrType) ?? clrType;
        return value.ValueKind switch
        {
            JsonValueKind.String when type == typeof(Guid) => "string:uuid",
            JsonValueKind.String when type == typeof(DateTimeOffset) || type == typeof(DateTime) => "string:date-time",
            JsonValueKind.String => "string",
            JsonValueKind.Number when IsInteger(type) => "integer",
            JsonValueKind.Number => "number",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            JsonValueKind.Array => $"array<{DescribeValue(value.EnumerateArray().First(), ElementType(type))}>",
            JsonValueKind.Object => "object",
            _ => throw new InvalidOperationException($"Sample produced {value.ValueKind} for {clrType.Name}; populate it.")
        };
    }

    private static bool IsInteger(Type type) =>
        type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte);

    private static Type ElementType(Type type) =>
        type.IsArray
            ? type.GetElementType()!
            : type.GetInterfaces().Append(type)
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                .Select(i => i.GetGenericArguments()[0])
                .First();

    private static object SampleValue(Type type, string seed, int index)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null) return SampleValue(underlying, seed, index);

        if (type == typeof(string)) return $"sample-{index}";
        if (type == typeof(Guid)) return DeterministicGuid(seed);
        if (type == typeof(DateTimeOffset)) return new DateTimeOffset(2026, 11, 23, 9, 0, index, TimeSpan.Zero);
        if (type == typeof(DateTime)) return new DateTime(2026, 11, 23, 9, 0, index, DateTimeKind.Utc);
        if (type == typeof(bool)) return true;
        if (type == typeof(int)) return index + 1;
        if (type == typeof(long)) return (long)index + 1;
        if (type == typeof(double)) return index + 0.5;
        if (type == typeof(decimal)) return index + 0.5m;

        if (type.IsArray || typeof(IEnumerable).IsAssignableFrom(type))
        {
            var element = ElementType(type);
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element))!;
            list.Add(SampleValue(element, seed + "[0]", 0));
            if (!type.IsArray) return list;
            var array = Array.CreateInstance(element, 1);
            list.CopyTo(array, 0);
            return array;
        }

        throw new NotSupportedException(
            $"No sample value for {type.Name} ({seed}). Add one to ContractShape.SampleValue.");
    }

    private static Guid DeterministicGuid(string seed)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(seed));
        return new Guid(bytes);
    }
}
