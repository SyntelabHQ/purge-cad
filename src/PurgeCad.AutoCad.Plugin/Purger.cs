using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;

namespace PurgeCad.AutoCad.Plugin
{
    /// <summary>
    /// A complete purge of one drawing, in the open database:
    ///  - what PURGE does (AutoCAD decides what is unused with Database.Purge), repeated until a pass removes
    ///    nothing, because removing one item frees others (nested blocks, the layers and styles they used);
    ///  - DGN line style data (ACAD_DGNLINESTYLECOMP) that no remaining linetype reaches; it keeps the DGN
    ///    linetypes imported from MicroStation alive, so PURGE alone never removes them;
    ///  - layer filters, unused annotation scales, and (only when asked) zero-length lines and empty texts.
    /// Xref-dependent items are never touched. Every step is guarded: a failing step is counted, the rest runs.
    /// </summary>
    sealed class Purger
    {
        const int MaxPasses = 50;
        const string DgnDictionary = "ACAD_DGNLINESTYLECOMP";

        // PurgeCAD category names (PurgeCad.Core PurgeCategory)
        const string Blocks = "Blocks", Layers = "Layers", Linetypes = "Linetypes", TextStyles = "TextStyles",
            DimensionStyles = "DimensionStyles", TableStyles = "TableStyles", MultileaderStyles = "MultileaderStyles",
            MultilineStyles = "MultilineStyles", Materials = "Materials", ViewStyles = "ViewStyles",
            RegisteredApps = "RegisteredApps", LayerFilters = "LayerFilters", Dgn = "DgnLinetypeComponents",
            Scales = "AnnotationScales", ZeroLength = "ZeroLengthGeometry", EmptyTexts = "EmptyTexts";

        /// <summary>Named-object dictionaries that hold purgeable styles, with the entries AutoCAD always keeps.</summary>
        /// (No value tuples: .NET Framework 4.6.2 does not have them.)
        static readonly string[][] StyleDictionaries =
        {
            new[] { "ACAD_TABLESTYLE", TableStyles, "Standard" },
            new[] { "ACAD_MLEADERSTYLE", MultileaderStyles, "Standard", "Annotative" },
            new[] { "ACAD_MLINESTYLE", MultilineStyles, "Standard" },
            new[] { "ACAD_MATERIAL", Materials, "ByLayer", "ByBlock", "Global" },
            new[] { "ACAD_DETAILVIEWSTYLE", ViewStyles, "Imperial24", "Metric50" },
            new[] { "ACAD_SECTIONVIEWSTYLE", ViewStyles, "Imperial24", "Metric50" },
        };

        readonly Database db;
        readonly Job job;
        readonly Report report;
        readonly Action<string> say;

        public Purger(Database db, Job job, Report report, Action<string> say)
        {
            this.db = db;
            this.job = job;
            this.report = report;
            this.say = say;
        }

        public void Run()
        {
            if (job.Has(LayerFilters)) Guard(RemoveLayerFilters);
            if (job.Has(ZeroLength) || job.Has(EmptyTexts)) Guard(RemoveEmptyGeometry);

            PurgeUntilStable(strictDgn: false);

            // Some DGN linetypes survive: their data and the linetype hold each other even when nothing in the
            // drawing uses the linetype. Those not in use lose their DGN data, then the purge runs again.
            if (job.Has(Dgn) && job.Has(Linetypes))
            {
                var stripped = 0;
                Guard(() => stripped = StripDgnDataOfUnusedLinetypes());
                if (stripped > 0)
                {
                    say(stripped + " unused DGN linetypes released");
                    PurgeUntilStable(strictDgn: true);
                }
            }

            if (job.Has(Scales)) Guard(RemoveUnusedScales);
            if (job.Has(Dgn)) Guard(NoteDgnLinetypesLeft);
        }

        void PurgeUntilStable(bool strictDgn)
        {
            for (var pass = 1; pass <= MaxPasses; pass++)
            {
                var before = report.Total;
                Guard(PurgePass);
                if (job.Has(Dgn)) Guard(() => RemoveUnreachedDgnComponents(strictDgn));
                report.Passes++;
                say("pass " + report.Passes + ", " + (report.Total - before) + " removed");
                if (report.Total == before) break;
            }
        }

