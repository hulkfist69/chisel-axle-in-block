using Vintagestory.GameContent;

namespace AxleChisel
{
    // The block entity for a chiseled axle. Inherits all chisel voxel behavior from
    // BlockEntityChisel; the MPAxle behavior is attached via the blocktype JSON
    // (entityBehaviors), and BlockEntityMicroBlock's Initialize/From-/ToTreeAttributes/
    // OnTesselation all call base, so that behavior initializes, persists and renders.
    public class BlockEntityChiseledAxle : BlockEntityChisel
    {
    }
}
