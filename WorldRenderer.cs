using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace VoxelEngine
{
    public class WorldRenderer
    {
        private readonly ChunkManager _chunkManager;
        private readonly int _shaderProgram;

        public WorldRenderer(ChunkManager chunkManager, int shaderProgram)
        {
            _chunkManager = chunkManager;
            _shaderProgram = shaderProgram;
        }

        public void Render(Matrix4 model, Matrix4 view, Matrix4 proj)
        {
            GL.UseProgram(_shaderProgram);
            GL.UniformMatrix4(GL.GetUniformLocation(_shaderProgram, "view"), false, ref view);
            GL.UniformMatrix4(GL.GetUniformLocation(_shaderProgram, "proj"), false, ref proj);
            foreach (var kvp in _chunkManager.GetAllRenderers())
            {
                var (key, renderer) = (kvp.Key, kvp.Value);
                // Compute world position for this chunk
                var chunkWorldPos = new Vector3(
                    key.Item1 * Chunk.SizeX,
                    key.Item2 * Chunk.SizeY,
                    key.Item3 * Chunk.SizeZ
                );
                Matrix4 chunkModel = Matrix4.CreateTranslation(chunkWorldPos);
                GL.UniformMatrix4(GL.GetUniformLocation(_shaderProgram, "model"), false, ref chunkModel);
                renderer.Render();
            }
        }
    }
}
