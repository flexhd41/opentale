using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace VoxelEngine;

public class ChunkRenderer
{
    private int _vao, _vbo, _ebo;
    private int _vertexCount;

    public struct MeshData
    {
        public float[] Vertices;
        public uint[] Indices;
    }

    // Generate mesh data in the background
    public static MeshData GenerateMeshData(Chunk chunk, (int, int, int) chunkKey)
    {
        List<float> vertices = new();
        List<uint> indices = new();
        uint index = 0;
        // Per-chunk color tint for grass
        var rand = new System.Random(chunkKey.GetHashCode());
        float tint = 0.85f + 0.3f * (float)rand.NextDouble(); // [0.85,1.15]
        // Greedy meshing for top (Y+) face
        bool[,,] visited = new bool[Chunk.SizeX, Chunk.SizeY, Chunk.SizeZ];
        // Top face (Y+)
        for (int y = 0; y < Chunk.SizeY; y++)
        {
            for (int z = 0; z < Chunk.SizeZ; z++)
            {
                for (int x = 0; x < Chunk.SizeX; x++)
                {
                    if (visited[x, y, z]) continue;
                    BlockType type = chunk[x, y, z];
                    if (type == BlockType.Air) continue;
                    // Only add top face if above is not solid
                    if (!chunk.IsBlockSolid(x, y + 1, z))
                    {
                        // Find width
                        int width = 1;
                        while (x + width < Chunk.SizeX && !visited[x + width, y, z] && chunk[x + width, y, z] == type && !chunk.IsBlockSolid(x + width, y + 1, z))
                            width++;
                        // Find height
                        int height = 1;
                        bool done = false;
                        while (z + height < Chunk.SizeZ && !done)
                        {
                            for (int k = 0; k < width; k++)
                            {
                                if (visited[x + k, y, z + height] || chunk[x + k, y, z + height] != type || chunk.IsBlockSolid(x + k, y + 1, z + height))
                                {
                                    done = true;
                                    break;
                                }
                            }
                            if (!done) height++;
                        }
                        // Add merged face
                        AddQuad(vertices, indices, ref index, x, y, z, width, height, 3, type, tint);
                        // Mark visited
                        for (int dz = 0; dz < height; dz++)
                            for (int dx = 0; dx < width; dx++)
                                visited[x + dx, y, z + dz] = true;
                    }
                }
            }
        }
        // Fallback: add other faces (not merged)
        for (int x = 0; x < Chunk.SizeX; x++)
        for (int y = 0; y < Chunk.SizeY; y++)
        for (int z = 0; z < Chunk.SizeZ; z++)
        {
            BlockType type = chunk[x, y, z];
            if (type == BlockType.Air) continue;
            for (int face = 0; face < 6; face++)
            {
                if (face == 3) continue; // skip top face, already merged
                var (nx, ny, nz) = face switch
                {
                    0 => (0, 0, -1),
                    1 => (0, 0, 1),
                    2 => (0, -1, 0),
                    4 => (-1, 0, 0),
                    5 => (1, 0, 0),
                    _ => (0, 0, 0)
                };
                if (!chunk.IsBlockSolid(x + nx, y + ny, z + nz))
                {
                    AddFace(vertices, indices, ref index, x, y, z, face, type, tint);
                }
            }
        }
        return new MeshData { Vertices = vertices.ToArray(), Indices = indices.ToArray() };
    }

