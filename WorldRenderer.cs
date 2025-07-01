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
            // Frustum culling and mesh batching by Y row
            var renderers = _chunkManager.GetAllRenderers().ToList();
            // Group by Y (vertical row)
            var grouped = renderers.GroupBy(kvp => kvp.Key.Item2).OrderBy(g => g.Key);
            foreach (var group in grouped)
            {
                foreach (var kvp in group)
                {
                    var (key, renderer) = (kvp.Key, kvp.Value);
                    // Compute world position for this chunk
                    var chunkWorldPos = new Vector3(
                        key.Item1 * Chunk.SizeX,
                        key.Item2 * Chunk.SizeY,
                        key.Item3 * Chunk.SizeZ
                    );
                    // Frustum culling: chunk AABB
                    var min = chunkWorldPos;
                    var max = chunkWorldPos + new Vector3(Chunk.SizeX, Chunk.SizeY, Chunk.SizeZ);
                    if (!FrustumCulling.IsBoxInFrustum(view, proj, min, max))
                        continue;
                    // No chunk-based occlusion culling; rely on block-level face culling in ChunkRenderer
                    Matrix4 chunkModel = Matrix4.CreateTranslation(chunkWorldPos);
                    GL.UniformMatrix4(GL.GetUniformLocation(_shaderProgram, "model"), false, ref chunkModel);
                    renderer.Render();
                }
            }
        }
    }
}
