using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace AxleChisel
{
    // Runtime reflection patches + one-time structure dumps. We resolve VS internal
    // types by name (they vary across versions) and patch by reflection so a missing
    // type logs and continues instead of crashing at startup.
    public static class RuntimePatches
    {
        // Resolved once at startup, used by the chisel prefix.
        private static Type AxleBlockType;       // Vintagestory.GameContent.Mechanics.BlockAxle
        private static Type AxleBehaviorType;     // Vintagestory.GameContent.Mechanics.BEBehaviorMPAxle

        public static void Apply(Harmony harmony, ILogger logger)
        {
            DumpDiscovery(logger);
            TryPatchChisel(harmony, logger);
        }

        // --- v0.2.0: non-destructive axle-chisel interception --------------------------
        // ItemChisel.OnHeldInteractStart (Item/ItemChisel.cs) is what converts a solid
        // block into a "chiseledblock": it SetBlock()s the chiseled block over the axle
        // (destroying it) then calls be.WasPlaced(originalBlock). We prefix that method:
        // when the targeted block is an axle, we log everything we need to build the
        // combined block and PREVENT the vanilla conversion so the axle is preserved.
        private static void TryPatchChisel(Harmony harmony, ILogger logger)
        {
            try
            {
                AxleBlockType = ResolveType("Vintagestory.GameContent.Mechanics.BlockAxle");
                AxleBehaviorType = ResolveType("Vintagestory.GameContent.Mechanics.BEBehaviorMPAxle");

                var itemChiselType = ResolveType("Vintagestory.GameContent.ItemChisel");
                if (itemChiselType == null)
                {
                    logger.Warning("[axlechisel] ItemChisel type not found; chisel patch skipped");
                    return;
                }

                // OnHeldInteractStart(ItemSlot, EntityAgent, BlockSelection, EntitySelection, bool, ref EnumHandHandling)
                var target = itemChiselType
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(m => m.Name == "OnHeldInteractStart" && m.GetParameters().Length == 6);

                if (target == null)
                {
                    logger.Warning("[axlechisel] ItemChisel.OnHeldInteractStart(6 args) not found; chisel patch skipped");
                    return;
                }

                var prefix = typeof(RuntimePatches).GetMethod(nameof(ChiselInteractPrefix),
                    BindingFlags.Static | BindingFlags.NonPublic);

                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                logger.Notification("[axlechisel] patched ItemChisel.OnHeldInteractStart (axle interception active)");
            }
            catch (Exception ex)
            {
                logger.Error("[axlechisel] TryPatchChisel FAILED: " + ex);
            }
        }

        // Harmony injects byEntity / blockSel / handling by parameter name. Returning
        // false skips the original method (the vanilla destructive conversion).
        private static bool ChiselInteractPrefix(EntityAgent byEntity, BlockSelection blockSel, ref EnumHandHandling handling)
        {
            try
            {
                if (byEntity == null || blockSel == null) return true;
                var world = byEntity.World;
                if (world == null) return true;

                var block = world.BlockAccessor.GetBlock(blockSel.Position);
                if (AxleBlockType == null || block == null || !AxleBlockType.IsInstanceOfType(block))
                {
                    return true; // not an axle -> let vanilla chiseling proceed
                }

                LogAxleTarget(world, blockSel.Position, block);

                // For now: preserve the axle instead of letting it be destroyed. The
                // combined chiseled-axle block lands in the next build.
                if (world.Side == EnumAppSide.Client)
                {
                    (world.Api as ICoreClientAPI)?.ShowChatMessage(
                        "[axlechisel] axle preserved - combined chiseled-axle block is WIP");
                }

                handling = EnumHandHandling.PreventDefaultAction;
                return false;
            }
            catch (Exception ex)
            {
                (byEntity?.World?.Logger)?.Error("[axlechisel] ChiselInteractPrefix error: " + ex);
                return true; // fail open: never break vanilla chiseling on our account
            }
        }

        // Dump exactly what we need to construct the combined block next iteration:
        // the axle's variant/orientation and its BEBehaviorMPAxle state.
        private static void LogAxleTarget(IWorldAccessor world, BlockPos pos, Block block)
        {
            var log = world.Logger;
            log.Notification("[axlechisel] === axle chisel intercepted at " + pos + " ===");
            log.Notification("[axlechisel]   block.Code = " + block.Code);
            if (block.Variant != null)
            {
                foreach (var kv in block.Variant)
                    log.Notification("[axlechisel]   variant " + kv.Key + " = " + kv.Value);
            }
            log.Notification("[axlechisel]   block.Shape = " + (block.Shape?.Base?.ToString() ?? "null"));

            var be = world.BlockAccessor.GetBlockEntity(pos);
            if (be == null) { log.Notification("[axlechisel]   no block entity at pos"); return; }
            log.Notification("[axlechisel]   BE type = " + be.GetType().FullName);

            // Find the axle behavior on the BE and dump its declared fields' values.
            var beh = FindBehavior(be, AxleBehaviorType);
            if (beh == null) { log.Notification("[axlechisel]   no BEBehaviorMPAxle on BE"); return; }
            log.Notification("[axlechisel]   axle behavior = " + beh.GetType().FullName);
            foreach (var f in beh.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                object val;
                try { val = f.GetValue(beh); } catch { val = "<err>"; }
                if (val is Array arr) val = "[" + string.Join(",", arr.Cast<object>()) + "]";
                log.Notification("[axlechisel]     " + f.Name + " = " + val);
            }
        }

        // Returns the BlockEntityBehavior on 'be' assignable to behaviorType, via the
        // public Behaviors list (no compile-time dependency on the behavior type).
        private static object FindBehavior(object be, Type behaviorType)
        {
            if (behaviorType == null) return null;
            // BlockEntity.Behaviors is a public field (List<BlockEntityBehavior>).
            object listObj = null;
            var field = be.GetType().GetField("Behaviors", BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy);
            if (field != null) listObj = field.GetValue(be);
            if (listObj == null)
            {
                var prop = be.GetType().GetProperty("Behaviors", BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy);
                listObj = prop?.GetValue(be);
            }
            if (listObj is System.Collections.IEnumerable list)
            {
                foreach (var b in list)
                    if (b != null && behaviorType.IsInstanceOfType(b)) return b;
            }
            return null;
        }

        // --- discovery dump (unchanged from v0.1.1) ------------------------------------
        private static void DumpDiscovery(ILogger logger)
        {
            logger.Notification("[axlechisel] ===== discovery dump begin =====");

            string[] candidates = new[]
            {
                "Vintagestory.GameContent.Mechanics.BlockAxle",
                "Vintagestory.GameContent.Mechanics.BEBehaviorMPAxle",
                "Vintagestory.GameContent.BlockMicroBlock",
                "Vintagestory.GameContent.BlockEntityMicroBlock",
                "Vintagestory.GameContent.ItemChisel"
            };

            logger.Notification("[axlechisel] -- pass 1: exact-name probes --");
            foreach (var name in candidates)
            {
                var t = ResolveType(name);
                if (t == null) { logger.Notification("[axlechisel] type NOT FOUND: " + name); continue; }
                DumpType(logger, t, dumpMembers: false);
            }

            logger.Notification("[axlechisel] ===== discovery dump end =====");
        }

        private static void DumpType(ILogger logger, Type t, bool dumpMembers)
        {
            logger.Notification("[axlechisel] type found: " + (t.FullName ?? t.Name) + "  [in " + t.Assembly.GetName().Name + "]");
            logger.Notification("[axlechisel]   extends: " + BaseChain(t));
            if (!dumpMembers) return;
            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                logger.Notification("[axlechisel]   field " + f.Name + " : " + f.FieldType.Name);
        }

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
