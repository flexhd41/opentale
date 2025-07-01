using OpenTK.Windowing.Desktop;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace VoxelEngine
{
    public static class Program
    {
        public static void Main()
        {
            var nativeWindowSettings = new NativeWindowSettings()
            {
                ClientSize = new Vector2i(800, 600),
                Title = "Voxel Engine (OpenTK)",
            };
            using (var window = new VoxelWindow(GameWindowSettings.Default, nativeWindowSettings))
            {
                window.Run();
            }
        }
    }

    // Modern OpenTK 4.x cube rendering with shaders and VBOs
    public class VoxelWindow : GameWindow

    {
        private int _shaderProgram;
        private Matrix4 _proj;
        private ChunkManager _chunkManager = new();
        private WorldRenderer? _worldRenderer;
        private FreeCamera? _camera;
        private bool _breakHeld = false;
        private double _frameTimeSum = 0;
        private int _frameCount = 0;
        private double _lastFpsUpdate = 0;
        private double _fps = 0;
        private double _totalTime = 0;
        private bool _placeHeld = false;
        public VoxelWindow(GameWindowSettings gws, NativeWindowSettings nws) : base(gws, nws) { }
        protected override void OnLoad()
        {
            base.OnLoad();
            GL.ClearColor(0.2f, 0.3f, 0.3f, 1.0f);
            GL.Enable(EnableCap.DepthTest);

            // Compile shaders
            string vert = @"#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aColor;
out vec3 vColor;
uniform mat4 model;
uniform mat4 view;
uniform mat4 proj;
void main() {
    vColor = aColor;
    gl_Position = proj * view * model * vec4(aPos, 1.0);
}";
            string frag = @"#version 330 core
in vec3 vColor;
out vec4 FragColor;
void main() {
    FragColor = vec4(vColor, 1.0);
}";
            int vs = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vs, vert);
            GL.CompileShader(vs);
            int fs = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fs, frag);
            GL.CompileShader(fs);
            _shaderProgram = GL.CreateProgram();
            GL.AttachShader(_shaderProgram, vs);
            GL.AttachShader(_shaderProgram, fs);
            GL.LinkProgram(_shaderProgram);
            GL.DeleteShader(vs);
            GL.DeleteShader(fs);

            // Camera
            _camera = new FreeCamera(new Vector3(32, 32, 64));
            _proj = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(45f), Size.X/(float)Size.Y, 0.1f, 1000f);

            // World renderer
            _worldRenderer = new WorldRenderer(_chunkManager, _shaderProgram);

            CursorState = OpenTK.Windowing.Common.CursorState.Grabbed;
        }

        protected override void OnResize(OpenTK.Windowing.Common.ResizeEventArgs e)
        {
            base.OnResize(e);
            GL.Viewport(0, 0, Size.X, Size.Y);
            _proj = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(45f), Size.X/(float)Size.Y, 0.1f, 1000f);
        }

        protected override void OnUpdateFrame(OpenTK.Windowing.Common.FrameEventArgs args)
        {
            base.OnUpdateFrame(args);
            if (_camera != null)
            {
                _camera.Update((float)args.Time, KeyboardState, MouseState);
                _chunkManager.UpdateChunks(_camera.Position);
            }

            // Block breaking (left mouse)
            if (MouseState.IsButtonDown(OpenTK.Windowing.GraphicsLibraryFramework.MouseButton.Left))
            {
                if (!_breakHeld)
                {
                    BreakBlock();
                    _breakHeld = true;
                }
            }
            else
            {
                _breakHeld = false;
            }

            // Block placing (right mouse)
            if (MouseState.IsButtonDown(OpenTK.Windowing.GraphicsLibraryFramework.MouseButton.Right))
            {
                if (!_placeHeld)
                {
                    PlaceBlock();
                    _placeHeld = true;
                }
            }
            else
            {
                _placeHeld = false;
            }
        }
        private void PlaceBlock()
        {
            if (_camera == null) return;
            var (chunk, (lx, ly, lz), chunkKey) = _chunkManager.RaycastPlace(_camera.Position, _camera.Front);
            if (chunk != null && chunk[lx, ly, lz] == BlockType.Air)
            {
                chunk[lx, ly, lz] = BlockType.Grass;
                // Rebuild renderer for this chunk only
            if (_chunkManager is { })
            {
                if (_chunkManager.GetRenderer(chunkKey) is ChunkRenderer renderer)
                {
                    renderer.Dispose();
                    var mesh = ChunkRenderer.GenerateMeshData(chunk, chunkKey);
                    _chunkManager.SetRenderer(chunkKey, new ChunkRenderer(mesh));
                }
            }
            }
        }

        private void BreakBlock()
        {
            if (_camera == null) return;
            var (chunk, (lx, ly, lz)) = _chunkManager.RaycastChunk(_camera.Position, _camera.Front);
            if (chunk != null && chunk[lx, ly, lz] != BlockType.Air)
            {
                chunk[lx, ly, lz] = BlockType.Air;
                // Rebuild renderer for this chunk only
                var chunkKey = _chunkManager.GetChunkKey(chunk);
            if (chunkKey is { } key)
            {
                if (_chunkManager.GetRenderer(key) is ChunkRenderer renderer)
                {
                    renderer.Dispose();
                    var mesh = ChunkRenderer.GenerateMeshData(chunk, key);
                    _chunkManager.SetRenderer(key, new ChunkRenderer(mesh));
                }
            }
            }
        }

        protected override void OnRenderFrame(OpenTK.Windowing.Common.FrameEventArgs args)
        {
            base.OnRenderFrame(args);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            if (_worldRenderer != null && _camera != null)
            {
                Matrix4 model = Matrix4.Identity;
                Matrix4 view = _camera.GetViewMatrix();
                _worldRenderer.Render(model, view, _proj);
            }

            // FPS counter and debug info
            _frameTimeSum += args.Time;
            _frameCount++;
            _totalTime += args.Time;
            if (_totalTime - _lastFpsUpdate > 0.5)
            {
                _fps = _frameCount / _frameTimeSum;
                _frameTimeSum = 0;
                _frameCount = 0;
                _lastFpsUpdate = _totalTime;
                int chunkCount = _chunkManager != null ? _chunkManager.GetAllRenderers().Count() : 0;
                var cam = _camera != null ? _camera.Position : Vector3.Zero;
                Title = $"Voxel Engine (FPS: {_fps:F1}) | Chunks: {chunkCount} | Cam: ({cam.X:F1},{cam.Y:F1},{cam.Z:F1})";
            }
            SwapBuffers();
        }

        protected override void OnUnload()
        {
            base.OnUnload();
            GL.DeleteProgram(_shaderProgram);
        }
    }
}
