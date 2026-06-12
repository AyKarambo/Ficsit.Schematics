using Microsoft.Maui.Graphics.Platform;
using IImage = Microsoft.Maui.Graphics.IImage;

namespace Ficsit.Schematics.Services;

/// <summary>
/// Loads the reference artwork from raw assets and caches it both as drawable
/// images (canvas) and ImageSources (XAML lists). Part/machine names map to
/// "icons/Name_With_Underscores.png".
/// </summary>
public sealed class IconStore
{
    private readonly Dictionary<string, IImage?> _images = [];
    private readonly Dictionary<string, ImageSource?> _sources = [];

    public static string AssetPathFor(string name) => name switch
    {
        "Outpost" => "custom_icons/Outpost.png",
        "Blueprint" => "custom_icons/Blueprint.png",
        "Splurger" or "Priority Splitter" or "Priority Merger" or "Priority Splurger"
            => "ui_icons/machine.png",
        "AnyPart" => "custom_icons/anypart.png",
        _ => "icons/" + name.Replace(' ', '_').Replace(":", "") + ".png",
    };

    /// <summary>Drawable image for canvas rendering; null when the asset is missing.</summary>
    public IImage? GetImage(string name)
    {
        if (_images.TryGetValue(name, out var cached)) return cached;
        IImage? image = null;
        try
        {
            using var stream = FileSystem.OpenAppPackageFileAsync(AssetPathFor(name)).GetAwaiter().GetResult();
            image = PlatformImage.FromStream(stream);
        }
        catch
        {
            // Missing artwork is tolerated; the node falls back to text.
        }
        _images[name] = image;
        return image;
    }

    /// <summary>Arbitrary raw asset as a drawable image, e.g. "map/world_map.jpg".</summary>
    public IImage? GetAsset(string assetPath)
    {
        var key = "asset:" + assetPath;
        if (_images.TryGetValue(key, out var cached)) return cached;
        IImage? image = null;
        try
        {
            using var stream = FileSystem.OpenAppPackageFileAsync(assetPath).GetAwaiter().GetResult();
            image = PlatformImage.FromStream(stream);
        }
        catch { }
        _images[key] = image;
        return image;
    }

    /// <summary>UI icon from ui_icons/, e.g. "somersloop".</summary>
    public IImage? GetUiImage(string baseName)
    {
        var key = "ui:" + baseName;
        if (_images.TryGetValue(key, out var cached)) return cached;
        IImage? image = null;
        try
        {
            using var stream = FileSystem.OpenAppPackageFileAsync($"ui_icons/{baseName}.png").GetAwaiter().GetResult();
            image = PlatformImage.FromStream(stream);
        }
        catch { }
        _images[key] = image;
        return image;
    }

    /// <summary>ImageSource for XAML (recipe chooser rows, summary tables).</summary>
    public ImageSource? GetSource(string name)
    {
        if (_sources.TryGetValue(name, out var cached)) return cached;
        var path = AssetPathFor(name);
        ImageSource? source = null;
        try
        {
            source = ImageSource.FromStream(() =>
                FileSystem.OpenAppPackageFileAsync(path).GetAwaiter().GetResult());
        }
        catch { }
        _sources[name] = source;
        return source;
    }
}
