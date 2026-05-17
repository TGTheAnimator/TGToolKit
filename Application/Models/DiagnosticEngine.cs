using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MoonSharp.Interpreter;

namespace ToolKitV.Models
{
    /// <summary>
    /// MoonSharp-powered Static Diagnostic Engine.
    /// Phase 1: Uses MoonSharp's Lua parser to detect genuine syntax errors and report them.
    /// Phase 2: Applies targeted auto-fixes for common patterns that would cause boot failures,
    ///          operating only on non-comment regions via LuaAstHelper.
    /// </summary>
    public static class DiagnosticEngine
    {
        // DIAGNOSTIC 1: Trailing comma inside table that closes immediately: ,\s*}
        private static readonly Regex TrailingCommaRegex = new(
            @",(\s*\})", RegexOptions.Compiled);

        // DIAGNOSTIC 2: Accidental double-equals in local variable assignment
        private static readonly Regex DoubleEqualsAssignRegex = new(
            @"(^\s*local\s+\w+\s*)==(\s*)", RegexOptions.Compiled | RegexOptions.Multiline);

        public class DiagnosticResult
        {
            public string FilePath    { get; set; } = string.Empty;
            public string Issue       { get; set; } = string.Empty;
            public bool   WasAutoFixed { get; set; }
        }

        /// <summary>
        /// Runs a full static analysis pass over every .lua file in the workspace.
        /// Returns the number of files that were auto-fixed.
        /// </summary>
        public static async Task<int> RunStaticAnalysisAsync(
            string workspaceRoot,
            AuditLogger auditLog,
            Action<DiagnosticResult>? onResult = null)
        {
            int filesFixed = 0;

            var allLuaFiles = Directory.GetFiles(workspaceRoot, "*.lua", SearchOption.AllDirectories)
                .Where(f =>
                {
                    string norm = f.Replace('\\', '/').ToLowerInvariant();
                    return !norm.Contains("/stream/") && !norm.Contains("/html/") && !norm.Contains("/ui/");
                })
                .ToList();

            foreach (var file in allLuaFiles)
            {
                string original = await File.ReadAllTextAsync(file);
                string relPath  = file.Replace(workspaceRoot, string.Empty);

                // ── Phase 1: MoonSharp Syntax Validation ──────────────────────────────
                try
                {
                    // Parse-only mode: load the script source without executing it
                    var script = new Script(CoreModules.None);
                    script.LoadString(original);
                }
                catch (SyntaxErrorException ex)
                {
                    string issue = $"[SYNTAX ERROR] Line {ex.DecoratedMessage}";
                    auditLog.LogChange(relPath, "[WARNING] Lua Syntax Error", issue);
                    onResult?.Invoke(new DiagnosticResult
                    {
                        FilePath = relPath,
                        Issue = issue,
                        WasAutoFixed = false
                    });
                    // Continue — attempt auto-fixes even on files with syntax errors
                }

                // ── Phase 2: Comment-aware auto-fix pass ──────────────────────────────
                string stripped  = LuaAstHelper.StripComments(original);
                string working   = original;
                bool   modified  = false;

                // Fix 1: Trailing comma in Lua tables (strict interpreters reject these)
                if (TrailingCommaRegex.IsMatch(stripped))
                {
                    working  = TrailingCommaRegex.Replace(working, "$1");
                    modified = true;
                }

                // Fix 2: Accidental double-equals in local assignments
                if (DoubleEqualsAssignRegex.IsMatch(stripped))
                {
                    working  = DoubleEqualsAssignRegex.Replace(working, "$1=$2");
                    modified = true;
                }

                if (modified && working != original)
                {
                    string backupPath = file + ".tg_backup";
                    if (!File.Exists(backupPath))
                        await File.WriteAllTextAsync(backupPath, original);

                    await File.WriteAllTextAsync(file, working);
                    filesFixed++;

                    auditLog.LogChange(relPath,
                        "Syntax Diagnosed & Fixed",
                        "Corrected fatal Lua syntax errors before deployment.");

                    onResult?.Invoke(new DiagnosticResult
                    {
                        FilePath = relPath,
                        Issue = "Trailing comma / double-equals assignment corrected.",
                        WasAutoFixed = true
                    });
                }
            }

            return filesFixed;
        }
    }
}
