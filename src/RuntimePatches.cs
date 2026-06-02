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
        //
        // v0.3.0 inspector: when the chiseled target is an axle-bearing position (the BE
        // carries a BEBehaviorMPAxle - true for both a plain axle and an axle that's been
        // encased in a block via the wrench), dump the FULL block/BE/behavior/decor state
        // so we learn how 1.22 represents an encased axle, and preserve it (prevent the
        // vanilla destructive conversion). Non-axle blocks chisel normally.
        private static bool ChiselInteractPrefix(EntityAgent byEntity, BlockSelection blockSel, ref EnumHandHandling handling)
        {
            try
            {
                if (byEntity == null || blockSel == null) return true;
                var world = byEntity.World;
                if (world == null) return true;

                var be = world.BlockAccessor.GetBlockEntity(blockSel.Position);
                var axleBeh = be == null ? null : FindBehavior(be, AxleBehaviorType);
                if (axleBeh == null) return true; // no axle here -> normal chiseling

                DumpTarget(world, blockSel.Position, be, axleBeh);

                if (world.Side == EnumAppSide.Client)
                {
                    (world.Api as ICoreClientAPI)?.ShowChatMessage(
                        "[axlechisel] axle-bearing block inspected + preserved (see log)");
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

        // Full ground-truth dump of an axle-bearing position so we can see exactly what an
        // encased axle is in 1.22: the block, its variants, every BE behavior, the axle
        // behavior's fields, and any decors at the position.
        private static void DumpTarget(IWorldAccessor world, BlockPos pos, object be, object axleBeh)
        {
            var log = world.Logger;
            var block = world.BlockAccessor.GetBlock(pos);
            log.Notification("[axlechisel] === axle-bearing block at " + pos + " ===");
            log.Notification("[axlechisel]   block.Code = " + block?.Code);
            log.Notification("[axlechisel]   block.GetType = " + (block?.GetType().FullName ?? "null"));
            if (block?.Variant != null)
                foreach (var kv in block.Variant)
                    log.Notification("[axlechisel]   variant " + kv.Key + " = " + kv.Value);
            log.Notification("[axlechisel]   block.Shape = " + (block?.Shape?.Base?.ToString() ?? "null"));
            log.Notification("[axlechisel]   block.DrawType = " + block?.DrawType + "  material=" + block?.BlockMaterial);

            log.Notification("[axlechisel]   BE type = " + be.GetType().FullName);
            // All behaviors on the BE (an encasing may add/replace behaviors).
            var behField = be.GetType().GetField("Behaviors", BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy);
            if (behField?.GetValue(be) is System.Collections.IEnumerable behs)
                foreach (var b in behs)
                    if (b != null) log.Notification("[axlechisel]   behavior: " + b.GetType().FullName);

            // Axle behavior field values (orientation, network, and any encasing block ref).
            log.Notification("[axlechisel]   --- axle behavior fields ---");
            foreach (var f in axleBeh.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                object val;
                try { val = f.GetValue(axleBeh); } catch { val = "<err>"; }
                if (val is Array arr) val = "[" + string.Join(",", arr.Cast<object>()) + "]";
                log.Notification("[axlechisel]     " + f.Name + " = " + val);
            }

            // Decors at this position (encasing may be implemented as a decor).
            try
            {
                var ba = world.BlockAccessor;
                var getDecors = ba.GetType().GetMethod("GetDecors", new[] { typeof(BlockPos) });
                if (getDecors?.Invoke(ba, new object[] { pos }) is Array decors)
                {
                    log.Notification("[axlechisel]   decors[] length = " + decors.Length);
                    for (int i = 0; i < decors.Length; i++)
                        if (decors.GetValue(i) is Block d) log.Notification("[axlechisel]     decor[" + i + "] = " + d.Code);
                }
                else log.Notification("[axlechisel]   GetDecors -> none");
            }
            catch (Exception ex) { log.Notification("[axlechisel]   decor probe err: " + ex.Message); }
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
