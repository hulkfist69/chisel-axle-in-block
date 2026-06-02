using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace AxleChisel
{
    // Path B: keep the axle block (so mechanical power keeps working for free) and
    // give its "cover" (BlockEntityBehaviorCoverable.WallStack) chiseled voxel geometry.
    // We store voxel cuboids on the WallStack's attributes, render them in place of the
    // full-block cover mesh, and (Layer 1) apply a fixed test carve when chiseling an
    // encased axle to prove the rendering + power preservation end to end.
    public static class RuntimePatches
    {
        public static ICoreClientAPI ClientApi; // set in StartClientSide, used during tesselation

        private const string VoxelKey = "axlechiselVoxels"; // byte[] of uint cuboids on WallStack.Attributes

        private static Type AxleBehaviorType; // Vintagestory.GameContent.Mechanics.BEBehaviorMPAxle

        public static void Apply(Harmony harmony, ILogger logger)
        {
            DumpDiscovery(logger);
            AxleBehaviorType = ResolveType("Vintagestory.GameContent.Mechanics.BEBehaviorMPAxle");
            TryPatchChisel(harmony, logger);
            TryPatchCoverRender(harmony, logger);
        }

        // ---------------------------------------------------------------------------
        // Patch wiring
        // ---------------------------------------------------------------------------
        private static void TryPatchChisel(Harmony harmony, ILogger logger)
        {
            try
            {
                var itemChiselType = ResolveType("Vintagestory.GameContent.ItemChisel");
                var target = itemChiselType?
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(m => m.Name == "OnHeldInteractStart" && m.GetParameters().Length == 6);
                if (target == null) { logger.Warning("[axlechisel] ItemChisel.OnHeldInteractStart not found; chisel patch skipped"); return; }

                harmony.Patch(target, prefix: new HarmonyMethod(
                    typeof(RuntimePatches).GetMethod(nameof(ChiselInteractPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
                logger.Notification("[axlechisel] patched ItemChisel.OnHeldInteractStart");
            }
            catch (Exception ex) { logger.Error("[axlechisel] TryPatchChisel FAILED: " + ex); }
        }

        private static void TryPatchCoverRender(Harmony harmony, ILogger logger)
        {
            try
            {
                var m = typeof(BlockEntityBehaviorCoverable).GetMethod("OnTesselation",
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (m == null) { logger.Warning("[axlechisel] Coverable.OnTesselation not found; render patch skipped"); return; }

                harmony.Patch(m, prefix: new HarmonyMethod(
                    typeof(RuntimePatches).GetMethod(nameof(CoverTesselationPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
                logger.Notification("[axlechisel] patched BlockEntityBehaviorCoverable.OnTesselation");
            }
            catch (Exception ex) { logger.Error("[axlechisel] TryPatchCoverRender FAILED: " + ex); }
        }

        // ---------------------------------------------------------------------------
        // Chisel an encased axle -> carve the cover (Layer 1: fixed test carve)
        // ---------------------------------------------------------------------------
        private static bool ChiselInteractPrefix(EntityAgent byEntity, BlockSelection blockSel, ref EnumHandHandling handling)
        {
            try
            {
                if (byEntity?.World == null || blockSel == null) return true;
                var world = byEntity.World;
                var be = world.BlockAccessor.GetBlockEntity(blockSel.Position);
                if (be == null || FindBehavior(be, AxleBehaviorType) == null) return true; // not an axle -> normal chiseling

                // Require a hammer in the offhand, exactly like vanilla chiseling (creative
                // bypasses). If it's missing, don't intercept - let vanilla ItemChisel run and
                // show its own "Requires a hammer in the off hand" error.
                var byPlayer = (byEntity as EntityPlayer)?.Player;
                if (byPlayer == null) return true;
                bool creative = byPlayer.WorldData.CurrentGameMode == EnumGameMode.Creative;
                if (byPlayer.InventoryManager.OffhandTool != EnumTool.Hammer && !creative) return true;

                var cover = FindBehavior(be, typeof(BlockEntityBehaviorCoverable)) as BlockEntityBehaviorCoverable;
                if (cover?.WallStack?.Block == null)
                {
                    Msg(world, "[axlechisel] encase this axle in a block first (wrench in offhand), then chisel the cover");
                    handling = EnumHandHandling.PreventDefaultAction;
                    return false;
                }

                // Server mutates; MarkDirty syncs the updated WallStack (with voxels) to clients.
                if (world.Side == EnumAppSide.Server)
                {
                    var cuboids = new List<uint> { BlockEntityMicroBlock.ToUint(0, 0, 0, 16, 8, 16, 0) }; // bottom half
                    SetVoxels(cover.WallStack, cuboids);
                    be.MarkDirty(true);
                    world.Logger.Notification("[axlechisel] carved cover (test) at " + blockSel.Position + " material=" + cover.WallStack.Block.Code);
                }
                Msg(world, "[axlechisel] chiseled the cover (test carve) - axle preserved");
                handling = EnumHandHandling.PreventDefaultAction;
                return false;
            }
            catch (Exception ex)
            {
                (byEntity?.World?.Logger)?.Error("[axlechisel] ChiselInteractPrefix error: " + ex);
                return true;
            }
        }

        // ---------------------------------------------------------------------------
        // Render the chiseled cover instead of the full-block cover mesh
        // ---------------------------------------------------------------------------
        private static bool CoverTesselationPrefix(object __instance, ITerrainMeshPool mesher, ref bool __result)
        {
            try
            {
                var cover = __instance as BlockEntityBehaviorCoverable;
                var ws = cover?.WallStack;
                if (ws?.Block == null) return true;            // no cover -> vanilla
                var cuboids = GetVoxels(ws);
                if (cuboids == null || cuboids.Count == 0) return true; // not chiseled -> vanilla full cover
                var capi = ClientApi;
                if (capi == null) return true;

                var mesh = BlockEntityMicroBlock.CreateMesh(capi, cuboids, new int[] { ws.Block.BlockId }, null);
                if (mesh != null) mesher.AddMeshData(mesh);

                __result = false; // keep default block tesselation (the axle shaft) running
                return false;     // skip vanilla's full-block cover mesh
            }
            catch
            {
                return true; // fall back to vanilla cover render
            }
        }

        // ---------------------------------------------------------------------------
        // Voxel storage on the cover's WallStack attributes (byte[] of uint cuboids)
        // ---------------------------------------------------------------------------
        private static void SetVoxels(ItemStack ws, List<uint> cuboids)
        {
            if (ws?.Attributes == null) return;
            var bytes = new byte[cuboids.Count * 4];
            for (int i = 0; i < cuboids.Count; i++) BitConverter.GetBytes(cuboids[i]).CopyTo(bytes, i * 4);
            ws.Attributes.SetBytes(VoxelKey, bytes);
        }

        private static List<uint> GetVoxels(ItemStack ws)
        {
            var bytes = ws?.Attributes?.GetBytes(VoxelKey, null);
            if (bytes == null || bytes.Length < 4) return null;
            var list = new List<uint>(bytes.Length / 4);
            for (int i = 0; i + 4 <= bytes.Length; i += 4) list.Add(BitConverter.ToUInt32(bytes, i));
            return list;
        }

        // ---------------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------------
        private static void Msg(IWorldAccessor world, string text)
        {
            if (world.Side == EnumAppSide.Client) (world.Api as ICoreClientAPI)?.ShowChatMessage(text);
        }

        private static object FindBehavior(object be, Type behaviorType)
        {
            if (behaviorType == null || be == null) return null;
            var field = be.GetType().GetField("Behaviors", BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy);
            if (field?.GetValue(be) is System.Collections.IEnumerable list)
                foreach (var b in list)
                    if (b != null && behaviorType.IsInstanceOfType(b)) return b;
            return null;
        }

        // ---------------------------------------------------------------------------
        // Discovery dump (retained, abbreviated)
        // ---------------------------------------------------------------------------
        private static void DumpDiscovery(ILogger logger)
        {
            logger.Notification("[axlechisel] ===== discovery dump begin =====");
            string[] candidates =
            {
                "Vintagestory.GameContent.Mechanics.BlockAxle",
                "Vintagestory.GameContent.Mechanics.BEBehaviorMPAxle",
                "Vintagestory.GameContent.BlockEntityBehaviorCoverable",
                "Vintagestory.GameContent.BlockEntityMicroBlock",
                "Vintagestory.GameContent.ItemChisel"
            };
            foreach (var name in candidates)
            {
                var t = ResolveType(name);
                logger.Notification(t == null ? "[axlechisel] type NOT FOUND: " + name
                                              : "[axlechisel] type found: " + t.FullName + "  [in " + t.Assembly.GetName().Name + "]");
            }
            logger.Notification("[axlechisel] ===== discovery dump end =====");
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
