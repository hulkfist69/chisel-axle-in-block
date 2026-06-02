using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;

namespace AxleChisel
{
    // Runtime reflection patches and one-time structure dumps to discover how VS 1.22
    // organizes mechanical axles and chiseled (microblock) blocks.
    public static class RuntimePatches
    {
        public static void Apply(Harmony harmony, ILogger logger)
        {
            DumpDiscovery(logger);
        }

        // One-shot dump at mod start: prints the class names, inheritance chains, and fields
        // of the relevant VS types so we can plan the actual integration. We need:
        //   - the mechanical axle block + BE class
        //   - the microblock (chiseled) BE + block class
        //   - whether axles store data on a BE that we can preserve when the parent block changes
        //
        // Two passes, because (lesson from Move Wrench) VS renames these classes across versions:
        //   1. Probe the exact fully-qualified names we expect, dump fields when found.
        //   2. Broad scan EVERY loaded assembly for any type whose simple name hints at axles
        //      or microblocks, so a rename in 1.22 still surfaces the real class name.
        private static void DumpDiscovery(ILogger logger)
        {
            logger.Notification("[axlechisel] ===== discovery dump begin =====");

            // ---- Pass 1: exact candidates -------------------------------------------------
            // VS keeps these in Vintagestory.GameContent(.Mechanics). Names are best guesses;
            // if a name is wrong here, Pass 2 still finds the class by substring.
            string[] candidates = new[]
            {
                "Vintagestory.GameContent.Mechanics.BlockAxle",
                "Vintagestory.GameContent.Mechanics.BlockEntityAxle",
                "Vintagestory.GameContent.Mechanics.BEBehaviorMPAxle",
                "Vintagestory.GameContent.Mechanics.BlockMechBase",
                "Vintagestory.GameContent.BlockMicroBlock",
                "Vintagestory.GameContent.BlockEntityMicroBlock"
            };

            logger.Notification("[axlechisel] -- pass 1: exact-name probes --");
            foreach (var name in candidates)
            {
                var t = ResolveType(name);
                if (t == null)
                {
                    logger.Notification("[axlechisel] type NOT FOUND: " + name);
                    continue;
                }
                DumpType(logger, t, dumpMembers: true);
            }

            // ---- Pass 2: broad substring scan ---------------------------------------------
            // Catches the real class names even if Pass 1's guesses are stale. We only dump the
            // name + inheritance chain here (not every field) to keep the log readable; once we
            // know the true names we can promote them into Pass 1 for full member dumps.
            logger.Notification("[axlechisel] -- pass 2: substring scan (Axle / MicroBlock) --");
            string[] needles = { "axle", "microblock" };
            var seen = new HashSet<string>();

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    // Some assemblies fail to fully load their types; take what we can.
                    types = ex.Types.Where(t => t != null).ToArray();
                }
                catch
                {
                    continue;
                }

                foreach (var t in types)
                {
                    if (t == null || t.Name == null) continue;
                    var lower = t.Name.ToLowerInvariant();
                    if (!needles.Any(n => lower.Contains(n))) continue;
                    if (!seen.Add(t.FullName ?? t.Name)) continue;
                    DumpType(logger, t, dumpMembers: false);
                }
            }

            logger.Notification("[axlechisel] ===== discovery dump end =====");
        }

        // Logs a type's full name, its inheritance chain (so we can tell at a glance whether a
        // class is a Block, BlockEntity, BlockBehavior or BEBehavior — the A-vs-B fork in the
        // handoff), and optionally its declared fields/properties.
        private static void DumpType(ILogger logger, Type t, bool dumpMembers)
        {
            logger.Notification("[axlechisel] type found: " + (t.FullName ?? t.Name) + "  [in " + t.Assembly.GetName().Name + "]");
            logger.Notification("[axlechisel]   extends: " + BaseChain(t));

            if (!dumpMembers) return;

            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                logger.Notification("[axlechisel]   field " + f.Name + " : " + f.FieldType.Name);
            }
            foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                logger.Notification("[axlechisel]   prop  " + p.Name + " : " + p.PropertyType.Name);
            }
        }

        // Builds a readable "Child -> Parent -> ... -> Object" chain so the inheritance model
        // is obvious from the log without another round-trip.
        private static string BaseChain(Type t)
        {
            var parts = new List<string>();
            var cur = t.BaseType;
            int guard = 0;
            while (cur != null && guard++ < 12)
            {
                parts.Add(cur.Name);
                if (cur == typeof(object)) break;
                cur = cur.BaseType;
            }
            return parts.Count > 0 ? string.Join(" -> ", parts) : "(none)";
        }

        private static Type ResolveType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t;
                try { t = asm.GetType(fullName); }
                catch { continue; }
                if (t != null) return t;
            }
            return null;
        }
    }
}
