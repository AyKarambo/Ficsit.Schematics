using Ficsit.Schematics.Core.Editing;
using Ficsit.Schematics.Core.GameData;
using Ficsit.Schematics.Core.Model;
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

    public event Action? SelectionChanged;
    public event Action? SettingsChanged;

    public AppState(GameDatabase data, FicsitStore store)
    {
        Data = data;
        _store = store;
        Editor = new FactoryEditor(data);
        Settings = store.LoadSettings();

        var current = store.LoadCurrent();
        if (current is not null) Editor.LoadDocument(current);
    }

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
