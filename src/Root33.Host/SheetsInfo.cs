using System.Collections;
using System.Linq;
using System.Reflection;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Host
{
    /// <summary>What a host reports at start (the plugin's "Root 33 loaded (core 1.0.0, sheets 1234 rows)" log line): the
    /// version of the Root33.Core it runs and how many sheet rows are compiled into it.</summary>
    public static class SheetsInfo
    {
        /// <summary>Sheets compiled into Root33.Core (one table per sheet in <see cref="Data"/>).</summary>
        public static int SheetCount => Tables().Count();

        /// <summary>Rows of every sheet compiled into Root33.Core.</summary>
        public static int RowCount => Tables().Sum(t => t.Count);

        /// <summary>Root33.Core's version: its informational version with a source revision shortened to 7 characters
        /// ("1.0.0+3f2a9c1"), else its assembly version.</summary>
        public static string CoreVersion
        {
            get
            {
                var asm = typeof(Root33Game).Assembly;
                var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (string.IsNullOrEmpty(info)) return asm.GetName().Version?.ToString() ?? "unknown";
                var plus = info!.IndexOf('+');
                return plus >= 0 && info.Length > plus + 8 ? info.Substring(0, plus + 8) : info;
            }
        }

        private static System.Collections.Generic.IEnumerable<ICollection> Tables() =>
            typeof(Data).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(f => f.GetValue(null)).OfType<ICollection>();
    }
}
