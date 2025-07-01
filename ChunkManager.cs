
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using OpenTK.Mathematics;

namespace VoxelEngine
{
public class ChunkManager {
    // Track modified blocks: (chunkX, chunkY, chunkZ, lx, ly, lz) -> BlockType
    private readonly Dictionary<(int, int, int, int, int, int), BlockType> _modifiedBlocks = new();

    // Call this when a block is changed
    public void SetBlockAndTrack(int cx, int cy, int cz, int lx, int ly, int lz, BlockType type)
    {
        var key = (cx, cy, cz, lx, ly, lz);
        if (type == BlockType.Air)
            _modifiedBlocks.Remove(key);
        else
            _modifiedBlocks[key] = type;
    }

    // Save modified blocks to a file (JSON)
    public void SaveWorld(string path)
    {
        var list = _modifiedBlocks.Select(kv => new BlockEdit {
            ChunkX = kv.Key.Item1, ChunkY = kv.Key.Item2, ChunkZ = kv.Key.Item3,
            LX = kv.Key.Item4, LY = kv.Key.Item5, LZ = kv.Key.Item6,
            Type = kv.Value
        }).ToList();
        var json = System.Text.Json.JsonSerializer.Serialize(list);
        System.IO.File.WriteAllText(path, json);
    }

    // Load modified blocks from a file (JSON)
    public void LoadWorld(string path)
    {
        if (!System.IO.File.Exists(path)) return;
        var json = System.IO.File.ReadAllText(path);
        var list = System.Text.Json.JsonSerializer.Deserialize<List<BlockEdit>>(json);
        _modifiedBlocks.Clear();
        if (list != null)
        {
            foreach (var edit in list)
                _modifiedBlocks[(edit.ChunkX, edit.ChunkY, edit.ChunkZ, edit.LX, edit.LY, edit.LZ)] = edit.Type;
        }
    }

    // Apply all saved modifications to the world (call after terrain gen)
    public void ApplyModifications()
    {
        foreach (var kv in _modifiedBlocks)
        {
            var (cx, cy, cz, lx, ly, lz) = kv.Key;
            if (_chunks.TryGetValue((cx, cy, cz), out var chunk))
                chunk[lx, ly, lz] = kv.Value;
        }
    }

    private class BlockEdit
    {
        public int ChunkX { get; set; }
        public int ChunkY { get; set; }
        public int ChunkZ { get; set; }
        public int LX { get; set; }
        public int LY { get; set; }
        public int LZ { get; set; }
        public BlockType Type { get; set; }
    }
    // Returns true if all chunks in a 5-chunk radius around the given position are loaded
    public bool AreMajorChunksLoaded(Vector3 cameraPos, int radius = 5)
    {
        int camChunkX = (int)Math.Floor(cameraPos.X / (float)Chunk.SizeX);
        int camChunkY = (int)Math.Floor(cameraPos.Y / (float)Chunk.SizeY);
        int camChunkZ = (int)Math.Floor(cameraPos.Z / (float)Chunk.SizeZ);
        for (int dx = -radius; dx <= radius; dx++)
        for (int dy = -VerticalRenderDistance; dy <= VerticalRenderDistance; dy++)
        for (int dz = -radius; dz <= radius; dz++)
        {
            int cx = camChunkX + dx;
            int cy = camChunkY + dy;
            int cz = camChunkZ + dz;
            int dist2 = dx * dx + dy * dy + dz * dz;
            if (dist2 <= radius * radius)
            {
                var key = (cx, cy, cz);
                if (!_chunks.ContainsKey(key) || !_renderers.ContainsKey(key))
                    return false;
            }
        }
        return true;
    }
    // Limit concurrent mesh generation tasks
    private static readonly int MaxMeshTasks = System.Environment.ProcessorCount;
    private static readonly System.Threading.SemaphoreSlim MeshSemaphore = new(MaxMeshTasks);
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
        // Improved queuing: always fill a 5-chunk radius around the player first
        var missingNear = new List<(int, int, int)>();
        var missingFar = new List<(int, int, int)>();
        int r2 = RenderDistance * RenderDistance;
        int nearR = 5;
        int nearR2 = nearR * nearR;
        for (int dx = -RenderDistance; dx <= RenderDistance; dx++)
        for (int dy = -VerticalRenderDistance; dy <= VerticalRenderDistance; dy++)
        for (int dz = -RenderDistance; dz <= RenderDistance; dz++)
        {
            int cx = camChunkX + dx;
            int cy = camChunkY + dy;
            int cz = camChunkZ + dz;
            int dist2 = dx * dx + dy * dy + dz * dz;
            if (dist2 <= r2)
            {
                var key = (cx, cy, cz);
                if (!_chunks.ContainsKey(key))
                {
                    if (dist2 <= nearR2)
                        missingNear.Add(key);
                    else
                        missingFar.Add(key);
                }
            }
        }
        // Sort both lists by distance
        missingNear.Sort((a, b) =>
        {
            float da = (a.Item1 - camChunkX) * (a.Item1 - camChunkX) + (a.Item2 - camChunkY) * (a.Item2 - camChunkY) + (a.Item3 - camChunkZ) * (a.Item3 - camChunkZ);
            float db = (b.Item1 - camChunkX) * (b.Item1 - camChunkX) + (b.Item2 - camChunkY) * (b.Item2 - camChunkY) + (b.Item3 - camChunkZ) * (b.Item3 - camChunkZ);
            return da.CompareTo(db);
        });
        missingFar.Sort((a, b) =>
        {
            float da = (a.Item1 - camChunkX) * (a.Item1 - camChunkX) + (a.Item2 - camChunkY) * (a.Item2 - camChunkY) + (a.Item3 - camChunkZ) * (a.Item3 - camChunkZ);
            float db = (b.Item1 - camChunkX) * (b.Item1 - camChunkX) + (b.Item2 - camChunkY) * (b.Item2 - camChunkY) + (b.Item3 - camChunkZ) * (b.Item3 - camChunkZ);
            return da.CompareTo(db);
        });
        // Queue near chunks first, then far
        foreach (var key in missingNear.Concat(missingFar))
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

