using Ficsit.Schematics.Core.Editing;
using Ficsit.Schematics.Core.GameData;
using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Saves;
using Ficsit.Schematics.Data;

namespace Ficsit.Schematics.Services;

/// <summary>
/// Application-wide state: the open editor, persisted settings, selection.
/// Owns the autosave loop.
/// </summary>
public sealed class AppState : IDisposable
{
    private readonly FicsitStore _store;
    private IDispatcherTimer? _autosaveTimer;

    public GameDatabase Data { get; }
    public FactoryEditor Editor { get; }
    public AppSettings Settings { get; private set; }

    public List<FactoryNode> Selection { get; } = [];

    /// <summary>Resource nodes imported from a Satisfactory save, for map mode.</summary>
    public IReadOnlyList<ResourceNodeInfo> MapNodes { get; private set; } = [];

    public event Action? SelectionChanged;
    public event Action? SettingsChanged;
    public event Action? MapNodesChanged;

    public AppState(GameDatabase data, FicsitStore store)
    {
        Data = data;
        _store = store;
        Editor = new FactoryEditor(data);
        Settings = store.LoadSettings();

        var current = store.LoadCurrent();
        if (current is not null) Editor.LoadDocument(current);
        MapNodes = store.LoadMapNodes();
    }

    public void ImportMapNodes(IReadOnlyList<ResourceNodeInfo> nodes)
    {
        MapNodes = nodes;
        _store.SaveMapNodes(nodes);
        MapNodesChanged?.Invoke();
    }

    /// <summary>The factory node occupying a map resource node, if any.</summary>
    public FactoryNode? OccupantOf(string resourceNodeId)
        => Editor.Document.Root.AllNodes()
            .FirstOrDefault(n => n.ResourceNodeId == resourceNodeId);

    /// <summary>Ids of all occupied map nodes (one pass; for per-frame rendering).</summary>
    public HashSet<string> OccupiedResourceNodes()
        => Editor.Document.Root.AllNodes()
            .Where(n => n.ResourceNodeId is not null)
            .Select(n => n.ResourceNodeId!)
            .ToHashSet();

    public void SetSelection(IEnumerable<FactoryNode> nodes)
    {
        Selection.Clear();
        Selection.AddRange(nodes);
        SelectionChanged?.Invoke();
    }

    public void ClearSelection() => SetSelection([]);

    public void SaveSettings()
    {
        _store.SaveSettings(Settings);
        SettingsChanged?.Invoke();
    }

    public void SaveNow()
    {
        _store.SaveCurrent(Editor.Document);
    }

    public void Backup()
    {
        _store.AddBackup(Editor.Document, Settings.MaxBackups);
    }

    public FicsitStore Store => _store;

    public void StartAutosave(IDispatcher dispatcher)
    {
        _autosaveTimer?.Stop();
        if (!Settings.Autosave) return;
        _autosaveTimer = dispatcher.CreateTimer();
        _autosaveTimer.Interval = TimeSpan.FromMinutes(Math.Max(1, Settings.AutosaveIntervalMinutes));
        _autosaveTimer.Tick += (_, _) =>
        {
            SaveNow();
            Backup();
        };
        _autosaveTimer.Start();
    }

    public void Dispose()
    {
        SaveNow();
        _store.Dispose();
    }
}
