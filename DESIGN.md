# Axle Chisel — design notes (from 1.22 discovery)

Captures what the v0.1.1 discovery dump + the VS 1.22 source (anegostudios/
vssurvivalmod) tell us, and the implementation plan that follows. This is the
basis for the real integration work; read alongside HANDOFF.md.

## 0. CORRECTION (v0.3.0) — what the user actually wants

The real workflow is **encase, then chisel the encasing block**:
1. Encase an axle in a building block — VANILLA: wrench in offhand, block in
   main hand, right-click the axle. (Confirmed on the official Wrench wiki:
   "Applying blocks into Wooden axles". The old "Axle in blocks" mod is now
   redundant and is reported broken in 1.22.)
2. Then chisel that **encasing block**, with the axle preserved and networked.

So the chisel target is the *encased block*, NOT the bare axle. v0.2.0 hooked the
wrong thing (chiseling a plain axle). v0.3.0 repurposes the chisel hook as an
inspector: right-clicking an axle-bearing position (BE has BEBehaviorMPAxle) with
a chisel dumps the full block/BE/behavior/decor state so we learn how 1.22
represents an encased axle (still BlockAxle? building block + axle behavior? a
decor?). That representation drives the real implementation.

The encasing logic itself appears to live in closed-source VintagestoryLib (not
in ItemWrench.cs / BlockAxle.cs), which is why we capture it from the running
game instead of reading source.

## 0b. BREAKTHROUGH (v0.3.0 in-game capture) — how encasing actually works

Right-clicking an encased axle with a chisel dumped the truth. An encased axle:
- Block stays `game:woodenaxle-<rot>` (`BlockAxle`). **Encasing never changes the
  block or the axle** — it is purely additive.
- BE is `BlockEntityGeneric` with behaviors: `BEBehaviorBurning`,
  `BEBehaviorMPAxle`, **`BlockEntityBehaviorCoverable`**.
- The cover block is an `ItemStack WallStack` on `BlockEntityBehaviorCoverable`.
  - Render: `OnTesselation` → `mesher.AddMeshData(capi.TesselatorManager
    .GetDefaultBlockMesh(WallStack.Block))` (a full-block mesh).
  - `BlockBehaviorCoverable` derives solidity/collision/selection/light from
    `WallStack`. Encase interaction: sprint + wrench-in-offhand + suitable block
    in main hand → `TryAddMaterial` sets `WallStack`.
  - Serialized as itemstack `wallStack`.

**Power is block-driven.** `BEBehaviorMPBase` resolves connectivity via
`GetBlock(pos) as IMechanicalPowerBlock` and even `(Block as
IMechanicalPowerBlock).DidConnectAt(...)`. So the block at the position MUST
implement `IMechanicalPowerBlock` or power breaks. This is why encasing is clean
(block stays `BlockAxle`) and why a plain `chiseledblock` (not an
`IMechanicalPowerBlock`) would sever power.

`IMechanicalPowerBlock` (3 members):
```
MechanicalNetwork GetNetwork(IWorldAccessor world, BlockPos pos);
bool HasMechPowerConnectorAt(IWorldAccessor world, BlockPos pos, BlockFacing face, BlockMPBase forBlock);
void DidConnectAt(IWorldAccessor world, BlockPos pos, BlockFacing face);
```
BlockAxle impl: `HasMechPowerConnectorAt = IsOrientedTo(face)` where
`IsOrientedTo` checks the rotation code (`LastCodePart()`, e.g. "ns" connects
n/s); `DidConnectAt` is a no-op; `GetNetwork` reads the BE behavior's Network.

## 0c. Two viable build paths (chisel an encased axle, keep power)

**Path A — new combined block `BlockChiseledAxle : BlockChisel, IMechanicalPowerBlock`.**
- Inherits the full interactive chisel system (voxel editing, mesh, selection;
  the chisel tool edits anything `is BlockChisel`). Implement the 3 power members
  (mirroring BlockAxle, orientation from rotation variant). BE =
  `BlockEntityChiseledAxle : BlockEntityChisel` hosting the MPAxle behavior.
- Chiseling an encased axle converts it to this block, seeding voxels from
  `WallStack.Block` and carrying the axle orientation/network.
- Cost: an intricate new blocktype JSON merging chisel + axle behaviors; network
  rejoin on convert; render the shaft + voxels together. Reuses chisel UX.

**Path B — keep `BlockAxle`, give the cover voxel geometry (extend Coverable).**
- Block stays `BlockAxle` ⇒ **power is free** (no IMechanicalPowerBlock work, no
  network rejoin). Store chiseled voxel cuboids in a companion BE behavior;
  Harmony-patch `BlockEntityBehaviorCoverable.OnTesselation` to render the
  microblock mesh (reuse `BlockEntityMicroBlock.CreateMesh` static) instead of
  the full-block cover; derive selection/collision from voxels.
- Chiseling an encased axle edits our voxel data (intercept ItemChisel; reuse
  voxel math) rather than converting the block.
- Cost: reimplement the chisel editing loop (can't inherit BlockEntityChisel).
  Keeps power trivially correct and needs no new blocktype.

**Recommendation: Path B.** Power correctness is the whole point of the mod, and
B keeps it free by never changing the block; it also avoids the fragile chisel
blocktype JSON. The tradeoff is reimplementing voxel editing, but the microblock
static helpers (CreateMesh/ToUint/FromUint/voxel math) carry most of it.

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
