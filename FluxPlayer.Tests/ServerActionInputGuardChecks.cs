using FluxPlayer.Services;

internal static class ServerActionInputGuardChecks
{
    private sealed record GuardCase(ServerActionDefinition Definition, Dictionary<string, string> Input, bool Valid, string Description);

    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        var definitions = ServerActionCatalog.Subsonic
            .Select(definition => (Provider: "Subsonic", Definition: definition))
            .Concat(ServerActionCatalog.Personal.Select(definition => (Provider: "Jellyfin/Emby", Definition: definition)))
            .Concat(ServerActionCatalog.Plex.Select(definition => (Provider: "Plex", Definition: definition)))
            .Concat(ServerActionCatalog.Upnp.Select(definition => (Provider: "UPnP", Definition: definition)))
            .ToArray();

        var validCases = new List<(string Provider, GuardCase Case)>();
        foreach (var (provider, definition) in definitions)
        {
            var input = ValidInput(definition);
            validCases.Add((provider, new(definition, input, true, "valid generated input")));
        }

        var invalidCases = new List<(string Provider, GuardCase Case, string Field)>();
        foreach (var (provider, definition) in definitions)
        foreach (var field in definition.Fields)
        foreach (int variant in Enumerable.Range(1, 4))
        {
            var input = ValidInput(definition);
            string? invalid = InvalidValue(field, variant - 1);
            if (invalid is null) continue;
            input[field.Key] = invalid;
            string description = variant == 1 ? field.Key + " rejects malformed value" : field.Key + " rejects boundary variant " + (variant - 1);
            invalidCases.Add((provider, new(definition, input, false, description), field.Key));
        }

        var balancedInvalidCases = new List<(string Provider, GuardCase Case)>();
        var invalidGroups = invalidCases
            .GroupBy(item => (item.Provider, item.Case.Definition.Key, item.Field))
            .Select(group => group.ToArray())
            .ToArray();
        for (int variant = 0; invalidGroups.Any(group => variant < group.Length); variant++)
        foreach (var group in invalidGroups)
            if (variant < group.Length)
                balancedInvalidCases.Add((group[variant].Provider, group[variant].Case));

        var cases = validCases.Concat(balancedInvalidCases).ToArray();
        if (cases.Length < 200)
            throw new InvalidOperationException($"Server action catalog produced only {cases.Length} input guards; 200 are required.");

        foreach (var (provider, guard) in cases.Take(200))
        {
            await test($"{provider} action input guard: {guard.Definition.Key} {guard.Description}", () =>
            {
                if (guard.Valid)
                {
                    _ = ServerActionCatalog.Validate(guard.Definition, guard.Input);
                    return Task.CompletedTask;
                }

                try
                {
                    _ = ServerActionCatalog.Validate(guard.Definition, guard.Input);
                }
                catch (ArgumentException)
                {
                    return Task.CompletedTask;
                }

                throw new InvalidOperationException($"Expected invalid input for {guard.Definition.Key}: {guard.Description}.");
            });
        }
    }

    internal static Dictionary<string, string> ValidInput(ServerActionDefinition definition)
    {
        var input = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in definition.Fields)
        {
            string value = field.Default;
            if (field.Kind == "integer")
            {
                if (!long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long number) || number < field.Minimum || number > field.Maximum)
                    value = field.Minimum.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (value.Length == 0)
            {
                value = field.Kind switch
                {
                    "ids" => "fixture-item-1\nfixture-item-2",
                    "indices" => "0\n1",
                    "url" => "https://media.example/fixture.wav",
                    "integer" => field.Minimum.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "choice" => field.Choices[0],
                    _ => "fixture-value"
                };
            }
            input[field.Key] = value;
        }

        if (definition.Key == "year-albums")
        {
            input["from"] = "2000";
            input["to"] = "2020";
        }
        return input;
    }

    private static string? InvalidValue(ServerActionField field, int variant)
    {
        return field.Kind switch
        {
            "integer" => variant switch
            {
                0 => "not-a-number",
                1 => (field.Minimum - 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                2 => (field.Maximum + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                3 => "1.5",
                _ => ""
            },
            "choice" => variant switch
            {
                0 => "__unsupported_choice__",
                1 => field.Choices[0].ToUpperInvariant() + "__invalid",
                _ => null
            },
            "ids" => variant switch
            {
                0 => string.Join('\n', Enumerable.Range(0, 51).Select(index => "item-" + index)),
                1 => new string('x', 513),
                2 => "item-1\nbad\0item",
                _ => null
            },
            "indices" => variant switch
            {
                0 => "-1",
                1 => "not-an-index",
                2 => "1\n1",
                3 => "1000001",
                _ => null
            },
            "url" => variant switch
            {
                0 => "file:///fixture.wav",
                1 => new UriBuilder("https", "media.example") { UserName = "fixture-user" }.Uri.AbsoluteUri,
                2 => "https://media.example/fixture.wav#fragment",
                3 => new string('x', 4097),
                _ => null
            },
            _ => variant switch
            {
                0 => new string('x', 513),
                1 => "fixture\0value",
                2 => "fixture\u0001value",
                _ => null
            }
        };
    }
}
