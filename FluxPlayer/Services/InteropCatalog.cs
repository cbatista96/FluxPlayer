using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace FluxPlayer.Services;

public sealed record InteropAction(string Key, string Name, string Group, bool Writes, JsonElement Schema)
{ public override string ToString() => Name; }
public sealed record InteropResult(string Provider, string Operation, DateTimeOffset TimeUtc, JsonElement Data);

public static class InteropCatalog
{
    private static readonly Lazy<JsonElement> KodiTypes = new(() => Resource("kodi-types.json"));
    private static readonly Lazy<ImmutableArray<InteropAction>> KodiActions = new(() => Resource("kodi-methods.json").EnumerateObject().Select(p => new InteropAction(p.Name, Friendly(p.Name[(p.Name.IndexOf('.') + 1)..]), p.Name.Split('.')[0], !p.Value.TryGetProperty("permission", out var permission) || permission.GetString() != "ReadData", p.Value.Clone())).ToImmutableArray());
    private static readonly Lazy<ImmutableArray<InteropAction>> MpdActions = new(() => Resource("mpd-commands.json").EnumerateArray().Select(p => new InteropAction(p.GetProperty("Key").GetString()!, p.GetProperty("Name").GetString()!, p.GetProperty("Group").GetString()!, p.GetProperty("Writes").GetBoolean(), p.Clone())).ToImmutableArray());
    public static ImmutableArray<InteropAction> Kodi => KodiActions.Value;
    public static ImmutableArray<InteropAction> Mpd => MpdActions.Value;
    public static string Friendly(string value) => System.Text.RegularExpressions.Regex.Replace(value, "(?<=[a-z0-9])(?=[A-Z])", " ", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    internal static JsonElement Resource(string name)
    {
        using var stream = typeof(InteropCatalog).Assembly.GetManifestResourceStream("FluxPlayer.Data.Interop." + name) ?? throw new IOException("The installed application lacks " + name + ".");
        using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 64 }); return doc.RootElement.Clone();
    }
    public static JsonElement ParseInput(string text)
    { if (System.Text.Encoding.UTF8.GetByteCount(text) > 65536) throw new ArgumentException("Keep request parameters within 64 KiB."); using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 16 }); Bound(doc.RootElement, 0); return doc.RootElement.Clone(); }
    private static void Bound(JsonElement value, int depth)
    {
        if (depth > 16) throw new ArgumentException("Request nesting exceeds sixteen levels.");
        if (value.ValueKind == JsonValueKind.String && value.GetString()!.Length > 32768) throw new ArgumentException("A request string exceeds 32,768 characters.");
        if (value.ValueKind == JsonValueKind.Array) { if (value.GetArrayLength() > 200) throw new ArgumentException("A request array exceeds 200 entries."); foreach (var child in value.EnumerateArray()) Bound(child, depth + 1); }
        if (value.ValueKind == JsonValueKind.Object) { var properties = value.EnumerateObject().ToArray(); if (properties.Length > 100 || properties.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length) throw new ArgumentException("Use at most 100 distinct object fields."); foreach (var property in properties) Bound(property.Value, depth + 1); }
    }
    public static void ValidateKodi(InteropAction action, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new ArgumentException("Kodi parameters must be a named JSON object.");
        var parameters = action.Schema.TryGetProperty("params", out var declared) ? declared.EnumerateArray().ToArray() : Array.Empty<JsonElement>();
        foreach (var property in value.EnumerateObject()) if (!parameters.Any(p => p.GetProperty("name").GetString() == property.Name)) throw new ArgumentException("Unknown parameter: " + property.Name);
        foreach (var parameter in parameters)
        {
            string name = parameter.GetProperty("name").GetString()!;
            if (!value.TryGetProperty(name, out var item)) { if (Required(parameter)) throw new ArgumentException("Enter " + name + "."); continue; }
            ValidateSchema(parameter, item, name, 0);
        }
    }
    private static bool Required(JsonElement schema) => schema.TryGetProperty("required", out var r) && r.ValueKind == JsonValueKind.True;
    private static void ValidateSchema(JsonElement schema, JsonElement value, string path, int depth)
    {
        if (depth > 32) throw new ArgumentException("Schema recursion limit at " + path);
        if (schema.ValueKind == JsonValueKind.String) { if (!KodiTypes.Value.TryGetProperty(schema.GetString()!, out var named)) throw new ArgumentException("Unknown schema reference."); ValidateSchema(named, value, path, depth + 1); return; }
        if (schema.TryGetProperty("$ref", out var reference)) { if (!KodiTypes.Value.TryGetProperty(reference.GetString()!, out var target)) throw new ArgumentException("Unknown Kodi parameter reference."); ValidateSchema(target, value, path, depth + 1); }
        if (schema.TryGetProperty("extends", out var inherited)) { if (inherited.ValueKind == JsonValueKind.Array) foreach (var parent in inherited.EnumerateArray()) ValidateSchema(parent, value, path, depth + 1); else ValidateSchema(inherited, value, path, depth + 1); }
        if (schema.TryGetProperty("type", out var type))
        {
            if (type.ValueKind == JsonValueKind.Array)
            {
                bool valid = false; foreach (var alternative in type.EnumerateArray()) { try { if (alternative.ValueKind == JsonValueKind.String) CheckType(alternative.GetString()!, value, path); else ValidateSchema(alternative, value, path, depth + 1); valid = true; break; } catch (ArgumentException) { } }
                if (!valid) throw new ArgumentException("No supported value type matches " + path + ".");
            }
            else CheckType(type.GetString()!, value, path);
        }
        if (schema.TryGetProperty("enum", out var enumeration) && !enumeration.EnumerateArray().Any(item => item.ToString() == value.ToString() && item.ValueKind == value.ValueKind)) throw new ArgumentException("Choose a documented value for " + path + ".");
        if (value.ValueKind == JsonValueKind.Number)
        {
            double number = value.GetDouble(); if (!double.IsFinite(number) || schema.TryGetProperty("minimum", out var min) && number < min.GetDouble() || schema.TryGetProperty("maximum", out var max) && number > max.GetDouble()) throw new ArgumentException("Number outside the allowed range: " + path);
        }
        if (value.ValueKind == JsonValueKind.String && schema.TryGetProperty("minLength", out var length) && value.GetString()!.Length < length.GetInt32()) throw new ArgumentException("Text too short: " + path);
        if (value.ValueKind == JsonValueKind.Array)
        {
            int count = value.GetArrayLength(); if (schema.TryGetProperty("minItems", out var min) && count < min.GetInt32() || schema.TryGetProperty("maxItems", out var max) && count > max.GetInt32()) throw new ArgumentException("Array length outside the allowed range: " + path);
            if (schema.TryGetProperty("items", out var items)) { if (items.ValueKind == JsonValueKind.Array) { var tuple = items.EnumerateArray().ToArray(); for (int i = 0; i < Math.Min(tuple.Length, count); i++) ValidateSchema(tuple[i], value[i], path + "[" + i + "]", depth + 1); } else foreach (var child in value.EnumerateArray()) ValidateSchema(items, child, path + "[]", depth + 1); }
        }
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("properties", out var properties)) foreach (var property in properties.EnumerateObject()) { if (value.TryGetProperty(property.Name, out var child)) ValidateSchema(property.Value, child, path + "." + property.Name, depth + 1); else if (Required(property.Value)) throw new ArgumentException("Missing " + path + "." + property.Name); }
            foreach (var property in value.EnumerateObject())
            {
                if (schema.TryGetProperty("properties", out var defined) && defined.TryGetProperty(property.Name, out _)) continue;
                if (schema.TryGetProperty("additionalProperties", out var extra)) { if (extra.ValueKind == JsonValueKind.False) throw new ArgumentException("Unknown field " + path + "." + property.Name); if (extra.ValueKind == JsonValueKind.Object) ValidateSchema(extra, property.Value, path + "." + property.Name, depth + 1); }
            }
        }
    }
    private static void CheckType(string type, JsonElement value, string path)
    {
        bool valid = type switch { "any" => true, "string" => value.ValueKind == JsonValueKind.String, "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _), "number" => value.ValueKind == JsonValueKind.Number, "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False, "object" => value.ValueKind == JsonValueKind.Object, "array" => value.ValueKind == JsonValueKind.Array, "null" => value.ValueKind == JsonValueKind.Null, _ => false };
        if (!valid) throw new ArgumentException("Expected " + type + " at " + path + ".");
    }
    public static string KodiTemplate(InteropAction action)
    {
        var values = new Dictionary<string, object?>(); if (action.Schema.TryGetProperty("params", out var parameters)) foreach (var parameter in parameters.EnumerateArray()) if (Required(parameter)) values[parameter.GetProperty("name").GetString()!] = Sample(parameter, 0);
        return JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true });
    }
    private static string TextSample(JsonElement schema)
    {
        int length = schema.TryGetProperty("minLength", out var minimumLength) ? minimumLength.GetInt32() : 0;
        if (length > 32768) throw new InvalidDataException("A Kodi schema requests an unsupported sample string length.");
        return new string('x', Math.Max(1, length));
    }
    private static object? Sample(JsonElement schema, int depth)
    {
        if (depth > 8) return null;
        if (schema.TryGetProperty("default", out var fallback))
        {
            try { ValidateSchema(schema, fallback, "default", 0); return fallback.Clone(); }
            catch (ArgumentException) { }
        }
        if (schema.TryGetProperty("enum", out var enumeration)) return enumeration.EnumerateArray().First().Clone();
        if (schema.TryGetProperty("$ref", out var reference) && KodiTypes.Value.TryGetProperty(reference.GetString()!, out var target)) return Sample(target, depth + 1);
        if (!schema.TryGetProperty("type", out var type)) return null;
        if (type.ValueKind == JsonValueKind.Array) { var first = type.EnumerateArray().First(); if (first.ValueKind == JsonValueKind.Object) return Sample(first, depth + 1); type = first; }
        return type.GetString() switch
        {
            "object" => schema.TryGetProperty("properties", out var properties) ? properties.EnumerateObject().Where(p => Required(p.Value)).ToDictionary(p => p.Name, p => Sample(p.Value, depth + 1)) : new Dictionary<string, object?>(),
            "array" => schema.TryGetProperty("minItems", out var min) && min.GetInt32() > 0 && schema.TryGetProperty("items", out var items) ? Enumerable.Range(0, Math.Min(20, min.GetInt32())).Select(_ => Sample(items, depth + 1)).ToArray() : Array.Empty<object>(),
            "integer" or "number" => schema.TryGetProperty("minimum", out var number) ? (object)number.Clone() : 0,
            "boolean" => false, "string" => TextSample(schema), _ => null
        };
    }
    public static Task Export(InteropResult result, string path, CancellationToken token)
    { string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }); if (System.Text.Encoding.UTF8.GetByteCount(json) > 16 * 1024 * 1024) throw new IOException("The report exceeds 16 MiB; request a narrower view."); return OfflineMediaJob.Publish(path, ".json", [], staging => File.WriteAllTextAsync(staging, json, token), token); }
}
