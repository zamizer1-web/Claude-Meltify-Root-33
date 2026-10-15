using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Root33.Core.Sheets;

namespace Root33.Core.Engine
{
    /// <summary>
    /// Character id → factory for its <see cref="ICharacterModule"/>. <see cref="Default"/> finds every non-abstract
    /// <see cref="ICharacterModule"/> in Root33.Core that has a public parameterless constructor, so a character's module
    /// registers itself just by existing (src/Root33.Core/Characters/&lt;Name&gt;/&lt;Name&gt;Module.cs).
    /// </summary>
    public sealed class ModuleRegistry
    {
        private readonly Dictionary<string, Func<ICharacterModule>> _factories = new Dictionary<string, Func<ICharacterModule>>(StringComparer.Ordinal);

        public ModuleRegistry Register(string characterId, Func<ICharacterModule> factory)
        {
            if (!Data.Characters.ContainsKey(characterId)) throw new ArgumentException("no characters row " + characterId);
            _factories[characterId] = factory;
            return this;
        }

        public bool Has(string characterId) => _factories.ContainsKey(characterId);

        public IReadOnlyCollection<string> CharacterIds => _factories.Keys;

        /// <summary>A fresh module for a character, or null when none is registered (the seat then plays plain Root rules
        /// with no themed win or bot plan).</summary>
        public ICharacterModule Create(string characterId)
        {
            if (characterId == null || !_factories.TryGetValue(characterId, out var f)) return null;
            var m = f();
            if (m.CharacterId != characterId) throw new InvalidOperationException("module for " + characterId + " reports " + m.CharacterId);
            return m;
        }

        /// <summary>Every module class in Root33.Core.</summary>
        public static ModuleRegistry Default()
        {
            var reg = new ModuleRegistry();
            foreach (var t in typeof(ModuleRegistry).Assembly.GetTypes()
                         .Where(t => typeof(ICharacterModule).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface
                                     && t.GetConstructor(Type.EmptyTypes) != null)
                         .OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                var type = t;
                var probe = (ICharacterModule)Activator.CreateInstance(type);
                reg.Register(probe.CharacterId, () => (ICharacterModule)Activator.CreateInstance(type));
            }
            return reg;
        }

        /// <summary>The character a Root engine carries (characters.json engine column; one character per engine).</summary>
        public static string CharacterForEngine(EngineId engine)
        {
            foreach (var c in Data.Characters.Values.OrderBy(c => c.Id, StringComparer.Ordinal))
                if (EngineIdOf(c.Engine) == engine) return c.Id;
            return null;
        }

        /// <summary>engines.json id ("vagabond-tinker") → <see cref="EngineId"/> (VagabondTinker).</summary>
        public static EngineId? EngineIdOf(string engineRowId)
        {
            if (string.IsNullOrEmpty(engineRowId)) return null;
            var name = string.Concat(engineRowId.Split('-').Where(p => p.Length > 0).Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1)));
            return Enum.TryParse<EngineId>(name, false, out var e) ? e : (EngineId?)null;
        }

        /// <summary>Rule ids of a bot plan that the director has no [BotRule] method for (empty when the plan is fully covered).</summary>
        public static IReadOnlyList<string> MissingBotRules(IBotDirector director)
        {
            var have = new HashSet<string>(director.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Select(m => m.GetCustomAttribute<BotRuleAttribute>()).Where(a => a != null).Select(a => a.RuleId), StringComparer.Ordinal);
            return Data.BotRules.Values.Where(r => r.Plan == director.PlanId).OrderBy(r => r.Order)
                .Select(r => r.Id).Where(id => !have.Contains(id)).ToList();
        }
    }
}
