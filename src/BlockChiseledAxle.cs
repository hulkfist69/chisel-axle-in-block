using System;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace AxleChisel
{
    // A chiseled block that ALSO conducts mechanical power. It inherits the entire vanilla
    // chisel system from BlockChisel (multi-material, modes, sizes, drops) and implements
    // IMechanicalPowerBlock so the network treats it as a conductor - exactly like BlockAxle.
    // Orientation comes from the rotation variant (e.g. chiseledaxle-ns connects north/south),
    // mirroring BlockAxle.IsOrientedTo. The axle behavior (BEBehaviorMPAxle) lives on the BE.
    public class BlockChiseledAxle : BlockChisel, IMechanicalPowerBlock
    {
        // BlockMicroBlock.OnLoaded resolves its "non snow-covered" base via FirstCodePart(),
        // which for our rotation-variant code ("chiseledaxle-ns") is "chiseledaxle" - not a
        // registered block - so it returns null and NPEs at `IsSnowCovered = ... notSnowCovered.Id`.
        // We need the rotation variant for the MPAxle behavior, so instead we let the base run,
        // catch that NPE, and finish the bits it skipped: point notSnowCovered at ourselves
        // (IsSnowCovered stays false) and set the client api. An axle is never snow-covered.
        public override void OnLoaded(ICoreAPI api)
        {
            try
            {
                base.OnLoaded(api);
            }
            catch (NullReferenceException)
            {
                TrySet(typeof(BlockMicroBlock), "notSnowCovered", this);
                TrySet(typeof(BlockMicroBlock), "capi", api as ICoreClientAPI);
            }
        }

        private void TrySet(Type t, string name, object val)
        {
            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) { f.SetValue(this, val); return; }
            var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && p.CanWrite) p.SetValue(this, val);
        }

        // Breaking a chiseled axle returns both the carved block (as a normal chiseledblock,
        // preserving the pattern/materials) and the wooden axle that was inside it.
        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
        {
            var drops = new List<ItemStack>();

            var bec = GetBlockEntity<BlockEntityMicroBlock>(pos);
            var chiseledblock = world.GetBlock(new AssetLocation("chiseledblock"));
            if (bec != null && chiseledblock != null)
            {
                var tree = new TreeAttribute();
                bec.ToTreeAttributes(tree);
                tree.RemoveAttribute("posx");
                tree.RemoveAttribute("posy");
                tree.RemoveAttribute("posz");
                tree.RemoveAttribute("snowcuboids");
                drops.Add(new ItemStack(chiseledblock.Id, EnumItemClass.Block, 1, tree, world));
            }

            var axle = world.GetBlock(new AssetLocation("woodenaxle-ud"));
            if (axle != null) drops.Add(new ItemStack(axle));

            return drops.ToArray();
        }

        public bool IsOrientedTo(BlockFacing facing)
        {
            string dirs = LastCodePart();
            return dirs[0] == facing.Code[0] || (dirs.Length > 1 && dirs[1] == facing.Code[0]);
        }

        public bool HasMechPowerConnectorAt(IWorldAccessor world, BlockPos pos, BlockFacing face, BlockMPBase forBlock)
        {
            return IsOrientedTo(face);
        }

        public void DidConnectAt(IWorldAccessor world, BlockPos pos, BlockFacing face)
        {
        }

        public MechanicalNetwork GetNetwork(IWorldAccessor world, BlockPos pos)
        {
            IMechanicalPowerDevice be = world.BlockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorMPBase>() as IMechanicalPowerDevice;
            return be?.Network;
        }
    }
}
