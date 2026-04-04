using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;

public class PaintUnlockedMod : IModApi
{
    public void InitMod(Mod _modInstance)
    {
        var harmony = new Harmony("com.adainthelab.paintunlocked");

        // DIAGNOSTIC: Find where chnTextures is initialized
        Log.Out("[PaintUnlocked] DIAGNOSTIC: Searching for chnTextures initialization");

        // Check bytesPerVal at runtime via postfix on Chunk constructor
        var chunkCtors = typeof(Chunk).GetConstructors();
        foreach (var ctor in chunkCtors)
        {
            Log.Out($"[PaintUnlocked] Found Chunk constructor: {ctor}");
        }

        // Look for the bytesPerVal value on the texture channel via a method that runs after chunk init
        // Also dump Chunk.OnLoadedFromDisk or similar init methods
        var initMethods = typeof(Chunk).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
        foreach (var m in initMethods)
        {
            if (m.Name.Contains("nit") || m.Name.Contains("exture") || m.Name.Contains("hnTex"))
                Log.Out($"[PaintUnlocked] Chunk method: {m.Name}({string.Join(", ", System.Array.ConvertAll(m.GetParameters(), p => p.ParameterType.Name))})");
        }

        // Check ChunkBlockChannel constructors
        var cbcCtors = typeof(ChunkBlockChannel).GetConstructors();
        foreach (var ctor in cbcCtors)
        {
            var parms = ctor.GetParameters();
            Log.Out($"[PaintUnlocked] ChunkBlockChannel ctor: ({string.Join(", ", System.Array.ConvertAll(parms, p => $"{p.ParameterType.Name} {p.Name}"))})");
        }

        // Dump Chunk constructor IL to find bytesPerVal=6
        var chunkCtor2 = typeof(Chunk).GetConstructor(System.Type.EmptyTypes);  // default ctor
        if (chunkCtor2 != null)
        {
            harmony.Patch(chunkCtor2, transpiler: new HarmonyMethod(AccessTools.Method(typeof(ChunkTexturePatch), "DumpIL_ChunkCtor")));
        }
        else Log.Warning("[PaintUnlocked] Chunk() default ctor not found");

        Log.Out("[PaintUnlocked] DIAGNOSTIC: Dumping ChunkBlockChannel IL");
        var cbcType = typeof(ChunkBlockChannel);
        var cbcGet = AccessTools.Method(cbcType, "Get", new[] { typeof(int), typeof(int), typeof(int) });
        var cbcSet = AccessTools.Method(cbcType, "Set", new[] { typeof(int), typeof(int), typeof(int), typeof(long) });
        if (cbcGet != null)
        {
            harmony.Patch(cbcGet, transpiler: new HarmonyMethod(AccessTools.Method(typeof(ChunkTexturePatch), "DumpIL_Get")));
        }
        else Log.Warning("[PaintUnlocked] ChunkBlockChannel.Get not found");
        if (cbcSet != null)
        {
            harmony.Patch(cbcSet, transpiler: new HarmonyMethod(AccessTools.Method(typeof(ChunkTexturePatch), "DumpIL_Set")));
        }
        else Log.Warning("[PaintUnlocked] ChunkBlockChannel.Set not found");

        var setBlockFaceTex = AccessTools.Method(typeof(Chunk), "SetBlockFaceTexture");
        harmony.Patch(setBlockFaceTex, transpiler: new HarmonyMethod(AccessTools.Method(typeof(ChunkTexturePatch), "PatchSet")));

        var getBlockFaceTex = AccessTools.Method(typeof(Chunk), "GetBlockFaceTexture");
        harmony.Patch(getBlockFaceTex, transpiler: new HarmonyMethod(AccessTools.Method(typeof(ChunkTexturePatch), "PatchGet")));

        var v64ToIdx = typeof(Chunk).GetMethod("Value64FullToIndex", BindingFlags.Public | BindingFlags.Static);
        if (v64ToIdx != null)
        {
            harmony.Patch(v64ToIdx, transpiler: new HarmonyMethod(AccessTools.Method(typeof(ChunkTexturePatch), "PatchValue64ToIndex")));
            harmony.Patch(v64ToIdx, postfix: new HarmonyMethod(AccessTools.Method(typeof(ChunkTexturePatch), "ClampValue64Result")));
        }

        Log.Out("[PaintUnlocked] Loaded - paint texture limit removed (byte -> ushort, chunk storage 8-bit -> 10-bit).");
        Log.Warning("[PaintUnlocked] IMPORTANT: This mod uses 10-bit chunk storage. Existing worlds painted with the vanilla 8-bit format will show default textures on previously painted blocks. A fresh world is required for correct operation.");
    }
}

