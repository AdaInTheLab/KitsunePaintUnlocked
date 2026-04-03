# PaintUnlocked

**Breaks the hardcoded 255 paint texture limit in 7 Days to Die, raising it to 1023.**

Vanilla 7D2D caps paint textures at 255 across four separate engine layers. PaintUnlocked patches all four simultaneously using Harmony, allowing large paint packs like PyroPaints, CK Textures, and KitsunePaints to run together without conflict.

## Requirements

- 7 Days to Die V2.0+
- [OcbCustomTextures](https://github.com/OCB7D2D/OcbCustomTextures) (PaintUnlocked-compatible fork)
- EAC **disabled** on server and all clients

## Installation

1. Drop the `PaintUnlocked` folder (containing `PaintUnlocked.dll` and `ModInfo.xml`) into `Mods/` on **both the server and all connecting clients**.
2. Install the PaintUnlocked-compatible OcbCustomTextures fork on both server and clients.
3. A **fresh world is required** for correct 10-bit chunk storage. Existing worlds will display paint index 0 on previously painted blocks (cosmetic only, no crashes).

Unpatched clients can still connect and use paint slots 0-254 normally. Slots 255+ will not render correctly for them.

## What it patches

### Layer 1: Network packets

`NetPackageSetBlockTexture` sends paint indices as a single `byte` (max 255). The packet is exactly 19 bytes and `GetLength()` is hardcoded -- adding bytes causes stream desync and instant disconnection.

PaintUnlocked repurposes the `channel` byte field to carry overflow bits. Bit 7 is an overflow flag: when set, the remaining 7 bits of channel plus the idx byte form a 15-bit index. For indices 0-254, the packet is byte-identical to vanilla.

### Layer 2: Chunk storage (10-bit)

`Chunk.SetBlockFaceTexture`, `Chunk.GetBlockFaceTexture`, and `Chunk.Value64FullToIndex` all use 8-bit masks (`0xFF`) and 8-bit shifts to pack/unpack paint indices into an `Int64` per block. IL transpilers widen these to 10-bit (`0x3FF` mask, 10-bit shifts), supporting up to 1023 indices per face.

A postfix clamp on `Value64FullToIndex` prevents array-out-of-bounds crashes when loading old 8-bit world data into the 10-bit decoder.

### Layer 3: Paint ID allocation

The server loads fewer vanilla paints than the client (~155 vs ~407), so custom paint IDs diverge unless forced to a common floor. PaintUnlocked seeds `GetFreePaintID` at ID 512 on both sides, ensuring identical allocation regardless of vanilla paint count.

### Layer 4: Network buffer sizing

`NetPackagePersistentPlayerState.GetLength()` returns 1000 bytes, which overflows with many custom paints and causes `Unknown NetPackage ID` disconnections. PaintUnlocked expands this to 65536 bytes.

### UI protection

A finalizer on `XUiC_ItemStack.updateBackgroundTexture` catches `NullReferenceException` for paint IDs beyond the texture atlas size, keeping the toolbelt functional.

### Debug command

`pu_debug <paintID>` -- dumps `BlockTextureData` for a specific paint, including texture atlas mappings and GPU slot assignments. Available in the F1 console.

## Compatibility

- Works with any OcbCustomTextures-based paint pack (KitsunePaints, PyroPaints, CK Textures, etc.)
- Backward compatible: indices 0-254 are wire-identical to vanilla
- Unpatched clients work for vanilla paint range
- **Not compatible with vanilla OcbCustomTextures** -- requires the PaintUnlocked-compatible fork that handles dynamic array resizing, 512 ID floor, and atlas size calculations

## Known limitations

- `TextureIdxToTextureFullValue64` (paint menu -> block storage) is not yet patched for above-255 indices. Painting from the radial wheel texture picker works correctly as a workaround.
- Toolbelt thumbnails may be blank for custom paints above the vanilla atlas size. Cosmetic only.
- Fresh world required for correct 10-bit chunk storage. Old worlds display paint 0 on previously painted blocks.

## Building from source

Copy these from your 7D2D install's `7DaysToDie_Data/Managed/` into `7dtd-binaries/`:

- `Assembly-CSharp.dll`
- `Assembly-CSharp-firstpass.dll`
- `UnityEngine.dll`
- `UnityEngine.CoreModule.dll`
- `0Harmony.dll`
- `CustomTextures.dll` (from OcbCustomTextures)
- `LogLibrary.dll`

Then:

```
dotnet build PaintUnlocked.csproj -c Release
```

Output: `bin/Release/net48/PaintUnlocked.dll`

## Versioning

PaintUnlocked and the OcbCustomTextures fork ship as a **single release zip** to prevent version mismatches. Both mods must be the same release -- mismatched versions cause silent failures.

- **PaintUnlocked**: semver (e.g. `1.0.0`), drives the shared version
- **OcbCustomTextures fork**: `{upstream base}-pu{version}` (e.g. `0.8.0-pu1.0.0`)

Both version numbers live in their respective `ModInfo.xml` files and are bumped together on every release.

## License

MIT -- see [LICENSE](LICENSE).

## Credits

- [ocbMaurice](https://github.com/OCB7D2D) for OcbCustomTextures, the foundation this builds on
- The 7D2D modding community for paint packs that inspired this work
