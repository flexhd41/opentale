using OpenTK.Mathematics;

namespace VoxelEngine;

public class Chunk
{
    public const int SizeX = 16;
    public const int SizeY = 16;
    public const int SizeZ = 16;

    private readonly BlockManager _blockManager;
    private readonly int _cx, _cy, _cz;

    public Chunk(int cx, int cz, BlockManager blockManager) : this(cx, 0, cz, blockManager) {}

    public Chunk(int cx, int cy, int cz, BlockManager blockManager)
    {
        _cx = cx;
        _cy = cy;
        _cz = cz;
        _blockManager = blockManager;
        // Worldgen should be handled elsewhere, not here
    }

    public BlockType this[int x, int y, int z]
    {
        get
        {
            uint wx = (uint)(_cx * SizeX + x);
            uint wy = (uint)(_cy * SizeY + y);
            uint wz = (uint)(_cz * SizeZ + z);
            return _blockManager[wx, wy, wz];
        }
        set
        {
            uint wx = (uint)(_cx * SizeX + x);
            uint wy = (uint)(_cy * SizeY + y);
            uint wz = (uint)(_cz * SizeZ + z);
            _blockManager[wx, wy, wz] = value;
        }
    }

    // Worldgen is now handled outside the chunk

    public static void GenerateTerrain(int cx, int cy, int cz, BlockManager blockManager)
    {
        for (int x = 0; x < SizeX; x++)
        for (int z = 0; z < SizeZ; z++)
        {
            int wx = cx * SizeX + x;
            int wz = cz * SizeZ + z;
            float noise = (MathF.Sin(wx * 0.07f) + MathF.Cos(wz * 0.09f)) * 0.5f;
            int height = (int)(32 + 12 * noise);
            for (int y = 0; y < SizeY; y++)
            {
                int wy = cy * SizeY + y;
                BlockType val;
                if (wy > height)
                    val = BlockType.Air;
                else if (wy == height)
                    val = BlockType.Grass;
                else if (wy > height - 3)
                    val = BlockType.Dirt;
                else if (wy < 8)
                    val = BlockType.Stone;
                else
                    val = BlockType.Dirt;
                // Water and sand
                if (wy < 16 && wy > 8)
                {
                    if (val == BlockType.Air)
                        val = BlockType.Water;
                    if (val == BlockType.Grass)
                        val = BlockType.Sand;
                }
                uint ux = (uint)wx;
                uint uy = (uint)wy;
                uint uz = (uint)wz;
                blockManager[ux, uy, uz] = val;
            }
        }
    }

    public bool IsBlockSolid(int x, int y, int z)
    {
        if (x < 0 || y < 0 || z < 0 || x >= SizeX || y >= SizeY || z >= SizeZ)
            return false;
        return this[x, y, z] != BlockType.Air;
    }
}
