using Vintagestory.API.Common;
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
