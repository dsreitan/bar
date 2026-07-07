using System.Text.Json;
using Bff.Entitlements;

// License linter: validates publication DATA, independent of any user request.
// The other half of the code-vs-data split — the scenario corpus and property
// tests catch code bugs before deploy, this catches data bugs before a user does.
//
// Usage: LicenseLinter [data-directory]   (defaults to ./sample-data)
//   publications.json        [{ "id": 1, "name": "...", "descriptor": "..." }]
//   users.json               [{ "userId": "...", "publicationIds": [1, 2] }]  (optional)
//   content-vocabulary.json  { "LearningMaterial": ["multi", ...], ... }      (optional)

var dataDirectory = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "sample-data");
var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

var publications = Load<List<PublicationDto>>("publications.json") ?? [];
var users = Load<List<UserDto>>("users.json") ?? [];
var vocabulary = Load<Dictionary<string, List<string>>>("content-vocabulary.json") ?? [];

var errors = 0;
var warnings = 0;

foreach (var publication in publications)
{
    var parsed = DescriptorParser.Parse(publication.Descriptor);

    if (!parsed.SyntaxValid)
    {
        // Invalid syntax fails OPEN at evaluation time (intended policy so a
        // misconfiguration never locks users out) — which makes it the single
        // most urgent data defect: this publication currently grants EVERYTHING.
        Error($"pub {publication.Id} '{publication.Name}': INVALID descriptor syntax — grants the entire catalog fail-open: [{publication.Descriptor}]");
        continue;
    }

    foreach (var key in parsed.UnknownKeys)
        Error($"pub {publication.Id} '{publication.Name}': descriptor key '{key}' is not a known tag type — the constraint is silently dropped");

    if (parsed.Grants.Count == 0)
    {
        Error($"pub {publication.Id} '{publication.Name}': descriptor parses to zero constraints — this publication grants nothing");
        continue;
    }

    foreach (var (tagType, values) in parsed.Grants)
    {
        if (!vocabulary.TryGetValue(tagType.ToString(), out var knownValues))
            continue;

        var normalizedKnown = knownValues.Select(v => Normalizer.NormalizeValue(v)).ToList();
        foreach (var value in values.OfType<string>())
        {
            var coversSomething = normalizedKnown.Any(known =>
                known == value || known!.StartsWith(value + "/", StringComparison.Ordinal));
            if (!coversSomething)
                Warn($"pub {publication.Id} '{publication.Name}': {tagType}={value} matches no known content value — dead grant or typo?");
        }
    }
}

var registryIds = publications.Select(p => p.Id).ToHashSet();
foreach (var user in users)
foreach (var id in user.PublicationIds.Where(id => !registryIds.Contains(id)))
    Error($"user '{user.UserId}': license references publication {id} which is NOT in the registry — behaves exactly like no license (top incident suspect)");

Console.WriteLine();
Console.WriteLine($"Checked {publications.Count} publications, {users.Count} user licenses: {errors} error(s), {warnings} warning(s).");
return errors > 0 ? 1 : 0;

T? Load<T>(string fileName) where T : class
{
    var path = Path.Combine(dataDirectory, fileName);
    return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), jsonOptions) : null;
}

void Error(string message)
{
    errors++;
    Console.WriteLine($"ERROR   {message}");
}

void Warn(string message)
{
    warnings++;
    Console.WriteLine($"WARNING {message}");
}

internal sealed record PublicationDto(int Id, string Name, string? Descriptor);

internal sealed record UserDto(string UserId, List<int> PublicationIds);