        void Guard(Action step)
        {
            try { step(); }
            catch (System.Exception e)
            {
                report.Errors++;
                if (report.Notes.Count < 20) report.Notes.Add("step failed: " + e.GetType().Name + ": " + e.Message);
            }
        }

        static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        // ----------------------------------------------------------------- purge pass

        void PurgePass()
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var candidates = new Dictionary<ObjectId, string>();

                if (job.Has(Blocks))
                    foreach (ObjectId id in (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead))
                    {
                        if (id.IsErased) continue;
                        var block = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                        if (block.IsLayout || block.IsFromExternalReference || block.IsFromOverlayReference || block.IsDependent) continue;
                        candidates[id] = Blocks;
                    }

                AddTable(tr, Layers, db.LayerTableId, candidates, "0", "Defpoints");
                AddTable(tr, Linetypes, db.LinetypeTableId, candidates, "ByLayer", "ByBlock", "Continuous");
                AddTable(tr, TextStyles, db.TextStyleTableId, candidates, "Standard");
                AddTable(tr, DimensionStyles, db.DimStyleTableId, candidates, "Standard");
                AddTable(tr, RegisteredApps, db.RegAppTableId, candidates, "ACAD");
                foreach (var entry in StyleDictionaries)
                    AddDictionary(tr, entry[1], entry[0], candidates, entry.Skip(2).ToArray());

                // what the drawing has as current is never removed
                foreach (var current in CurrentObjects()) candidates.Remove(current);