        // Unload chunks no longer needed (enqueue for main-thread disposal)
        var keep = new HashSet<(int, int, int)>();
        // Add a +1 buffer to prevent edge chunks from being unloaded too early
        for (int dx = -RenderDistance - 1; dx <= RenderDistance + 1; dx++)
        for (int dy = -VerticalRenderDistance - 1; dy <= VerticalRenderDistance + 1; dy++)
        for (int dz = -RenderDistance - 1; dz <= RenderDistance + 1; dz++)
        {
            int cx = camChunkX + dx;
            int cy = camChunkY + dy;
            int cz = camChunkZ + dz;
            int dist2 = dx * dx + dy * dy + dz * dz;
            if (dist2 <= (RenderDistance + 1) * (RenderDistance + 1))
                keep.Add((cx, cy, cz));
        }
        foreach (var key in _chunks.Keys.ToList())
        {
            if (!keep.Contains(key))
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

        System.Diagnostics.Debug.WriteLine($"[DEBUG] _chunks.Count={_chunks.Count}, keep.Count={keep.Count}");
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
                Chunk? chunk = null;
                bool isNew = false;
                if (!_chunks.TryGetValue((cx, cy, cz), out chunk))
                {
                    Chunk.GenerateTerrain(cx, cy, cz, _blockManager);
                    chunk = new Chunk(cx, cy, cz, _blockManager);
                    isNew = true;
                }
                if (chunk != null)
                {
                    // Always apply modifications after terrain gen or chunk load
                    if (isNew) ApplyModifications();
                    var mesh = ChunkRenderer.GenerateMeshData(chunk, (cx, cy, cz));
                    lock (_genLock)
                    {
                        _readyQueue.Enqueue((cx, cy, cz, chunk, mesh));
                    }
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
        (Chunk?, (int, int, int)) closest = (null, (0, 0, 0));
        float closestT = float.MaxValue;
        for (float t = 0; t < maxDist; t += 0.1f)
        {
            Vector3 p = origin + dir * t;
            int cx = (int)Math.Floor(p.X / (float)Chunk.SizeX);
            int cy = (int)Math.Floor(p.Y / (float)Chunk.SizeY);
            int cz = (int)Math.Floor(p.Z / (float)Chunk.SizeZ);
            int lx = (int)(p.X - cx * Chunk.SizeX);
            int ly = (int)(p.Y - cy * Chunk.SizeY);
            int lz = (int)(p.Z - cz * Chunk.SizeZ);
            var key = (cx, cy, cz);
            if (!_chunks.ContainsKey(key))
            {
                // Optionally trigger chunk generation if missing
                lock (_genLock)
                {
                    if (!_genQueue.Contains(key))
                        _genQueue.Enqueue(key);
                }
                continue;
            }
            var chunk = _chunks[key];
            if (lx >= 0 && ly >= 0 && lz >= 0 && lx < Chunk.SizeX && ly < Chunk.SizeY && lz < Chunk.SizeZ)
            {
                if (chunk[lx, ly, lz] != BlockType.Air)
                {
                    if (t < closestT)
                    {
                        closest = (chunk, (lx, ly, lz));
                        closestT = t;
                    }
                }
            }
        }
        return closest;
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
