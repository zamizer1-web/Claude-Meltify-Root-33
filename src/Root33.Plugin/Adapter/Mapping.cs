using System;

namespace Root33.Plugin.Adapter
{
    /// <summary>
    /// Marks a RootBoard member as mapped onto Root's own code: <c>[Mapped("read.seats")]</c> names its sheet row. Write mapped
    /// members in RootBoard.Mapped.cs (or another hand-written file in Adapter/) and rerun
    /// <c>python3 -I tools/gen_plugin_stubs.py</c>: the generated stub for that row leaves RootBoard.cs. Use the row id as a
    /// string literal; the generator reads it from the source.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = false)]
    public sealed class MappedAttribute : Attribute
    {
        public MappedAttribute(string rowId) { RowId = rowId; }
        public string RowId { get; }
    }

    /// <summary>
    /// A board read or action the plugin has not mapped onto Root yet (RootBoard.cs). The message names the sheet row and the
    /// sheet's current guess at Root's symbol, so the panel and the log say exactly what to map next.
    /// </summary>
    public sealed class NotMappedException : NotSupportedException
    {
        public NotMappedException(string rowId, string engineSymbol)
            : base($"{rowId} is not mapped onto Root yet (engineSymbol guess: {engineSymbol}); map it in Adapter/RootBoard.Mapped.cs")
        {
            RowId = rowId;
            EngineSymbol = engineSymbol;
        }

        public string RowId { get; }
        public string EngineSymbol { get; }
    }
}