                if (candidates.Count > 0)
                {
                    var ids = new ObjectIdCollection(candidates.Keys.ToArray());
                    db.Purge(ids); // keeps only the ids nothing references
                    foreach (ObjectId id in ids)
                    {
                        if (Erase(tr, id)) report.Add(candidates[id]);
                        else report.Errors++;
                    }
                }
                tr.Commit();
            }
        }

        IEnumerable<ObjectId> CurrentObjects()
        {
            var current = new List<Func<ObjectId>>
            {
                () => db.Clayer, () => db.Celtype, () => db.Textstyle, () => db.Dimstyle, () => db.Tablestyle,
                () => db.MLeaderstyle, () => db.CmlstyleID, () => db.ContinuousLinetype, () => db.ByLayerLinetype,
                () => db.ByBlockLinetype, () => db.LayerZero,
            };
            foreach (var get in current)
            {
                ObjectId id;
                try { id = get(); }
                catch (System.Exception) { continue; }
                if (!id.IsNull) yield return id;
            }
        }

        void AddTable(Transaction tr, string category, ObjectId tableId, Dictionary<ObjectId, string> candidates, params string[] keep)
        {
            if (!job.Has(category)) return;
            foreach (ObjectId id in (SymbolTable)tr.GetObject(tableId, OpenMode.ForRead))
            {
                if (id.IsErased) continue;
                var record = (SymbolTableRecord)tr.GetObject(id, OpenMode.ForRead);
                if (record.IsDependent || keep.Any(k => Is(k, record.Name))) continue; // xref items stay
                candidates[id] = category;
            }
        }

        void AddDictionary(Transaction tr, string category, string name, Dictionary<ObjectId, string> candidates, string[] keep)
        {
            if (!job.Has(category)) return;
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(name)) return;
            if (!(tr.GetObject(nod.GetAt(name), OpenMode.ForRead) is DBDictionary dictionary)) return;
            foreach (DBDictionaryEntry entry in dictionary)
                if (!entry.Value.IsErased && !keep.Any(k => Is(k, entry.Key))) candidates[entry.Value] = category;
        }

        static bool Erase(Transaction tr, ObjectId id)
        {
            try
            {
                tr.GetObject(id, OpenMode.ForWrite, false, true).Erase();
                return true;
            }
            catch (System.Exception) { return false; }
        }

        // ----------------------------------------------------------------- DGN line style data

        /// <summary>
        /// Erases the DGN components no linetype reaches. The walk starts at the linetypes (all remaining ones,
        /// or with <paramref name="onlyUsed"/> only those in use) and follows extension dictionaries, dictionary
        /// entries and xrecord pointers. It never walks through the dictionaries that hold every component,
        /// otherwise every component would look reached. Components that remaining linetypes need are kept,
        /// so the drawing looks the same.
        /// </summary>
        void RemoveUnreachedDgnComponents(bool onlyUsed)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                if (!nod.Contains(DgnDictionary)) return;
                var root = nod.GetAt(DgnDictionary);
                var components = new HashSet<ObjectId>();
                var containers = new HashSet<ObjectId> { db.NamedObjectsDictionaryId, root };
                CollectEntries(tr, root, components, containers);
                if (components.Count == 0) return;

                var used = onlyUsed ? UsedLinetypes(tr) : null;
                var reached = new HashSet<ObjectId>(containers);
                var queue = new Queue<ObjectId>();
                foreach (ObjectId id in (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead))
                {
                    if (id.IsErased || (used != null && !used.Contains(id))) continue;
                    queue.Enqueue(id);
                }
                while (queue.Count > 0)
                {
                    var id = queue.Dequeue();
                    if (id.IsNull || id.IsErased || !reached.Add(id)) continue;
                    foreach (var next in References(tr, id)) queue.Enqueue(next);
                }

                foreach (var id in components)
                {
                    if (reached.Contains(id)) continue;
                    if (Erase(tr, id)) report.Add(Dgn);
                    else report.Errors++;
                }
                tr.Commit();
            }
        }

        /// <summary>Entries of a dictionary and of its sub-dictionaries; the sub-dictionaries go in <paramref name="containers"/>.</summary>
        static void CollectEntries(Transaction tr, ObjectId dictionaryId, HashSet<ObjectId> entries, HashSet<ObjectId> containers)
        {
            if (!(tr.GetObject(dictionaryId, OpenMode.ForRead) is DBDictionary dictionary)) return;
            foreach (DBDictionaryEntry entry in dictionary)
            {
                if (entry.Value.IsErased) continue;
                if (tr.GetObject(entry.Value, OpenMode.ForRead) is DBDictionary)
                {
                    containers.Add(entry.Value);
                    CollectEntries(tr, entry.Value, entries, containers);
                }
                else entries.Add(entry.Value);
            }
        }

        /// <summary>What an object points to: its extension dictionary, dictionary entries, xrecord object pointers.</summary>
        static List<ObjectId> References(Transaction tr, ObjectId id)
        {
            var result = new List<ObjectId>();
            DBObject obj;
            try { obj = tr.GetObject(id, OpenMode.ForRead); }
            catch (System.Exception) { return result; }
            if (!obj.ExtensionDictionary.IsNull) result.Add(obj.ExtensionDictionary);
            if (obj is DBDictionary dictionary)
                foreach (DBDictionaryEntry entry in dictionary) result.Add(entry.Value);
            if (obj is Xrecord xrecord && xrecord.Data != null)
                foreach (TypedValue value in xrecord.Data)
                    if (value.TypeCode >= 330 && value.TypeCode <= 369 && value.Value is ObjectId target) result.Add(target);
            return result;
        }

        /// <summary>
        /// Linetypes not in use lose the DGN data in their extension dictionary, so PURGE can take them. Returns how
        /// many linetypes were released. A linetype in use is never changed.
        /// </summary>
        int StripDgnDataOfUnusedLinetypes()
        {
            var released = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var used = UsedLinetypes(tr);
                foreach (ObjectId id in (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead))
                {
                    if (id.IsErased || used.Contains(id)) continue;
                    var linetype = (LinetypeTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (linetype.IsDependent || linetype.ExtensionDictionary.IsNull) continue;
                    if (!(tr.GetObject(linetype.ExtensionDictionary, OpenMode.ForWrite) is DBDictionary xdict)) continue;
                    var entries = new List<DBDictionaryEntry>();
                    foreach (DBDictionaryEntry entry in xdict) entries.Add(entry);
                    if (entries.Count == 0) continue;
                    foreach (var entry in entries)
                    {
                        xdict.Remove(entry.Key);
                        EraseTree(tr, entry.Value);
                    }
                    released++;
                }
                tr.Commit();
            }
            return released;
        }

        static void EraseTree(Transaction tr, ObjectId id)
        {
            if (id.IsNull || id.IsErased) return;
            if (tr.GetObject(id, OpenMode.ForRead) is DBDictionary dictionary)
            {
                var children = new List<ObjectId>();
                foreach (DBDictionaryEntry entry in dictionary) children.Add(entry.Value);
                foreach (var child in children) EraseTree(tr, child);
            }
            Erase(tr, id);
        }

        /// <summary>
        /// Linetypes the drawing uses: the current one, layers, every entity (also inside blocks, and attributes),
        /// dimension and multiline / multileader styles, dimension overrides, and every xref-dependent linetype.
        /// </summary>
        HashSet<ObjectId> UsedLinetypes(Transaction tr)
        {
            var used = new HashSet<ObjectId> { db.Celtype, db.ContinuousLinetype, db.ByLayerLinetype, db.ByBlockLinetype };

            foreach (ObjectId id in (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead))
                if (!id.IsErased && ((LinetypeTableRecord)tr.GetObject(id, OpenMode.ForRead)).IsDependent) used.Add(id);

            foreach (ObjectId id in (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead))
                if (!id.IsErased) used.Add(((LayerTableRecord)tr.GetObject(id, OpenMode.ForRead)).LinetypeObjectId);

            foreach (ObjectId id in (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead))
            {
                if (id.IsErased) continue;
                var style = (DimStyleTableRecord)tr.GetObject(id, OpenMode.ForRead);
                Try(() => { used.Add(style.Dimltype); used.Add(style.Dimltex1); used.Add(style.Dimltex2); });
            }

            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (nod.Contains("ACAD_MLINESTYLE") && tr.GetObject(nod.GetAt("ACAD_MLINESTYLE"), OpenMode.ForRead) is DBDictionary mlines)
                foreach (DBDictionaryEntry entry in mlines)
                    if (!entry.Value.IsErased && tr.GetObject(entry.Value, OpenMode.ForRead) is MlineStyle mline)
                        foreach (MlineStyleElement element in mline.Elements) used.Add(element.LinetypeId);
            if (nod.Contains("ACAD_MLEADERSTYLE") && tr.GetObject(nod.GetAt("ACAD_MLEADERSTYLE"), OpenMode.ForRead) is DBDictionary leaders)
                foreach (DBDictionaryEntry entry in leaders)
                    if (!entry.Value.IsErased && tr.GetObject(entry.Value, OpenMode.ForRead) is MLeaderStyle leader)
                        Try(() => used.Add(leader.LeaderLineTypeId));

            foreach (ObjectId blockId in (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead))
            {
                if (blockId.IsErased) continue;
                foreach (ObjectId id in (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead))
                {
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Entity entity)) continue;
                    used.Add(entity.LinetypeId);
                    if (entity is BlockReference reference)
                        foreach (ObjectId attId in reference.AttributeCollection)
                            if (!attId.IsErased && tr.GetObject(attId, OpenMode.ForRead) is Entity attribute) used.Add(attribute.LinetypeId);
                    if (entity is Dimension dimension)
                        Try(() => { used.Add(dimension.Dimltype); used.Add(dimension.Dimltex1); used.Add(dimension.Dimltex2); });
                    if (entity is MLeader mleader)
                        Try(() => used.Add(mleader.LeaderLineTypeId));
                }
            }
            used.Remove(ObjectId.Null);
            return used;
        }

        static void Try(Action action)
        {
            try { action(); }
            catch (System.Exception) { }
        }

        /// <summary>For the log: DGN linetypes still in the drawing after the purge, and whether something uses them.</summary>
        void NoteDgnLinetypesLeft()
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var used = UsedLinetypes(tr);
                var left = 0;
                foreach (ObjectId id in (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead))
                {
                    if (id.IsErased) continue;
                    var linetype = (LinetypeTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (linetype.ExtensionDictionary.IsNull) continue;
                    left++;
                    if (left <= 40)
                        report.Notes.Add("DGN linetype left: " + linetype.Name + (used.Contains(id) ? " (in use)" : " (not in use)"));
                }
                if (left > 0) report.Notes.Add("DGN linetypes left: " + left);
                tr.Commit();
            }
        }

        // ----------------------------------------------------------------- layer filters

        void RemoveLayerFilters()
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var table = tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                if (table.ExtensionDictionary.IsNull) return;
                var xdict = (DBDictionary)tr.GetObject(table.ExtensionDictionary, OpenMode.ForRead);
                // layer states (ACAD_LAYERSTATES) are kept
                foreach (var name in new[] { "ACAD_LAYERFILTERS", "ACLYDICTIONARY" })
                {
                    if (!xdict.Contains(name)) continue;
                    var id = xdict.GetAt(name);
                    var count = tr.GetObject(id, OpenMode.ForRead) is DBDictionary filters ? Math.Max(1, filters.Count) : 1;
                    if (!xdict.IsWriteEnabled) xdict.UpgradeOpen();
                    xdict.Remove(id);
                    EraseTree(tr, id);
                    report.Add(LayerFilters, count);
                }
                tr.Commit();
            }
        }

        // ----------------------------------------------------------------- annotation scales

        /// <summary>
        /// Scales nothing uses (Database.Purge on ACAD_SCALELIST decides) are removed through the scale collection,
        /// except the current scale and 1:1. A scale is matched to its ACAD_SCALELIST object by UniqueIdentifier.
        /// </summary>
        void RemoveUnusedScales()
        {
            var scales = db.ObjectContextManager?.GetContextCollection("ACDB_ANNOTATIONSCALES");
            if (scales == null) return;
            var current = db.Cannoscale?.Name ?? "";

            var unused = new HashSet<long>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                if (!nod.Contains("ACAD_SCALELIST")) return;
                var list = (DBDictionary)tr.GetObject(nod.GetAt("ACAD_SCALELIST"), OpenMode.ForRead);
                var ids = new ObjectIdCollection();
                foreach (DBDictionaryEntry entry in list) if (!entry.Value.IsErased) ids.Add(entry.Value);
                db.Purge(ids);
                foreach (ObjectId id in ids) unused.Add(id.OldIdPtr.ToInt64());
                tr.Commit();
            }
            if (unused.Count == 0) return;

            var names = new List<string>();
            var matched = 0;
            foreach (ObjectContext context in scales)
            {
                if (!unused.Contains(context.UniqueIdentifier.ToInt64())) continue;
                matched++;
                if (Is(context.Name, current) || Is(context.Name, "1:1")) continue;
                names.Add(context.Name);
            }
            if (matched == 0)
            {
                report.Notes.Add("annotation scales could not be matched; none removed");
                return;
            }
            foreach (var name in names)
            {
                try
                {
                    scales.RemoveContext(name);
                    report.Add(Scales);
                }
                catch (System.Exception) { report.Errors++; }
            }
        }

        // ----------------------------------------------------------------- zero-length geometry, empty texts

        void RemoveEmptyGeometry()
        {
            const double Tolerance = 1e-9;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId blockId in (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead))
                {
                    if (blockId.IsErased) continue;
                    var block = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead);
                    if (block.IsFromExternalReference || block.IsFromOverlayReference || block.IsDependent) continue;
                    var doomed = new List<KeyValuePair<ObjectId, string>>();
                    foreach (ObjectId id in block)
                    {
                        if (id.IsErased) continue;
                        var entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        string category = null;
                        if (job.Has(ZeroLength))
                        {
                            if (entity is Line line && line.Length < Tolerance) category = ZeroLength;
                            else if (entity is Polyline polyline && polyline.Length < Tolerance) category = ZeroLength;
                        }
                        if (category == null && job.Has(EmptyTexts))
                        {
                            if (entity is DBText text && !(entity is AttributeDefinition) && string.IsNullOrWhiteSpace(text.TextString)) category = EmptyTexts;
                            else if (entity is MText mtext && string.IsNullOrWhiteSpace(mtext.Text)) category = EmptyTexts;
                        }
                        if (category != null) doomed.Add(new KeyValuePair<ObjectId, string>(id, category));
                    }
                    foreach (var pair in doomed)
                    {
                        if (Erase(tr, pair.Key)) report.Add(pair.Value);
                        else report.Errors++;
                    }
                }
                tr.Commit();
            }
        }
    }
}
