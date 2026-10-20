using System;

namespace Root33.Plugin.Patches
{
    /// <summary>
    /// Marks a Harmony patch class with the sheets/game_hooks.json row it raises: <c>[RootHook("hook.evening-end")]</c>.
    /// tools/gen_plugin_stubs.py reads it to mark the row patched in HookMap.cs, and <see cref="Root33Runtime"/> counts the
    /// patched rows for the panel. Use the row id as a string literal; the generator reads it from the source.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class RootHookAttribute : Attribute
    {
        public RootHookAttribute(string rowId) { RowId = rowId; }
        public string RowId { get; }
    }
}
