using System.Text.Json;
using Ficsit.Schematics.Core.Serialization;

namespace Ficsit.Schematics.Services;

/// <summary>
/// String tables from the reference app's languages/translations assets.
/// Keys are either UPPER_SNAKE ui ids or English part/recipe names.
/// </summary>
public sealed class LocalizationService
{
    private Dictionary<string, string> _strings = [];

    public string LanguageId { get; private set; } = "en-US";

    public event Action? LanguageChanged;

    public async Task LoadAsync(string languageId)
    {
        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync($"languages/translations/{languageId}.json");
            _strings = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
            LanguageId = languageId;
            LanguageChanged?.Invoke();
        }
        catch
        {
            if (languageId != "en-US") await LoadAsync("en-US");
        }
    }

    public string L(string key)
    {
        if (_strings.TryGetValue(key, out var text)) return text;
        // The reference string tables predate official-name adoption: a renamed part or
        // recipe (e.g. Screws) still has its translation under the legacy name.
        if (LegacyByOfficial.TryGetValue(key, out var legacy) && _strings.TryGetValue(legacy, out var legacyText))
            return legacyText;
        return key;
    }

    private static readonly IReadOnlyDictionary<string, string> LegacyByOfficial =
        NameAliases.ByLegacyName
            .GroupBy(kv => kv.Value)
            .ToDictionary(g => g.Key, g => g.First().Key, StringComparer.Ordinal);

    public async Task<IReadOnlyList<(string Id, string Name)>> ListLanguagesAsync()
    {
        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync("languages/languages.json");
            var map = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream) ?? [];
            return map.Select(kv => (kv.Key, kv.Value)).ToList();
        }
        catch
        {
            return [("en-US", "English (US)")];
        }
    }
}
