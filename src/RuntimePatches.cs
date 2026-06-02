using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace AxleChisel
{
    // Path A: chiseling an encased axle converts it to our combined block, BlockChiseledAxle
    // (a real BlockChisel that also implements IMechanicalPowerBlock). From then on it chisels
    // exactly like vanilla AND conducts power, because it IS a chisel block and an axle.
    public static class RuntimePatches
    {
        private static Type AxleBlockType; // Vintagestory.GameContent.Mechanics.BlockAxle

        public static void Apply(Harmony harmony, ILogger logger)
        {
            DumpDiscovery(logger);
            AxleBlockType = ResolveType("Vintagestory.GameContent.Mechanics.BlockAxle");
            TryPatchChisel(harmony, logger);
        }

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
                logger.Notification("[axlechisel] patched ItemChisel.OnHeldInteractStart (encased-axle conversion active)");
            }
            catch (Exception ex) { logger.Error("[axlechisel] TryPatchChisel FAILED: " + ex); }
        }

        // Intercept chiseling of a RAW encased axle (BlockAxle with a Coverable cover) and
        // convert it to our chiseledaxle block, seeded from the cover material. After this the
        // block is a BlockChisel (not a BlockAxle) so we no longer intercept it - vanilla
        // chiseling handles all further edits while our block keeps power flowing.
        private static bool ChiselInteractPrefix(EntityAgent byEntity, BlockSelection blockSel, ref EnumHandHandling handling)
        {
            try
            {
                if (byEntity?.World == null || blockSel == null) return true;
                var world = byEntity.World;
                var block = world.BlockAccessor.GetBlock(blockSel.Position);
                if (AxleBlockType == null || block == null || !AxleBlockType.IsInstanceOfType(block)) return true; // not a raw axle

                // Require a hammer in the offhand, like vanilla (creative bypasses). If missing,
                // don't intercept -> vanilla shows its own "Requires a hammer in the off hand".
                var byPlayer = (byEntity as EntityPlayer)?.Player;
                if (byPlayer == null) return true;
                bool creative = byPlayer.WorldData.CurrentGameMode == EnumGameMode.Creative;
                if (byPlayer.InventoryManager.OffhandTool != EnumTool.Hammer && !creative) return true;

                var be = world.BlockAccessor.GetBlockEntity(blockSel.Position);
                var cover = FindBehavior(be, typeof(BlockEntityBehaviorCoverable)) as BlockEntityBehaviorCoverable;
                if (cover?.WallStack?.Block == null)
                {
                    Msg(world, "[axlechisel] encase this axle in a block first (wrench in offhand), then chisel the cover");
                    handling = EnumHandHandling.PreventDefaultAction;
                    return false;
                }

                if (world.Side == EnumAppSide.Server)
                {
                    var coverBlock = cover.WallStack.Block;
                    string rot = block.Variant?["rotation"] ?? "ud";
                    var newBlock = world.GetBlock(new AssetLocation("axlechisel", "chiseledaxle-" + rot));
                    if (newBlock == null)
                    {
                        world.Logger.Error("[axlechisel] chiseledaxle-" + rot + " not found; conversion aborted");
                        return false;
                    }
                    world.BlockAccessor.SetBlock(newBlock.BlockId, blockSel.Position);
                    var newBe = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BlockEntityChisel;
                    newBe?.WasPlaced(coverBlock, null);

                    // Join the mechanical network. The vanilla axle joins via BlockAxle.TryPlaceBlock,
                    // which our SetBlock bypasses, so trigger it manually on the oriented faces.
                    // (Chunk reloads rejoin automatically via the serialized NetworkId.)
                    var mp = newBe?.GetBehavior<BEBehaviorMPBase>();
                    if (mp != null)
                        foreach (var face in OrientedFaces(rot))
                            if (mp.tryConnect(face)) break;

                    newBe?.MarkDirty(true);
                    world.Logger.Notification("[axlechisel] converted encased axle -> chiseledaxle-" + rot + " (material " + coverBlock.Code + ") at " + blockSel.Position);
                }

                Msg(world, "[axlechisel] axle is now chiselable - chisel away, power keeps flowing");
                handling = EnumHandHandling.PreventDefaultAction;
                return false;
            }
            catch (Exception ex)
            {
                (byEntity?.World?.Logger)?.Error("[axlechisel] ChiselInteractPrefix error: " + ex);
                return true; // fail open
            }
        }

        private static BlockFacing[] OrientedFaces(string rot)
        {
            switch (rot)
            {
                case "ns": return new[] { BlockFacing.NORTH, BlockFacing.SOUTH };
                case "we": return new[] { BlockFacing.WEST, BlockFacing.EAST };
                default: return new[] { BlockFacing.DOWN, BlockFacing.UP }; // ud
            }
        }

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

        private static void DumpDiscovery(ILogger logger)
        {
            logger.Notification("[axlechisel] ===== discovery dump begin =====");
            string[] candidates =
            {
                "Vintagestory.GameContent.Mechanics.BlockAxle",
                "Vintagestory.GameContent.Mechanics.BEBehaviorMPAxle",
                "Vintagestory.GameContent.BlockEntityBehaviorCoverable",
                "Vintagestory.GameContent.BlockChisel",
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