    // Add a merged quad for greedy meshing (top face only)
    private static void AddQuad(List<float> vertices, List<uint> indices, ref uint index, int x, int y, int z, int width, int height, int face, BlockType type, float grassTint)
    {
        // Only for top face (face==3)
        // Vertices: (x, y+1, z), (x+width, y+1, z), (x+width, y+1, z+height), (x, y+1, z+height)
        Vector3[] quadVerts = new[]
        {
            new Vector3(x, y+1, z),
            new Vector3(x+width, y+1, z),
            new Vector3(x+width, y+1, z+height),
            new Vector3(x, y+1, z+height)
        };
        Vector3 color = type switch
        {
            BlockType.Grass => new Vector3(0.1f * grassTint, 0.9f * grassTint, 0.1f * grassTint),
            BlockType.Dirt => new Vector3(0.55f, 0.27f, 0.07f),
            BlockType.Stone => new Vector3(0.6f, 0.6f, 0.7f),
            BlockType.Water => new Vector3(0.1f, 0.4f, 1.0f),
            BlockType.Sand => new Vector3(1.0f, 0.95f, 0.6f),
            _ => new Vector3(0.9f, 0.9f, 0.9f)
        };
        foreach (var v in quadVerts)
        {
            vertices.Add(v.X - Chunk.SizeX/2);
            vertices.Add(v.Y - Chunk.SizeY/2);
            vertices.Add(v.Z - Chunk.SizeZ/2);
            vertices.Add(color.X);
            vertices.Add(color.Y);
            vertices.Add(color.Z);
        }
        indices.Add(index); indices.Add(index+1); indices.Add(index+2);
        indices.Add(index); indices.Add(index+2); indices.Add(index+3);
        index += 4;
    }

    // Construct renderer from mesh data (main thread only)
    public ChunkRenderer(MeshData mesh)
    {
        _vertexCount = mesh.Indices.Length;
        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();
        _ebo = GL.GenBuffer();
        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, mesh.Vertices.Length * sizeof(float), mesh.Vertices, BufferUsageHint.StaticDraw);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ebo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, mesh.Indices.Length * sizeof(uint), mesh.Indices, BufferUsageHint.StaticDraw);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);
    }

    public void Render()
    {
        GL.BindVertexArray(_vao);
        GL.DrawElements(PrimitiveType.Triangles, _vertexCount, DrawElementsType.UnsignedInt, 0);
    }

    public void Dispose()
    {
        GL.DeleteBuffer(_vbo);
        GL.DeleteBuffer(_ebo);
        GL.DeleteVertexArray(_vao);
    }

    private static void AddFace(List<float> vertices, List<uint> indices, ref uint index, int x, int y, int z, int face, BlockType type, float grassTint)
    {
        // Cube face vertex positions and colors
        Vector3[] faceVerts = face switch
        {
            0 => new[] { new Vector3(0,0,0), new Vector3(1,0,0), new Vector3(1,1,0), new Vector3(0,1,0) }, // back
            1 => new[] { new Vector3(0,0,1), new Vector3(1,0,1), new Vector3(1,1,1), new Vector3(0,1,1) }, // front
            2 => new[] { new Vector3(0,0,0), new Vector3(1,0,0), new Vector3(1,0,1), new Vector3(0,0,1) }, // bottom
            3 => new[] { new Vector3(0,1,0), new Vector3(1,1,0), new Vector3(1,1,1), new Vector3(0,1,1) }, // top
            4 => new[] { new Vector3(0,0,0), new Vector3(0,1,0), new Vector3(0,1,1), new Vector3(0,0,1) }, // left
            5 => new[] { new Vector3(1,0,0), new Vector3(1,1,0), new Vector3(1,1,1), new Vector3(1,0,1) }, // right
            _ => null
        };
        if (faceVerts == null) return;
        Vector3 color = type switch
        {
            BlockType.Grass => new Vector3(0.1f * grassTint, 0.9f * grassTint, 0.1f * grassTint),
            BlockType.Dirt => new Vector3(0.55f, 0.27f, 0.07f),
            BlockType.Stone => new Vector3(0.6f, 0.6f, 0.7f),
            BlockType.Water => new Vector3(0.1f, 0.4f, 1.0f),
            BlockType.Sand => new Vector3(1.0f, 0.95f, 0.6f),
            _ => new Vector3(0.9f, 0.9f, 0.9f)
        };
        foreach (var v in faceVerts)
        {
            vertices.Add(x + v.X - Chunk.SizeX/2);
            vertices.Add(y + v.Y - Chunk.SizeY/2);
            vertices.Add(z + v.Z - Chunk.SizeZ/2);
            vertices.Add(color.X);
            vertices.Add(color.Y);
            vertices.Add(color.Z);
        }
        indices.Add(index); indices.Add(index+1); indices.Add(index+2);
        indices.Add(index); indices.Add(index+2); indices.Add(index+3);
        index += 4;
    }
}
