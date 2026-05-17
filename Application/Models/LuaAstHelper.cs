using System;
using System.Text;
using System.Text.RegularExpressions;

namespace ToolKitV.Models
{
    /// <summary>
    /// Comment-aware Lua helper used by WebhookEngine, LocalizationEngine, and DiagnosticEngine.
    /// Strips both single-line (--) and multi-line (--[[ ... ]]) Lua comments so that Regex
    /// patterns never match dead code — the V1 equivalent of an AST comment filter.
    /// </summary>
    public static class LuaAstHelper
    {
        // Multi-line comments: --[[ ... ]] (with optional equals, e.g. --[==[ ... ]==])
        private static readonly Regex MultiLineCommentRegex = new(
            @"--\[=*\[[\s\S]*?\]=*\]",
            RegexOptions.Compiled);

        // Single-line comments: -- ...  (not followed by [, to avoid eating --[[)
        private static readonly Regex SingleLineCommentRegex = new(
            @"--(?!\[)[^\n]*",
            RegexOptions.Compiled);

        // Lua string literals we must NOT strip comments inside
        private static readonly Regex StringLiteralRegex = new(
            @"""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)*'|\[=*\[[\s\S]*?\]=*\]",
            RegexOptions.Compiled);

        /// <summary>
        /// Returns a version of the Lua source with all comment text replaced by
        /// equivalent whitespace, preserving line/column offsets for accurate matching.
        /// String literals are protected so their content is never misidentified as comments.
        /// </summary>
        public static string StripComments(string luaSource)
        {
            if (string.IsNullOrEmpty(luaSource)) return luaSource;

            // Phase 1 — protect string literals by replacing their content with safe placeholders
            var protectedStrings = new System.Collections.Generic.Dictionary<string, string>();
            string working = StringLiteralRegex.Replace(luaSource, m =>
            {
                string key = $"\x02STR{protectedStrings.Count}\x03";
                protectedStrings[key] = m.Value;
                return key;
            });

            // Phase 2 — strip multi-line comments (preserving newlines for line count accuracy)
            working = MultiLineCommentRegex.Replace(working, m =>
            {
                var sb = new StringBuilder(m.Length);
                foreach (char c in m.Value)
                    sb.Append(c == '\n' ? '\n' : ' ');
                return sb.ToString();
            });

            // Phase 3 — strip single-line comments
            working = SingleLineCommentRegex.Replace(working, m =>
                new string(' ', m.Length));

            // Phase 4 — restore string literals
            foreach (var kv in protectedStrings)
                working = working.Replace(kv.Key, kv.Value);

            return working;
        }

        /// <summary>
        /// Returns true if the given position in the source falls inside a comment region.
        /// Useful for precise position-based matching.
        /// </summary>
        public static bool IsPositionInComment(string luaSource, int position)
        {
            string stripped = StripComments(luaSource);
            if (position >= stripped.Length) return false;
            // A position is in a comment if its stripped equivalent is pure whitespace
            // where the original was not whitespace
            return stripped[position] == ' ' && luaSource[position] != ' ' && luaSource[position] != '\t';
        }
    }
}
