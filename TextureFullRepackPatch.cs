using HarmonyLib;

/// <summary>
/// Re-encodes raw Int64 texture values from vanilla 8-bit face packing to 10-bit packing
/// when written via Chunk.SetTextureFull.
///
/// Prefab placement and bulk texture operations pass pre-packed Int64 values where each
/// face's paint index occupies 8 bits at positions 0,8,16,24,32,40. Our 10-bit patched
/// GetBlockFaceTexture/Value64FullToIndex read at positions 0,10,20,30,40,50.
///
/// Without re-encoding, prefab textures show wrong paints on faces 1-5 because the
/// bit positions don't align.
///
/// Individual face painting goes through SetBlockFaceTexture (already 10-bit patched)
/// and does NOT pass through SetTextureFull, so this re-encoding is safe.
/// </summary>
public static class TextureFullRepackPatch
{
    /// <summary>
    /// Re-packs a vanilla 8-bit packed Int64 to 10-bit packed format.
    /// Extracts 6 face values at 8-bit positions and re-packs at 10-bit positions.
    /// </summary>
    public static void Prefix(int _x, int _y, int _z, ref long _texturefull, int channel)
    {
        // Only re-encode for texture channel (channel 0)
        // Values that are already all-zero (unpainted) pass through unchanged
        if (_texturefull == 0L) return;

        long repacked = 0;
        for (int face = 0; face < 6; face++)
        {
            int idx = (int)((_texturefull >> (face * 8)) & 0xFF);
            repacked |= ((long)(idx & 0x3FF) << (face * 10));
        }
        _texturefull = repacked;
    }
}
