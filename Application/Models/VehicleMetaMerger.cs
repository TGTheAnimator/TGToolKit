using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace ToolKitV.Models
{
    /// <summary>
    /// Merges per-vehicle FiveM meta files into single consolidated master files.
    ///
    /// Solves the GTA V "productId != ProductID::INVALID" fatal crash caused by
    /// vehicle packs that mount hundreds of individual .meta files as separate
    /// mini-DLCs, exhausting the engine's hardcoded ~255-product ID pool.
    ///
    /// Supported meta types:
    ///   • vehicles.meta       — CVehicleModelInfo__InitDataList
    ///   • handling.meta       — CHandlingDataMgr
    ///   • carcols.meta        — CVehicleModelInfoVarGlobal (with modkit ID conflict resolution)
    ///   • carvariations.meta  — CVehicleModelInfoVariation
    ///   • vehiclelayouts.meta — CVehicleMetadataMgr
    /// </summary>
    public static class VehicleMetaMerger
    {
        // ── Public data structures ────────────────────────────────────────────

        public struct MergeResults
        {
            public int VehiclesMerged;
            public int TxdRelationshipsMerged;
            public int HandlingMerged;
            public int KitsMerged;
            public int LightsMerged;
            public int SirenSettingsMerged;
            public int VariationsMerged;
            public int LayoutsMerged;
            public int ConflictsResolved;
            public int DuplicatesSkipped;
            public List<string> Warnings;

            // File counts per type
            public int VehiclesFilesFound;
            public int HandlingFilesFound;
            public int CarcolsFilesFound;
            public int VariationsFilesFound;
            public int LayoutsFilesFound;
        }

        private record KitEntry(XElement Element, string SourceFile, int OriginalId, string KitName);

        private record ConflictLog(string SourceFile, string KitName, int OldId, int NewId);

        // ── Meta file name constants ──────────────────────────────────────────

        private const string VehiclesMeta   = "vehicles.meta";
        private const string HandlingMeta   = "handling.meta";
        private const string CarcolsMeta    = "carcols.meta";
        private const string VariationsMeta = "carvariations.meta";
        private const string LayoutsMeta    = "vehiclelayouts.meta";

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Scans <paramref name="resourceDirectory"/> recursively for per-vehicle meta files,
        /// merges them into master files, writes them to
        /// <c>[resourceDirectory]/data/</c>, and optionally removes the originals.
        ///
        /// All original meta files are backed up to <paramref name="backupDirectory"/>
        /// (if non-empty) before any writes occur.
        /// </summary>
        public static MergeResults MergeVehicleMetas(
            string   resourceDirectory,
            string   backupDirectory,
            bool     removeSourceFiles,
            Delegate progressHandler)
        {
            MergeResults results = new() { Warnings = new List<string>() };

            // 1 ── Discover all meta files grouped by type
            var discovered = DiscoverMetaFiles(resourceDirectory);

            results.VehiclesFilesFound   = discovered.GetValueOrDefault(VehiclesMeta)?.Count  ?? 0;
            results.HandlingFilesFound   = discovered.GetValueOrDefault(HandlingMeta)?.Count  ?? 0;
            results.CarcolsFilesFound    = discovered.GetValueOrDefault(CarcolsMeta)?.Count   ?? 0;
            results.VariationsFilesFound = discovered.GetValueOrDefault(VariationsMeta)?.Count ?? 0;
            results.LayoutsFilesFound    = discovered.GetValueOrDefault(LayoutsMeta)?.Count   ?? 0;

            var log = new LogWriter("=== TGToolKit Vehicle Meta Merge started ===");
            log.LogWrite($"Resource directory: {resourceDirectory}");
            LogDiscovery(discovered, log);

            // Nothing to merge
            if (discovered.Values.All(l => l.Count == 0))
            {
                log.LogWrite("No meta files found. Aborting.");
                return results;
            }

            // 2 ── Backup all originals before touching anything
            if (!string.IsNullOrEmpty(backupDirectory))
                BackupAll(discovered, resourceDirectory, backupDirectory, log);

            progressHandler?.DynamicInvoke(results, 5);

            // 3 ── Merge each type (carcols first — produces the kit ID remapping
            //      that carvariations needs to correct its kit references)
            Dictionary<string, Dictionary<int, int>> kitIdRemapping   = new();
            Dictionary<string, Dictionary<int, int>> sirenIdRemapping = new();

            XDocument? vehiclesDoc   = null;
            XDocument? handlingDoc   = null;
            XDocument? carcolsDoc    = null;
            XDocument? variationsDoc = null;
            XDocument? layoutsDoc    = null;

            var carcolsFiles    = discovered.GetValueOrDefault(CarcolsMeta)    ?? new();
            var variationsFiles = discovered.GetValueOrDefault(VariationsMeta) ?? new();
            var vehiclesFiles   = discovered.GetValueOrDefault(VehiclesMeta)   ?? new();
            var handlingFiles   = discovered.GetValueOrDefault(HandlingMeta)   ?? new();
            var layoutsFiles    = discovered.GetValueOrDefault(LayoutsMeta)    ?? new();

            if (carcolsFiles.Count > 0)
            {
                log.LogWrite($"Merging {carcolsFiles.Count} carcols.meta files...");
                (carcolsDoc, var kitConflicts, var sirenConflicts) =
                    MergeCarColsMeta(carcolsFiles, out kitIdRemapping, out sirenIdRemapping, log);
                results.KitsMerged          = carcolsDoc.Root?.Element("Kits")?.Elements("Item").Count()         ?? 0;
                results.LightsMerged        = carcolsDoc.Root?.Element("Lights")?.Elements("Item").Count()        ?? 0;
                results.SirenSettingsMerged = carcolsDoc.Root?.Element("sirenSettings")?.Elements("Item").Count() ?? 0;
                results.ConflictsResolved   = kitConflicts.Count + sirenConflicts.Count;
                foreach (var c in kitConflicts)
                    results.Warnings.Add($"Kit ID conflict: '{c.KitName}' in {Path.GetDirectoryName(c.SourceFile)} remapped {c.OldId} → {c.NewId}");
                foreach (var c in sirenConflicts)
                    results.Warnings.Add($"Siren ID conflict in {Path.GetDirectoryName(c.SourceFile)}: ID {c.OldId} → {c.NewId}");
            }

            progressHandler?.DynamicInvoke(results, 25);

            if (variationsFiles.Count > 0)
            {
                log.LogWrite($"Merging {variationsFiles.Count} carvariations.meta files...");
                (variationsDoc, int dupes) = MergeCarVariationsMeta(variationsFiles, kitIdRemapping, sirenIdRemapping, log);
                results.VariationsMerged = variationsDoc.Root?.Element("variationData")?.Elements("Item").Count() ?? 0;
                results.DuplicatesSkipped += dupes;
            }

            progressHandler?.DynamicInvoke(results, 45);

            if (vehiclesFiles.Count > 0)
            {
                log.LogWrite($"Merging {vehiclesFiles.Count} vehicles.meta files...");
                (vehiclesDoc, int dupes) = MergeVehiclesMeta(vehiclesFiles, log);
                results.VehiclesMerged         = vehiclesDoc.Root?.Element("InitDatas")?.Elements("Item").Count() ?? 0;
                results.TxdRelationshipsMerged = vehiclesDoc.Root?.Element("txdRelationships")?.Elements("Item").Count() ?? 0;
                results.DuplicatesSkipped += dupes;
            }

            progressHandler?.DynamicInvoke(results, 60);

            if (handlingFiles.Count > 0)
            {
                log.LogWrite($"Merging {handlingFiles.Count} handling.meta files...");
                (handlingDoc, int dupes) = MergeHandlingMeta(handlingFiles, log);
                results.HandlingMerged = handlingDoc.Root?.Element("HandlingData")?.Elements("Item").Count() ?? 0;
                results.DuplicatesSkipped += dupes;
            }

            progressHandler?.DynamicInvoke(results, 75);

            if (layoutsFiles.Count > 0)
            {
                log.LogWrite($"Merging {layoutsFiles.Count} vehiclelayouts.meta files...");
                (layoutsDoc, int dupes) = MergeVehicleLayoutsMeta(layoutsFiles, log);
                results.LayoutsMerged = layoutsDoc.Root?.Element("layouts")?.Elements("Item").Count() ?? 0;
                results.DuplicatesSkipped += dupes;
            }

            // 4 ── Write merged output to [resource]/data/
            string outputDataDir = Path.Combine(resourceDirectory, "data");
            Directory.CreateDirectory(outputDataDir);

            WriteMergedDoc(vehiclesDoc,   Path.Combine(outputDataDir, VehiclesMeta),   log);
            WriteMergedDoc(handlingDoc,   Path.Combine(outputDataDir, HandlingMeta),   log);
            WriteMergedDoc(carcolsDoc,    Path.Combine(outputDataDir, CarcolsMeta),    log);
            WriteMergedDoc(variationsDoc, Path.Combine(outputDataDir, VariationsMeta), log);
            WriteMergedDoc(layoutsDoc,    Path.Combine(outputDataDir, LayoutsMeta),    log);

            progressHandler?.DynamicInvoke(results, 88);

            // 5 ── Generate fxmanifest snippet
            GenerateFxManifestSnippet(resourceDirectory, discovered, log);

            // 6 ── Write merge report
            WriteMergeReport(resourceDirectory, results, log);

            // 7 ── Optionally remove source meta files (never touches the merged outputs we just wrote)
            if (removeSourceFiles)
                RemoveSourceFiles(discovered, outputDataDir, log);

            progressHandler?.DynamicInvoke(results, 100);
            log.LogWrite("=== TGToolKit Vehicle Meta Merge finished ===");
            return results;
        }

        // ── Discovery ─────────────────────────────────────────────────────────

        private static Dictionary<string, List<string>> DiscoverMetaFiles(string root)
        {
            string[] targets = { VehiclesMeta, HandlingMeta, CarcolsMeta, VariationsMeta, LayoutsMeta };
            var result = targets.ToDictionary(t => t, _ => new List<string>());

            foreach (string file in Directory.EnumerateFiles(root, "*.meta", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(file).ToLowerInvariant();
                if (result.TryGetValue(name, out var list))
                    list.Add(file);
            }

            return result;
        }

        // ── Merge: vehicles.meta ──────────────────────────────────────────────

        private static (XDocument doc, int duplicatesSkipped) MergeVehiclesMeta(
            List<string> files, LogWriter log)
        {
            var initItems = new List<XElement>();
            var txdItems  = new List<XElement>();
            var seenModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int dupes = 0;

            foreach (string file in files)
            {
                XDocument doc = SafeLoad(file, log);
                XElement? root = doc.Root;

                foreach (XElement item in root?.Element("InitDatas")?.Elements("Item") ?? Enumerable.Empty<XElement>())
                {
                    string? model = item.Element("modelName")?.Value?.Trim();
                    if (model != null && seenModels.Add(model))
                        initItems.Add(new XElement(item)); // deep clone
                    else
                        dupes++;
                }

                foreach (XElement item in root?.Element("txdRelationships")?.Elements("Item") ?? Enumerable.Empty<XElement>())
                    txdItems.Add(new XElement(item));
            }

            return (new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XElement("CVehicleModelInfo__InitDataList",
                    new XElement("residentTxd", "vehshare"),
                    new XElement("residentAnims"),
                    new XElement("InitDatas",      initItems),
                    new XElement("txdRelationships", txdItems)
                )), dupes);
        }

        // ── Merge: handling.meta ──────────────────────────────────────────────

        private static (XDocument doc, int duplicatesSkipped) MergeHandlingMeta(
            List<string> files, LogWriter log)
        {
            var items = new List<XElement>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int dupes = 0;

            foreach (string file in files)
            {
                XDocument doc = SafeLoad(file, log);
                foreach (XElement item in doc.Root?.Element("HandlingData")?.Elements("Item") ?? Enumerable.Empty<XElement>())
                {
                    string? name = item.Element("handlingName")?.Value?.Trim();
                    if (name != null && seenNames.Add(name))
                        items.Add(new XElement(item));
                    else
                        dupes++;
                }
            }

            return (new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XElement("CHandlingDataMgr",
                    new XElement("HandlingData", items)
                )), dupes);
        }

        // ── Merge: carcols.meta (with modkit ID + siren ID conflict resolution) ─

        private static (XDocument doc, List<ConflictLog> kitConflicts, List<ConflictLog> sirenConflicts) MergeCarColsMeta(
            List<string> files,
            out Dictionary<string, Dictionary<int, int>> kitIdRemapping,
            out Dictionary<string, Dictionary<int, int>> sirenIdRemapping,
            LogWriter log)
        {
            kitIdRemapping   = new();
            sirenIdRemapping = new();
            var kitConflicts   = new List<ConflictLog>();
            var sirenConflicts = new List<ConflictLog>();

            // Load all entries, preserving source file path
            var allKits   = new List<KitEntry>();
            var allLights = new List<XElement>();
            // (sirenFile, originalId, element)
            var allSirens = new List<(string File, int Id, XElement Element)>();

            foreach (string file in files)
            {
                XDocument doc = SafeLoad(file, log);

                foreach (XElement kit in doc.Root?.Element("Kits")?.Elements("Item") ?? Enumerable.Empty<XElement>())
                {
                    string? idStr = kit.Element("id")?.Attribute("value")?.Value;
                    string  name  = kit.Element("kitName")?.Value?.Trim() ?? "unknown";
                    int     id    = int.TryParse(idStr, out int kp) ? kp : -1;
                    allKits.Add(new KitEntry(new XElement(kit), file, id, name));
                }

                foreach (XElement light in doc.Root?.Element("Lights")?.Elements("Item") ?? Enumerable.Empty<XElement>())
                    allLights.Add(new XElement(light));

                foreach (XElement siren in doc.Root?.Element("sirenSettings")?.Elements("Item") ?? Enumerable.Empty<XElement>())
                {
                    string? idStr = siren.Element("id")?.Attribute("value")?.Value;
                    int id = int.TryParse(idStr, out int sp) ? sp : -1;
                    allSirens.Add((file, id, new XElement(siren)));
                }
            }

            // ── Kit ID conflict resolution ──────────────────────────────────────
            var kitGrouped = allKits
                .Where(k => k.OriginalId >= 0)
                .GroupBy(k => k.OriginalId)
                .ToDictionary(g => g.Key, g => g.ToList());

            int nextKitId  = kitGrouped.Keys.Count > 0 ? kitGrouped.Keys.Max() + 1 : 1000;
            var mergedKits = new List<XElement>();

            foreach (var group in kitGrouped.Values.OrderBy(g => g[0].OriginalId))
            {
                mergedKits.Add(group[0].Element);
                for (int i = 1; i < group.Count; i++)
                {
                    KitEntry entry  = group[i];
                    XElement cloned = new(entry.Element);
                    cloned.Element("id")?.SetAttributeValue("value", nextKitId.ToString());

                    if (!kitIdRemapping.ContainsKey(entry.SourceFile))
                        kitIdRemapping[entry.SourceFile] = new();
                    kitIdRemapping[entry.SourceFile][entry.OriginalId] = nextKitId;

                    kitConflicts.Add(new ConflictLog(entry.SourceFile, entry.KitName, entry.OriginalId, nextKitId));
                    log.LogWrite($"  [CONFLICT] Kit '{entry.KitName}' ID {entry.OriginalId} → {nextKitId} ({Path.GetDirectoryName(entry.SourceFile)})");
                    mergedKits.Add(cloned);
                    nextKitId++;
                }
            }
            foreach (var kit in allKits.Where(k => k.OriginalId < 0))
                mergedKits.Add(kit.Element);

            // ── Siren ID conflict resolution ────────────────────────────────────
            var sirenGrouped = allSirens
                .Where(s => s.Id >= 0)
                .GroupBy(s => s.Id)
                .ToDictionary(g => g.Key, g => g.ToList());

            int nextSirenId  = sirenGrouped.Keys.Count > 0 ? sirenGrouped.Keys.Max() + 1 : 10000;
            var mergedSirens = new List<XElement>();

            foreach (var group in sirenGrouped.Values.OrderBy(g => g[0].Id))
            {
                mergedSirens.Add(group[0].Element);
                for (int i = 1; i < group.Count; i++)
                {
                    var (srcFile, oldId, elem) = group[i];
                    XElement cloned = new(elem);
                    cloned.Element("id")?.SetAttributeValue("value", nextSirenId.ToString());

                    if (!sirenIdRemapping.ContainsKey(srcFile))
                        sirenIdRemapping[srcFile] = new();
                    sirenIdRemapping[srcFile][oldId] = nextSirenId;

                    sirenConflicts.Add(new ConflictLog(srcFile, $"siren#{oldId}", oldId, nextSirenId));
                    log.LogWrite($"  [CONFLICT] Siren ID {oldId} → {nextSirenId} ({Path.GetDirectoryName(srcFile)})");
                    mergedSirens.Add(cloned);
                    nextSirenId++;
                }
            }
            foreach (var (_, id, elem) in allSirens.Where(s => s.Id < 0))
                mergedSirens.Add(elem);

            var root = new XElement("CVehicleModelInfoVarGlobal",
                new XElement("Kits",   mergedKits),
                new XElement("Lights", allLights));
            if (mergedSirens.Count > 0)
                root.Add(new XElement("sirenSettings", mergedSirens));

            return (new XDocument(new XDeclaration("1.0", "UTF-8", null), root),
                kitConflicts, sirenConflicts);
        }

        // ── Merge: carvariations.meta ─────────────────────────────────────────

        private static (XDocument doc, int duplicatesSkipped) MergeCarVariationsMeta(
            List<string> files,
            Dictionary<string, Dictionary<int, int>> kitIdRemapping,
            Dictionary<string, Dictionary<int, int>> sirenIdRemapping,
            LogWriter log)
        {
            var items = new List<XElement>();
            var seenModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int dupes = 0;

            foreach (string file in files)
            {
                XDocument doc = SafeLoad(file, log);
                string fileDir = Path.GetDirectoryName(file)!;

                // Find the remappings that apply to carcols.meta in the same folder
                Dictionary<int, int>? kitRemap = kitIdRemapping
                    .Where(kvp => string.Equals(Path.GetDirectoryName(kvp.Key) ?? "", fileDir ?? "", StringComparison.OrdinalIgnoreCase))
                    .Select(kvp => kvp.Value)
                    .FirstOrDefault();

                Dictionary<int, int>? sirenRemap = sirenIdRemapping
                    .Where(kvp => string.Equals(Path.GetDirectoryName(kvp.Key) ?? "", fileDir ?? "", StringComparison.OrdinalIgnoreCase))
                    .Select(kvp => kvp.Value)
                    .FirstOrDefault();

                foreach (XElement item in doc.Root?.Element("variationData")?.Elements("Item") ?? Enumerable.Empty<XElement>())
                {
                    string? model = item.Element("modelName")?.Value?.Trim();
                    if (model == null || !seenModels.Add(model)) { dupes++; continue; }

                    XElement cloned = new(item);

                    // Apply kit ID remapping
                    if (kitRemap != null)
                    {
                        foreach (XElement kitRef in cloned.Element("kits")?.Elements("Item") ?? Enumerable.Empty<XElement>())
                        {
                            XAttribute? val = kitRef.Attribute("value");
                            if (val != null && int.TryParse(val.Value, out int oldId) && kitRemap.TryGetValue(oldId, out int newId))
                                val.SetValue(newId.ToString());
                        }
                    }

                    // Apply siren ID remapping
                    if (sirenRemap != null)
                    {
                        XAttribute? sirenVal = cloned.Element("sirenSettings")?.Attribute("value");
                        if (sirenVal != null && int.TryParse(sirenVal.Value, out int oldSirenId) && sirenRemap.TryGetValue(oldSirenId, out int newSirenId))
                            sirenVal.SetValue(newSirenId.ToString());
                    }

                    items.Add(cloned);
                }
            }

            return (new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XElement("CVehicleModelInfoVariation",
                    new XElement("variationData", items)
                )), dupes);
        }

        // ── Merge: vehiclelayouts.meta ────────────────────────────────────────

        private static (XDocument doc, int duplicatesSkipped) MergeVehicleLayoutsMeta(
            List<string> files, LogWriter log)
        {
            var items = new List<XElement>();
            var seenLayouts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int dupes = 0;

            foreach (string file in files)
            {
                XDocument doc = SafeLoad(file, log);
                foreach (XElement item in doc.Root?.Element("layouts")?.Elements("Item") ?? Enumerable.Empty<XElement>())
                {
                    string? name = item.Element("layoutName")?.Value?.Trim();
                    if (name != null && seenLayouts.Add(name))
                        items.Add(new XElement(item));
                    else
                        dupes++;
                }
            }

            return (new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XElement("CVehicleMetadataMgr",
                    new XElement("layouts", items)
                )), dupes);
        }

        // ── Output helpers ────────────────────────────────────────────────────

        private static void WriteMergedDoc(XDocument? doc, string outputPath, LogWriter log)
        {
            if (doc is null) return;

            var settings = new System.Xml.XmlWriterSettings
            {
                Indent             = true,
                IndentChars        = "  ",
                Encoding           = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                NewLineOnAttributes = false,
            };

            using var writer = System.Xml.XmlWriter.Create(outputPath, settings);
            doc.Save(writer);

            log.LogWrite($"  Written: {outputPath}");
        }

        private static void GenerateFxManifestSnippet(
            string resourceDirectory,
            Dictionary<string, List<string>> discovered,
            LogWriter log)
        {
            bool hasVehicles   = (discovered.GetValueOrDefault(VehiclesMeta)?.Count   ?? 0) > 0;
            bool hasHandling   = (discovered.GetValueOrDefault(HandlingMeta)?.Count   ?? 0) > 0;
            bool hasCarcols    = (discovered.GetValueOrDefault(CarcolsMeta)?.Count    ?? 0) > 0;
            bool hasVariations = (discovered.GetValueOrDefault(VariationsMeta)?.Count ?? 0) > 0;
            bool hasLayouts    = (discovered.GetValueOrDefault(LayoutsMeta)?.Count    ?? 0) > 0;

            var sb = new StringBuilder();
            sb.AppendLine("-- ─────────────────────────────────────────────────────────────────");
            sb.AppendLine("-- Generated by TGToolKit — paste these blocks into fxmanifest.lua");
            sb.AppendLine("-- ─────────────────────────────────────────────────────────────────");
            sb.AppendLine();
            sb.AppendLine("files {");
            if (hasVehicles)   sb.AppendLine("    'data/vehicles.meta',");
            if (hasHandling)   sb.AppendLine("    'data/handling.meta',");
            if (hasCarcols)    sb.AppendLine("    'data/carcols.meta',");
            if (hasVariations) sb.AppendLine("    'data/carvariations.meta',");
            if (hasLayouts)    sb.AppendLine("    'data/vehiclelayouts.meta',");
            sb.AppendLine("}");
            sb.AppendLine();
            if (hasVehicles)   sb.AppendLine("data_file 'VEHICLE_METADATA_FILE'  'data/vehicles.meta'");
            if (hasHandling)   sb.AppendLine("data_file 'HANDLING_FILE'           'data/handling.meta'");
            if (hasCarcols)    sb.AppendLine("data_file 'CARCOLS_FILE'            'data/carcols.meta'");
            if (hasVariations) sb.AppendLine("data_file 'VEHICLE_VARIATION_FILE'  'data/carvariations.meta'");
            if (hasLayouts)    sb.AppendLine("data_file 'VEHICLE_LAYOUTS_FILE'    'data/vehiclelayouts.meta'");

            string snippetPath = Path.Combine(resourceDirectory, "fxmanifest_snippet.lua");
            File.WriteAllText(snippetPath, sb.ToString(), Encoding.UTF8);
            log.LogWrite($"  fxmanifest snippet: {snippetPath}");
        }

        private static void WriteMergeReport(
            string resourceDirectory,
            MergeResults results,
            LogWriter log)
        {
            var sb = new StringBuilder();
            sb.AppendLine("TGToolKit — Vehicle Meta Merge Report");
            sb.AppendLine(new string('─', 50));
            sb.AppendLine($"vehicles.meta    files found:  {results.VehiclesFilesFound}  →  {results.VehiclesMerged} vehicles merged");
            sb.AppendLine($"handling.meta    files found:  {results.HandlingFilesFound}  →  {results.HandlingMerged} entries merged");
            sb.AppendLine($"carcols.meta     files found:  {results.CarcolsFilesFound}  →  {results.KitsMerged} kits / {results.LightsMerged} lights / {results.SirenSettingsMerged} siren settings merged");
            sb.AppendLine($"carvariations    files found:  {results.VariationsFilesFound}  →  {results.VariationsMerged} variations merged");
            sb.AppendLine($"vehiclelayouts   files found:  {results.LayoutsFilesFound}  →  {results.LayoutsMerged} layouts merged");
            sb.AppendLine();
            sb.AppendLine($"Kit/Siren ID conflicts resolved: {results.ConflictsResolved}");
            sb.AppendLine($"Duplicates skipped:           {results.DuplicatesSkipped}");
            if (results.Warnings?.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("WARNINGS:");
                foreach (string w in results.Warnings)
                    sb.AppendLine($"  • {w}");
            }

            string reportPath = Path.Combine(resourceDirectory, "merge_report.txt");
            File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
            log.LogWrite($"  Merge report: {reportPath}");
        }

        // ── Source file cleanup ───────────────────────────────────────────────

        private static void RemoveSourceFiles(
            Dictionary<string, List<string>> discovered,
            string mergedOutputDir,
            LogWriter log)
        {
            // Canonical output paths — never delete these
            var protectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Path.Combine(mergedOutputDir, VehiclesMeta),
                Path.Combine(mergedOutputDir, HandlingMeta),
                Path.Combine(mergedOutputDir, CarcolsMeta),
                Path.Combine(mergedOutputDir, VariationsMeta),
                Path.Combine(mergedOutputDir, LayoutsMeta),
            };

            foreach (var fileList in discovered.Values)
            {
                foreach (string file in fileList)
                {
                    if (protectedPaths.Contains(file)) continue;
                    try
                    {
                        File.Delete(file);
                        log.LogWrite($"  Removed source: {file}");
                    }
                    catch (Exception ex)
                    {
                        log.LogWrite($"  WARNING: Could not remove {file}: {ex.Message}");
                    }
                }
            }

            // Prune empty directories (bottom-up)
            PruneEmptyDirectories(Path.GetDirectoryName(mergedOutputDir)!, log);
        }

        private static void PruneEmptyDirectories(string root, LogWriter log)
        {
            foreach (string dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                         .OrderByDescending(d => d.Length)) // deepest first
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    try { Directory.Delete(dir); log.LogWrite($"  Pruned empty dir: {dir}"); }
                    catch { /* best-effort */ }
                }
            }
        }

        // ── Backup ────────────────────────────────────────────────────────────

        private static void BackupAll(
            Dictionary<string, List<string>> discovered,
            string resourceDirectory,
            string backupDirectory,
            LogWriter log)
        {
            foreach (var fileList in discovered.Values)
            {
                foreach (string file in fileList)
                {
                    try
                    {
                        string rel  = Path.GetRelativePath(resourceDirectory, file);
                        string dest = Path.Combine(backupDirectory, rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                        File.Copy(file, dest, overwrite: true);
                    }
                    catch (Exception ex)
                    {
                        log.LogWrite($"  WARNING: backup failed for {file}: {ex.Message}");
                    }
                }
            }
            log.LogWrite($"  Backed up to: {backupDirectory}");
        }

        // ── Utilities ─────────────────────────────────────────────────────────

        private static XDocument SafeLoad(string path, LogWriter log)
        {
            try
            {
                return XDocument.Load(path);
            }
            catch (Exception ex)
            {
                log.LogWrite($"  WARNING: Failed to parse {path}: {ex.Message}");
                return new XDocument(new XElement("empty"));
            }
        }

        private static void LogDiscovery(Dictionary<string, List<string>> discovered, LogWriter log)
        {
            foreach (var (type, files) in discovered)
                log.LogWrite($"  Found {files.Count,3} × {type}");
        }
    }
}
