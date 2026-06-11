using System.Text.Json;

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
        => _strings.TryGetValue(key, out var text) ? text : key;

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
