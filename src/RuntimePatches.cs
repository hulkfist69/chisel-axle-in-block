using System;
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

        // One-shot dump at mod start: prints the class names and fields of the relevant VS
        // types so we can plan the actual integration. We need:
        //   - the mechanical axle block + BE class
        //   - the microblock (chiseled) BE + block class
        //   - whether axles store data on a BE that we can preserve when the parent block changes
        private static void DumpDiscovery(ILogger logger)
        {
            // Candidate type names — VS keeps these in Vintagestory.GameContent.Mechanics.
            string[] candidates = new[]
            {
                "Vintagestory.GameContent.Mechanics.BlockAxle",
                "Vintagestory.GameContent.Mechanics.BlockEntityAxle",
                "Vintagestory.GameContent.Mechanics.BEBehaviorMPAxle",
                "Vintagestory.GameContent.Mechanics.BlockMechBase",
                "Vintagestory.GameContent.BlockMicroBlock",
                "Vintagestory.GameContent.BlockEntityMicroBlock"
            };

            foreach (var name in candidates)
            {
                var t = ResolveType(name);
                if (t == null)
                {
                    logger.Notification("[axlechisel] type NOT FOUND: " + name);
                    continue;
                }
                logger.Notification("[axlechisel] type found: " + t.FullName);
                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    logger.Notification("[axlechisel]   field " + f.Name + " : " + f.FieldType.Name);
                }
                foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    logger.Notification("[axlechisel]   prop  " + p.Name + " : " + p.PropertyType.Name);
                }
            }
        }

        private static Type ResolveType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }
    }
}
