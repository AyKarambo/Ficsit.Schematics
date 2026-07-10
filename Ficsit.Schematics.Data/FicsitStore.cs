using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Saves;
using Ficsit.Schematics.Core.Serialization;
using LiteDB;

namespace Ficsit.Schematics.Data;

/// <summary>
/// Embedded document store (LiteDB, single data file). Factories are saved as real
/// BSON documents in the .sfmd shape — no relational mapping. Collections:
/// <c>factories</c> (working copy + named saves), <c>backups</c> (rolling
/// autosaves), <c>settings</c> (one document).
/// </summary>
public sealed class FicsitStore : IDisposable
{
    private const string CurrentName = "__current__";

    private readonly LiteDatabase _db;

    public FicsitStore(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        _db = new LiteDatabase(new ConnectionString
        {
            Filename = databasePath,
            Connection = ConnectionType.Direct,
        });
        Factories.EnsureIndex(f => f["name"]);
        Backups.EnsureIndex(b => b["createdUtc"]);
    }

    private ILiteCollection<BsonDocument> Factories => _db.GetCollection("factories");
    private ILiteCollection<BsonDocument> Backups => _db.GetCollection("backups");
    private ILiteCollection<AppSettings> Settings => _db.GetCollection<AppSettings>("settings");

    // -------------------------------------------------------------- factories

    public void SaveCurrent(FactoryDocument document) => SaveNamed(CurrentName, document);

    public FactoryDocument? LoadCurrent() => LoadNamed(CurrentName);

    public void SaveNamed(string name, FactoryDocument document)
    {
        var bson = ToBson(document);
        var existing = Factories.FindOne(Query.EQ("name", name));
        var doc = existing ?? new BsonDocument();
        doc["name"] = name;
        doc["modifiedUtc"] = DateTime.UtcNow;
        doc["factory"] = bson;
        Factories.Upsert(doc);
    }

    public FactoryDocument? LoadNamed(string name)
    {
        var doc = Factories.FindOne(Query.EQ("name", name));
        if (doc is null || doc["factory"] is not BsonDocument factory) return null;
        return FromBson(factory);
    }

    public void DeleteNamed(string name)
        => Factories.DeleteMany(Query.EQ("name", name));

    /// <summary>Named saves, newest first (the working copy is excluded).</summary>
    public IReadOnlyList<(string Name, DateTime ModifiedUtc)> ListSaves()
        => Factories.FindAll()
            .Where(d => d["name"].AsString != CurrentName)
            .Select(d => (d["name"].AsString, d["modifiedUtc"].AsDateTime))
            .OrderByDescending(x => x.Item2)
            .ToList();

    // ---------------------------------------------------------------- backups

    public void AddBackup(FactoryDocument document, int maxBackups)
    {
        Backups.Insert(new BsonDocument
        {
            ["createdUtc"] = DateTime.UtcNow,
            ["factory"] = ToBson(document),
        });
        var excess = Backups.Count() - Math.Max(1, maxBackups);
        if (excess <= 0) return;
        foreach (var old in Backups.Find(Query.All("createdUtc")).Take(excess).ToList())
            Backups.Delete(old["_id"]);
    }

    public IReadOnlyList<(BsonValue Id, DateTime CreatedUtc)> ListBackups()
        => Backups.FindAll()
            .Select(b => (b["_id"], b["createdUtc"].AsDateTime))
            .OrderByDescending(x => x.Item2)
            .ToList();

    public FactoryDocument? LoadBackup(BsonValue id)
    {
        var doc = Backups.FindById(id);
        return doc?["factory"] is BsonDocument factory ? FromBson(factory) : null;
    }

    // -------------------------------------------------------------- map nodes

    /// <summary>Resource nodes imported from a Satisfactory save (map mode).</summary>
    public IReadOnlyList<ResourceNodeInfo> LoadMapNodes()
        => _db.GetCollection<ResourceNodeInfo>("mapnodes").FindAll().OrderBy(n => n.Id).ToList();

    public void SaveMapNodes(IReadOnlyList<ResourceNodeInfo> nodes)
    {
        var collection = _db.GetCollection<ResourceNodeInfo>("mapnodes");
        collection.DeleteAll();
        collection.InsertBulk(nodes);
    }

    // --------------------------------------------------------------- settings

    public AppSettings LoadSettings()
        => MigrateNames(Settings.FindById(1) ?? new AppSettings());

    /// <summary>Part/recipe/machine names persisted before a catalog rename keep acting:
    /// resolve every stored name through the alias table, exactly like the .sfmd reader
    /// does for documents (factories and backups already migrate via <see cref="FromBson"/>).</summary>
    private static AppSettings MigrateNames(AppSettings settings)
    {
        settings.PlannerDisabledRecipes = settings.PlannerDisabledRecipes
            .Select(NameAliases.Resolve).Distinct().ToList();

        var preferences = new Dictionary<string, int>();
        foreach (var (part, weight) in settings.PlannerResourcePreferences)
            preferences[NameAliases.Resolve(part)] = weight;
        settings.PlannerResourcePreferences = preferences;

        foreach (var machineDefault in settings.MachineDefaults)
            machineDefault.Name = NameAliases.Resolve(machineDefault.Name);
        return settings;
    }

    public void SaveSettings(AppSettings settings)
    {
        settings.Id = 1;
        Settings.Upsert(settings);
    }

    // ------------------------------------------------------------- conversion

    private static BsonDocument ToBson(FactoryDocument document)
        => (BsonDocument)JsonSerializer.Deserialize(SfmdSerializer.Serialize(document));

    private static FactoryDocument FromBson(BsonDocument bson)
        => SfmdSerializer.Deserialize(JsonSerializer.Serialize(bson));

    public void Dispose() => _db.Dispose();
}
