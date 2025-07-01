using System;

namespace VoxelEngine
{
    public class BlockManager
    {
        public const int ViewDistance = 8; // Default, can be set elsewhere
        public const uint SIZEX = (uint)(Chunk.SizeX * ((ViewDistance * 2) + 1));
        public const uint SIZEZ = (uint)(Chunk.SizeZ * ((ViewDistance * 2) + 1));
        public const uint SIZEY = Chunk.SizeY;

        public const uint SIZEXY = SIZEX * SIZEY;
        public const uint BUFFERSIZE = SIZEX * SIZEY * SIZEZ;

        public const int ADDX = (int)SIZEY;
        public const int SUBX = -(int)SIZEY;
        public const int ADDY = 1;
        public const int SUBY = -1;
        public const int ADDZ = (int)SIZEXY;
        public const int SUBZ = -(int)SIZEXY;

        public BlockType[] Blocks;

        public BlockManager()
        {
            Blocks = new BlockType[BUFFERSIZE];
        }

        public uint Index(uint x, uint y, uint z)
        {
            uint modx = x % SIZEX;
            uint mody = y % SIZEY;
            uint modz = z % SIZEZ;

            return (modz * SIZEXY) + (modx * SIZEY) + mody;
        }

        public BlockType this[uint x, uint y, uint z]
        {
            get => Blocks[Index(x, y, z)];
            set => Blocks[Index(x, y, z)] = value;
        }
    }
}
