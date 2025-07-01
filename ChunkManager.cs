
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using OpenTK.Mathematics;

namespace VoxelEngine
{
public class ChunkManager {
    private readonly BlockManager _blockManager;
    // Thread-safe queues for main-thread OpenGL work
    private readonly ConcurrentQueue<((int, int, int), Chunk, ChunkRenderer.MeshData)> _uploadQueue = new();
    private readonly ConcurrentQueue<(int, int, int)> _disposalQueue = new();
    private const int MaxChunkUploadsPerFrame = 2;
    private const int MaxChunkDisposalsPerFrame = 2;
    // Enumerate all chunk keys and renderers
    public IEnumerable<KeyValuePair<(int, int, int), ChunkRenderer>> GetAllRenderers()
    {
        foreach (var kvp in _renderers)
            yield return kvp;
    }
    public int RenderDistance { get; set; } = 8;
    private readonly Dictionary<(int, int, int), Chunk> _chunks = new();
    private readonly Dictionary<(int, int, int), ChunkRenderer> _renderers = new();
    public int VerticalRenderDistance { get; set; } = 1;

    // Background worldgen fields
    private readonly Queue<(int, int, int)> _genQueue = new();
    private readonly Queue<(int, int, int, Chunk, ChunkRenderer.MeshData)> _readyQueue = new();
    private readonly object _genLock = new();
    private Thread? _worldgenThread;
    private bool _running = true;

    public ChunkManager()
    {
        _blockManager = new BlockManager();
        // Start worldgen thread
        _worldgenThread = new Thread(WorldgenWorker) { IsBackground = true };
        _worldgenThread.Start();
    }

    public ChunkRenderer? GetRenderer((int, int, int) key)
    {
        _renderers.TryGetValue(key, out var renderer);
        return renderer;
    }

    public void SetRenderer((int, int, int) key, ChunkRenderer renderer)
    {
        _renderers[key] = renderer;
    }

    public (int, int, int)? GetChunkKey(Chunk chunk)
    {
        foreach (var kvp in _chunks)
        {
            if (ReferenceEquals(kvp.Value, chunk))
                return kvp.Key;
        }
        return null;
    }

    public void UpdateChunks(Vector3 cameraPos)
    {
        System.Diagnostics.Debug.WriteLine($"UpdateChunks called with cameraPos=({cameraPos.X},{cameraPos.Y},{cameraPos.Z})");

        int camChunkX = (int)Math.Floor(cameraPos.X / (float)Chunk.SizeX);
        int camChunkY = (int)Math.Floor(cameraPos.Y / (float)Chunk.SizeY);
        int camChunkZ = (int)Math.Floor(cameraPos.Z / (float)Chunk.SizeZ);
        var needed = new HashSet<(int, int, int)>();
        for (int dx = -RenderDistance; dx <= RenderDistance; dx++)
        for (int dy = -VerticalRenderDistance; dy <= VerticalRenderDistance; dy++)
        for (int dz = -RenderDistance; dz <= RenderDistance; dz++)
        {
            int cx = camChunkX + dx;
            int cy = camChunkY + dy;
            int cz = camChunkZ + dz;
            needed.Add((cx, cy, cz));
        }

        // Queue missing chunks for background generation
        foreach (var key in needed)
        {
            if (!_chunks.ContainsKey(key))
            {
                lock (_genLock)
                {
                    if (!_genQueue.Contains(key))
                    {
                        _genQueue.Enqueue(key);
                        System.Diagnostics.Debug.WriteLine($"QUEUE chunk {key} for worldgen");
                    }
                }
            }
        }

        // Unload chunks no longer needed (enqueue for main-thread disposal)
        foreach (var key in _chunks.Keys.ToList())
        {
            if (!needed.Contains(key))
            {
                _disposalQueue.Enqueue(key);
            }
        }

        // Integrate ready chunks from worldgen thread into upload queue
        lock (_genLock)
        {
            while (_readyQueue.Count > 0)
            {
                var (cx, cy, cz, chunk, mesh) = _readyQueue.Dequeue();
                var key = (cx, cy, cz);
                if (!_chunks.ContainsKey(key))
                {
                    _uploadQueue.Enqueue((key, chunk, mesh));
                }
            }
        }

        // Throttle chunk uploads per frame (main thread OpenGL)
        int uploads = 0;
        while (_uploadQueue.TryDequeue(out var upload) && uploads < MaxChunkUploadsPerFrame)
        {
            var (key, chunk, mesh) = upload;
            if (!_chunks.ContainsKey(key))
            {
                System.Diagnostics.Debug.WriteLine($"LOAD chunk {key} (threaded)");
                _chunks[key] = chunk;
                _renderers[key] = new ChunkRenderer(mesh);
            }
            uploads++;
        }

        // Throttle chunk disposals per frame (main thread OpenGL)
        int disposals = 0;
        while (_disposalQueue.TryDequeue(out var key) && disposals < MaxChunkDisposalsPerFrame)
        {
            if (_renderers.TryGetValue(key, out var renderer))
            {
                System.Diagnostics.Debug.WriteLine($"UNLOAD chunk {key} (threaded)");
                renderer.Dispose();
                _renderers.Remove(key);
            }
            _chunks.Remove(key);
            disposals++;
        }

        System.Diagnostics.Debug.WriteLine($"[DEBUG] _chunks.Count={_chunks.Count}, needed.Count={needed.Count}");
    }