public static class PaintIndexWidenerPatch
{
    private const byte OverflowFlag = 0x80;

    private static readonly Dictionary<int, ushort> _idxMap = new Dictionary<int, ushort>();
    private static readonly object _idxLock = new object();

    private static readonly BindingFlags _fieldFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly FieldInfo _fIdx =
        typeof(NetPackageSetBlockTexture).GetField("idx", _fieldFlags);
    private static readonly FieldInfo _fBlockPos =
        typeof(NetPackageSetBlockTexture).GetField("blockPos", _fieldFlags);
    private static readonly FieldInfo _fBlockFace =
        typeof(NetPackageSetBlockTexture).GetField("blockFace", _fieldFlags);
    private static readonly FieldInfo _fPlayerId =
        typeof(NetPackageSetBlockTexture).GetField("playerIdThatChanged", _fieldFlags);
    private static readonly FieldInfo _fChannel =
        typeof(NetPackageSetBlockTexture).GetField("channel", _fieldFlags);

    private static readonly BindingFlags _methodFlags =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
    private static readonly MethodInfo _writeInt =
        typeof(PooledBinaryWriter).GetMethod("Write", _methodFlags, null, new[] { typeof(int) }, null);
    private static readonly MethodInfo _writeByte =
        typeof(PooledBinaryWriter).GetMethod("Write", _methodFlags, null, new[] { typeof(byte) }, null);

    private static bool _reflectionValid = false;

    static PaintIndexWidenerPatch()
    {
        _reflectionValid = _fIdx != null && _fBlockPos != null && _fBlockFace != null
                        && _fPlayerId != null && _fChannel != null
                        && _writeInt != null && _writeByte != null;
        if (!_reflectionValid)
            Log.Warning($"[PaintUnlocked] Reflection check FAILED");
        else
            Log.Out("[PaintUnlocked] Reflection check passed - all fields found.");
    }

    private static void StoreIdx(NetPackageSetBlockTexture instance, ushort value)
    {
        var key = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(instance);
        lock (_idxLock) { _idxMap[key] = value; }
        _fIdx?.SetValue(instance, (byte)(value & 0xFF));
    }

    private static ushort LoadIdx(NetPackageSetBlockTexture instance)
    {
        var key = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(instance);
        lock (_idxLock) { if (_idxMap.TryGetValue(key, out var v)) return v; }
        return _fIdx != null ? (byte)_fIdx.GetValue(instance) : (byte)0;
    }

    public static void SetupPostfix(NetPackageSetBlockTexture __instance, int _idx)
    {
        if (!_reflectionValid) return;
        StoreIdx(__instance, (ushort)_idx);
    }

    /// <summary>
    /// For overflow indices (256+), modify the channel and idx fields on the instance
    /// BEFORE vanilla write() runs. Vanilla write() then serializes our values through
    /// its normal PooledBinaryWriter path -- no bypassing, no stream corruption.
    ///
    /// Encoding: channel = OverflowFlag | (idx >> 8), idx = (byte)(idx & 0xFF)
    /// </summary>
    public static void WritePrefix(NetPackageSetBlockTexture __instance)
    {
        if (!_reflectionValid) return;

        var idx = LoadIdx(__instance);
        if (idx <= 255) return;

        byte channelWire = (byte)(OverflowFlag | ((idx >> 8) & 0x7F));
        byte idxLow = (byte)(idx & 0xFF);

        _fChannel.SetValue(__instance, channelWire);
        _fIdx.SetValue(__instance, idxLow);

        Log.Out($"[PaintUnlocked] WritePrefix: idx={idx} -> channel=0x{channelWire:X2} idx=0x{idxLow:X2}");
    }

