using System.Collections;
using System.Reflection;
using ACadSharp;
using ACadSharp.Objects;
using ACadSharp.Tables;

namespace PurgeCad.Engines.ACadSharp;

/// <summary>
/// Finds every table entry / style that something in the drawing points to.
///
/// Deliberately conservative: it walks the object graph by reflection, so any property of any entity, table entry
/// or dictionary object that holds a reference counts as "used" — including ones this code does not know by name.
/// Wrongly keeping an unused item is harmless; wrongly removing a used one would damage the drawing.
/// </summary>
internal sealed class ReferenceCollector
{
    /// <summary>Back-pointers that must not count as usage (an entity's owner block is not "used" by the entity).</summary>
    static readonly HashSet<string> SkippedProperties = new(StringComparer.Ordinal)
    {
        "Owner", "Document", "Reactors", "BlockOwner", "BlockEnd", "Preview", "ProxyGeometries",
    };

    static readonly Dictionary<Type, PropertyInfo[]> PropertyCache = new();
    const int MaxDepth = 16;

    readonly HashSet<object> _referenced = new(ReferenceEqualityComparer.Instance);
    readonly HashSet<object> _visited = new(ReferenceEqualityComparer.Instance);
    object? _currentRoot;

    public bool IsReferenced(object entry) => _referenced.Contains(entry);

    /// <summary>Collects references from the whole document.</summary>
    public static ReferenceCollector Collect(CadDocument doc)
    {
        var c = new ReferenceCollector();

        // Entities of every block record (model space, layouts, block definitions).
        foreach (var record in doc.BlockRecords)
        {
            c.WalkRoot(record, record.BlockEntity);
            foreach (var entity in record.Entities) c.WalkRoot(record, entity);
        }

        // Table entries pointing to other entries (layer → linetype, dimstyle → text style / arrow blocks, …).
        foreach (var e in doc.Layers) c.WalkRoot(e, e);
        foreach (var e in doc.LineTypes) c.WalkRoot(e, e);
        foreach (var e in doc.TextStyles) c.WalkRoot(e, e);
        foreach (var e in doc.DimensionStyles) c.WalkRoot(e, e);
        foreach (var e in doc.VPorts) c.WalkRoot(e, e);
        foreach (var e in doc.Views) c.WalkRoot(e, e);
        foreach (var e in doc.UCSs) c.WalkRoot(e, e);

        // Everything reachable from the named object dictionary (styles, layouts, groups, xrecords, app data, …).
        c.WalkRoot(doc.RootDictionary, doc.RootDictionary);

        return c;
    }

    void WalkRoot(object root, object? start)
    {
        if (start is null) return;
        _currentRoot = root;
        Walk(start, 0);
    }

    void Walk(object obj, int depth)
    {
        // One visited set for the whole document: shared helper objects are walked once.
        if (depth > MaxDepth || !_visited.Add(obj)) return;

        foreach (var property in PropertiesOf(obj.GetType()))
        {
            object? value;
            try { value = property.GetValue(obj); }
            catch { continue; } // computed properties may throw on partial data; ignore them

            Visit(value, depth + 1);
        }

        // Being listed in a dictionary is not usage: walk the entries' contents without marking the entries.
        if (obj is CadDictionary dictionary)
            foreach (var item in dictionary)
            {
                if (item is CadDictionary) Visit(item, depth + 1);
                else if (item is not null) Walk(item, depth + 1);
            }
    }

    void Visit(object? value, int depth)
    {
        switch (value)
        {
            case null:
            case string:
                return;

            case CadDictionary dictionary:
                Walk(dictionary, depth);
                return;

            // A table entry or named object (style, block record, layer, …) pointed to by something: it is used.
            // It is not walked from here; it is walked as a root of its own.
            case TableEntry or NonGraphicalObject:
                if (!ReferenceEquals(value, _currentRoot)) _referenced.Add(value);
                if (value is XRecord) Walk(value, depth);
                return;

            case IDictionary map:
                foreach (DictionaryEntry entry in map) { Visit(entry.Key, depth); Visit(entry.Value, depth); }
                return;

            case IEnumerable sequence when value is not CadObject:
                foreach (var item in sequence) Visit(item, depth);
                return;
        }

        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum) return;

        if (type.IsValueType)
        {
            // Only key/value pairs (extended data keyed by AppId) are worth looking into; geometry structs are not.
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                Walk(value, depth);
            return;
        }

        // Entities owned by other entities (attributes, vertices, …) and helper classes (context data, cell styles).
        if (type.Namespace?.StartsWith("ACadSharp", StringComparison.Ordinal) == true)
            Walk(value, depth);
    }

    static PropertyInfo[] PropertiesOf(Type type)
    {
        lock (PropertyCache)
        {
            if (!PropertyCache.TryGetValue(type, out var props))
            {
                props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && !SkippedProperties.Contains(p.Name))
                    .ToArray();
                PropertyCache[type] = props;
            }
            return props;
        }
    }
}