    public void RenderAll()
    {
        System.Diagnostics.Debug.WriteLine($"RenderAll: rendering {_renderers.Count} chunks");
        foreach (var renderer in _renderers.Values)
            renderer.Render();
    }

    // Background worldgen worker
    private void WorldgenWorker()
    {
        while (_running)
        {
            (int, int, int)? key = null;
            lock (_genLock)
            {
                if (_genQueue.Count > 0)
                    key = _genQueue.Dequeue();
            }
            if (key.HasValue)
            {
                var (cx, cy, cz) = key.Value;
                // Fill the block buffer for this chunk
                Chunk.GenerateTerrain(cx, cy, cz, _blockManager);
                var chunk = new Chunk(cx, cy, cz, _blockManager);
                var mesh = ChunkRenderer.GenerateMeshData(chunk, (cx, cy, cz));
                lock (_genLock)
                {
                    _readyQueue.Enqueue((cx, cy, cz, chunk, mesh));
                }
            }
            else
            {
                Thread.Sleep(2);
            }
        }
    }

    // Call this on shutdown to stop the worldgen thread
    public void StopWorldgen()
    {
        _running = false;
        _worldgenThread?.Join();
    }
    

    public (Chunk?, (int, int, int)) RaycastChunk(Vector3 origin, Vector3 dir, float maxDist = 100f)
    {
        for (float t = 0; t < maxDist; t += 0.1f)
        {
            Vector3 p = origin + dir * t;
            int cx = (int)Math.Floor(p.X / (float)Chunk.SizeX);
            int cy = (int)Math.Floor(p.Y / (float)Chunk.SizeY);
            int cz = (int)Math.Floor(p.Z / (float)Chunk.SizeZ);
            int lx = (int)(p.X - cx * Chunk.SizeX);
            int ly = (int)(p.Y - cy * Chunk.SizeY);
            int lz = (int)(p.Z - cz * Chunk.SizeZ);
            if (_chunks.TryGetValue((cx, cy, cz), out var chunk))
            {
                if (lx >= 0 && ly >= 0 && lz >= 0 && lx < Chunk.SizeX && ly < Chunk.SizeY && lz < Chunk.SizeZ)
                {
                    if (chunk[lx, ly, lz] != BlockType.Air)
                        return (chunk, (lx, ly, lz));
                }
            }
        }
        return (null, (0, 0, 0));
    }

    public (Chunk?, (int, int, int), (int, int, int)) RaycastPlace(Vector3 origin, Vector3 dir, float maxDist = 100f)
    {
        int lastCx = 0, lastCy = 0, lastCz = 0, lastLx = 0, lastLy = 0, lastLz = 0;
        bool hasLast = false;
        for (float t = 0; t < maxDist; t += 0.1f)
        {
            Vector3 p = origin + dir * t;
            int cx = (int)Math.Floor(p.X / (float)Chunk.SizeX);
            int cy = (int)Math.Floor(p.Y / (float)Chunk.SizeY);
            int cz = (int)Math.Floor(p.Z / (float)Chunk.SizeZ);
            int lx = (int)(p.X - cx * Chunk.SizeX);
            int ly = (int)(p.Y - cy * Chunk.SizeY);
            int lz = (int)(p.Z - cz * Chunk.SizeZ);
            if (_chunks.TryGetValue((cx, cy, cz), out var chunk))
            {
                if (lx >= 0 && ly >= 0 && lz >= 0 && lx < Chunk.SizeX && ly < Chunk.SizeY && lz < Chunk.SizeZ)
                {
                    if (chunk[lx, ly, lz] != BlockType.Air && hasLast)
                        return (_chunks.TryGetValue((lastCx, lastCy, lastCz), out var prevChunk) ? prevChunk : null, (lastLx, lastLy, lastLz), (lastCx, lastCy, lastCz));
                }
            }
            lastCx = cx; lastCy = cy; lastCz = cz; lastLx = lx; lastLy = ly; lastLz = lz;
            hasLast = true;
        }
        return (null, (0, 0, 0), (0, 0, 0));
    }
    }
}
