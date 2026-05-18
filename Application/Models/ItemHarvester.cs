using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using MoonSharp.Interpreter;

namespace ToolKitV.Models
{
    // 1. INotifyPropertyChanged allows the UI DataGrid to instantly save edits
    public class HarvestedItem : INotifyPropertyChanged
    {
        private string _spawnCode = string.Empty;
        private string _label = string.Empty;
        private float _weight;
        private string _imageFileName = string.Empty;
        private string _localImagePath = string.Empty;
        private string _rawLuaSnippet = string.Empty;

        public string SpawnCode { get => _spawnCode; set { _spawnCode = value; OnPropertyChanged(); } }
        public string Label { get => _label; set { _label = value; OnPropertyChanged(); } }
        public float Weight { get => _weight; set { _weight = value; OnPropertyChanged(); } }
        public string ImageFileName { get => _imageFileName; set { _imageFileName = value; OnPropertyChanged(); } }
        public string LocalImagePath { get => _localImagePath; set { _localImagePath = value; OnPropertyChanged(); } }
        public string RawLuaSnippet { get => _rawLuaSnippet; set { _rawLuaSnippet = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public static class ItemHarvester
    {
        public static List<HarvestedItem> HarvestFromDirectory(string scriptDirectory)
        {
            var items = new List<HarvestedItem>();
            if (!Directory.Exists(scriptDirectory)) return items;

            // FIX: Aggressively search for both .lua and .txt files
            var files = new List<string>();
            files.AddRange(Directory.GetFiles(scriptDirectory, "*.lua", SearchOption.AllDirectories));
            files.AddRange(Directory.GetFiles(scriptDirectory, "*.txt", SearchOption.AllDirectories));

            foreach (var file in files)
            {
                // Speed Optimization: Skip heavy client/server logic files. We only care about configs/shared data.
                string normPath = file.ToLowerInvariant();
                if (normPath.Contains("\\client") || normPath.Contains("\\server") || 
                    normPath.Contains("/client") || normPath.Contains("/server") ||
                    normPath.Contains("_client") || normPath.Contains("_server")) continue;

                try
                {
                    string text = File.ReadAllText(file);
                    
                    // Pre-filter: If it doesn't mention "weight" or "label", it's impossible for it to be an item file.
                    if (!text.Contains("weight") && !text.Contains("label")) continue;

                    ExtractWithMoonSharp(text, scriptDirectory, items);
                }
                catch
                {
                    // Fallback gracefully on read errors
                }
            }
            return items;
        }

        private static void ExtractWithMoonSharp(string luaText, string scriptDir, List<HarvestedItem> items)
        {
            Script script = new Script(CoreModules.Preset_SoftSandbox);
            
            // FIVEM NATIVE STUBS: Prevent the compiler from crashing if it hits GTA V natives inside a config file
            script.Globals["vec3"] = (Func<double, double, double, object>)((x, y, z) => new object());
            script.Globals["vector3"] = (Func<double, double, double, object>)((x, y, z) => new object());
            script.Globals["vec4"] = (Func<double, double, double, double, object>)((x, y, z, w) => new object());
            script.Globals["vector4"] = (Func<double, double, double, double, object>)((x, y, z, w) => new object());
            script.Globals["CreateThread"] = (Action<object>)((cb) => { });
            script.Globals["_U"] = (Func<string, object[], string>)((str, args) => str);
            script.Globals["joaat"] = (Func<string, int>)((str) => 0);

            Table? targetTable = null;

            try
            {
                // ATTEMPT 1: QBCore / Global Assignment (e.g. QBShared.Items = { ... })
                script.DoString(luaText);
                
                if (script.Globals.Get("QBShared").Type == DataType.Table)
                {
                    var qbShared = script.Globals.Get("QBShared").Table;
                    if (qbShared.Get("Items").Type == DataType.Table) targetTable = qbShared.Get("Items").Table;
                }
                else if (script.Globals.Get("Config").Type == DataType.Table)
                {
                    var config = script.Globals.Get("Config").Table;
                    if (config.Get("Items").Type == DataType.Table) targetTable = config.Get("Items").Table;
                }
                else if (script.Globals.Get("Items").Type == DataType.Table)
                {
                    targetTable = script.Globals.Get("Items").Table;
                }
            }
            catch
            {
                // ATTEMPT 2: Ox / QS Dictionary format (e.g. ['water'] = { ... })
                // This is not valid standalone Lua. We wrap it in a return statement to force table evaluation.
                try
                {
                    Script dictScript = new Script(CoreModules.Preset_SoftSandbox);
                    string wrapped = $"return {{\n{luaText}\n}}";
                    DynValue result = dictScript.DoString(wrapped);
                    if (result.Type == DataType.Table) targetTable = result.Table;
                }
                catch { return; } // Invalid syntax, skip file
            }

            if (targetTable == null) return;

            // Loop through the evaluated table and strictly validate items
            foreach (var pair in targetTable.Pairs)
            {
                if (pair.Key.Type != DataType.String || pair.Value.Type != DataType.Table) continue;

                string code = pair.Key.String;
                Table data = pair.Value.Table;

                // STRICT VALIDATION: It MUST have a label AND a weight to be considered an item.
                // This permanently stops "blips" and "zones" from being harvested.
                bool hasLabel = !data.Get("label").IsNil() || !data.Get("name").IsNil();
                bool hasWeight = !data.Get("weight").IsNil();

                if (!hasLabel || !hasWeight) continue;

                string label = data.Get("label").Type == DataType.String ? data.Get("label").String : code;
                float weight = data.Get("weight").Type == DataType.Number ? (float)data.Get("weight").Number : 0f;
                string imageProp = data.Get("image").Type == DataType.String ? data.Get("image").String : "";

                string imageName = string.IsNullOrEmpty(imageProp) ? $"{code}.png" : imageProp;
                string imagePath = FindImageFile(scriptDir, imageName);

                items.Add(new HarvestedItem
                {
                    SpawnCode = code,
                    Label = label,
                    Weight = weight,
                    ImageFileName = imageName,
                    LocalImagePath = imagePath,
                    RawLuaSnippet = $"-- Transpiled from {code}"
                });
            }
        }

        private static string FindImageFile(string rootDir, string imageName)
        {
            try
            {
                if (!Directory.Exists(rootDir)) return string.Empty;
                string[] images = Directory.GetFiles(rootDir, imageName, SearchOption.AllDirectories);
                return images.Length > 0 ? images[0] : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
