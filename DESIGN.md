# Axle Chisel — design notes (from 1.22 discovery)

Captures what the v0.1.1 discovery dump + the VS 1.22 source (anegostudios/
vssurvivalmod) tell us, and the implementation plan that follows. This is the
basis for the real integration work; read alongside HANDOFF.md.

## 1. Class map (verified)

### Mechanical power / axle
- `BlockAxle : BlockMPBase : BlockGeneric : Block`  (assembly VSSurvivalMod)
  - `BlockMPBase : BlockGeneric, IMechanicalPowerBlock` — only ~75 lines.
    Provides `WasPlaced`, `tryConnect`, `GetNetwork`, `ExchangeBlockAt`;
    declares abstract `DidConnectAt`, `HasMechPowerConnectorAt`.
  - `BlockAxle` overrides `TryPlaceBlock`, `HasMechPowerConnectorAt`,
    `OnNeighbourBlockChange`, `DidConnectAt`, plus `IsOrientedTo`.
- `BEBehaviorMPAxle : BEBehaviorMPBase : BlockEntityBehavior`
  - Holds the rotation/orientation state: `orientations` (string), `orients`
    (BlockFacing[]), `center`, `axleshape`, stand asset locs, `AddStands`.
  - Static `IsAttachedToBlock(blockAccessor, block, pos)` — used for stand
    rendering, NOT an "axle embedded in arbitrary block" feature.
- The axle has **no dedicated BlockEntity class**; it uses the generic BE that
  hosts behaviors declared on the block, including `BEBehaviorMPAxle` +
  `BEBehaviorMPBase`.

**Key fact:** mechanical power flows block→block through the
`IMechanicalPowerBlock` interface. A position only conducts if its *block*
implements that interface. The BE behavior carries rotation/network state but
the block is what the network walker sees.

### Chiseled / microblock
- `BlockMicroBlock : Block, IContainedMeshSource, IDisplayableProps`
- `BlockChisel : BlockMicroBlock, IWrenchOrientable, ...`
- `BlockEntityMicroBlock : BlockEntity, IRotatable, IAcceptsDecor, IMaterialExchangeable`
  - Voxel state: `VoxelCuboids` (List<uint>), `BlockIds`/`MaterialIds`, `Mesh`,
    selection boxes, `rotationY`.
  - `WasPlaced(Block block, string blockName)` seeds the microblock with the
    source block as material.
  - `ConvertToVoxels`, `SetData`, `FromTreeAttributes`/`ToTreeAttributes`,
    `OnTesselation`, `RegenMesh`, static `CreateMesh(...)`.
- `BlockEntityChisel : BlockEntityMicroBlock`
  - `Interact`, `SetVoxel`, `AddMaterial`, `WasPlaced` override, packet handling.
- Third-party present in user's install: `chisel.src.BELockedMicroblock :
  BlockEntityChisel` (chiseltools mod). Hooking the vanilla base covers it.

## 2. Core constraint

One block + one block-entity per voxel position. Axle wants an
`IMechanicalPowerBlock` block; chiseled wants a `BlockMicroBlock`. Mutually
exclusive in vanilla → chiseling replaces the axle block (and its BE), severing
it from the network. This is the whole problem.

## 3. Chosen approach: a combined block + BE

Register a new pair:

- `BlockChiseledAxle : BlockMicroBlock, IMechanicalPowerBlock`
  - Re-implements the BlockMPBase surface (copy its ~75 lines) so the network
    walker treats the position as a conductor.
  - `HasMechPowerConnectorAt` answers from the stored axle orientation.
  - Mesh/selection from the microblock voxels; axle shaft mesh contributed by
    the attached axle behavior's tesselation.
- `BlockEntityChiseledAxle : BlockEntityMicroBlock`
  - Hosts a `BEBehaviorMPAxle` (+ `BEBehaviorMPBase`) so rotation + network
    membership persist exactly like a real axle.
  - Serializes axle orientation alongside the voxel data.

### Story 1 — chisel around an axle
1. Make `BlockAxle` chiselable (add the chiselable behavior / allow the chisel
   to act on it).
2. Hook the chisel→microblock conversion: when the source block is an axle,
   create `BlockChiseledAxle` instead of `BlockChisel`, copying the axle's
   orientation and re-joining the network in `WasPlaced`.

### Story 2 — place a chiseled block onto an axle
1. Hook microblock placement: if the target position already holds an axle
   (block is `BlockAxle` or BE has `BEBehaviorMPAxle`), place
   `BlockChiseledAxle`, transfer the axle state, keep the network intact.

## 4. Open risks / to verify in-game
- Exact conversion entry point: confirm whether `ItemChisel` (in /Item) calls a
  static on `BlockEntityMicroBlock`/`BlockChisel`, or replaces the block then
  calls `WasPlaced`. That's the precise Harmony seam for Story 1.
- Whether attaching `BEBehaviorMPAxle` to a non-axle BE initializes correctly
  (it may read orientation from the block code/variant).
- Mesh merge: confirm the axle behavior's `OnTesselation` adds the shaft when
  hosted on our BE, or whether we composite manually.
- Network rejoin timing on chunk load (BlockMPBase.WasPlaced / DidConnectAt).

## 5. Next build increment
Pick ONE story (Story 1 is the more natural demo) and ship a vertical slice:
register the combined block+BE, hook the one conversion path, prove on a
screenshot that a chiseled position still spins and stays networked. Bundle a
deeper method-discovery dump for `ItemChisel` + the conversion seam in the same
build to avoid an extra round-trip.