    /// <summary>
    /// Runs before ProcessPackage to decode overflow encoding that vanilla read() stored literally.
    /// This is the reliable server-side decode path -- ReadPrefix may not fire due to virtual dispatch.
    /// If ReadPrefix already decoded (channel won't have overflow flag), this is a no-op.
    ///
    /// For overflow packets, this replaces ProcessPackage entirely (returns false) because the
    /// vanilla code reads the byte-sized idx field which can only hold 0-255. We must call
    /// SetBlockFaceTexture ourselves with the full decoded index.
    /// </summary>
    public static bool ProcessPackagePrefix(NetPackageSetBlockTexture __instance, World _world)
    {
        if (!_reflectionValid) return true;

        try
        {
            var channel = (byte)_fChannel.GetValue(__instance);

            // If ReadPrefix already fired and decoded, channel won't have the flag -- let vanilla run
            if ((channel & OverflowFlag) == 0)
            {
                // But check _idxMap in case ReadPrefix decoded an overflow for us
                var fullIdx = LoadIdx(__instance);
                if (fullIdx <= 255) return true; // normal packet, let vanilla handle it

                // ReadPrefix decoded it -- we still need to apply it ourselves since idx field is byte
                var blockPos2  = (Vector3i)_fBlockPos.GetValue(__instance);
                var blockFace2 = (BlockFace)_fBlockFace.GetValue(__instance);
                var playerId2  = (int)_fPlayerId.GetValue(__instance);

                Log.Out($"[PaintUnlocked] ProcessPackagePrefix: applying ReadPrefix-decoded idx={fullIdx} at {blockPos2} face={blockFace2}");
                ApplyTexture(_world, blockPos2, blockFace2, fullIdx, playerId2);
                return false;
            }

            // Overflow flag still set -- ReadPrefix didn't fire (server virtual dispatch issue)
            var idxByte = (byte)_fIdx.GetValue(__instance);
            ushort decodedIdx = (ushort)(((channel & 0x7F) << 8) | idxByte);

            var blockPos  = (Vector3i)_fBlockPos.GetValue(__instance);
            var blockFace = (BlockFace)_fBlockFace.GetValue(__instance);
            var playerId  = (int)_fPlayerId.GetValue(__instance);

            Log.Out($"[PaintUnlocked] ProcessPackagePrefix: server-side overflow decode channel=0x{channel:X2} idx=0x{idxByte:X2} -> fullIdx={decodedIdx} at {blockPos} face={blockFace}");

            ApplyTexture(_world, blockPos, blockFace, decodedIdx, playerId);
            return false; // skip vanilla ProcessPackage
        }
        catch (System.Exception ex)
        {
            Log.Error($"[PaintUnlocked] ProcessPackagePrefix failed: {ex.Message}");
            return true; // fall through to vanilla on error
        }
    }

    private static void ApplyTexture(World _world, Vector3i blockPos, BlockFace blockFace, ushort idx, int playerId)
    {
        // Mirror what vanilla ProcessPackage does: set the texture on the block face
        var cc = _world.ChunkClusters[0];
        if (cc == null) return;

        var chunk = (Chunk)cc.GetChunkFromWorldPos(blockPos);
        if (chunk == null) return;

        var localPos = World.toBlock(blockPos);
        chunk.SetBlockFaceTexture(localPos.x, localPos.y, localPos.z, blockFace, idx);
        chunk.isModified = true;
    }

    public static bool ReadPrefix(NetPackageSetBlockTexture __instance, PooledBinaryReader _br)
    {
        // Let vanilla read() handle the bytes. We decode overflow in ProcessPackagePrefix.
        // ReadPrefix was unreliable on dedicated server due to virtual dispatch anyway.
        return true;
    }
}
