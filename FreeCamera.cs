using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace VoxelEngine;

public class FreeCamera
{
    public Vector3 Position { get; private set; }
    public float Pitch { get; private set; }
    public float Yaw { get; private set; }
    public Vector3 Front { get; private set; } = -Vector3.UnitZ;
    public Vector3 Up { get; private set; } = Vector3.UnitY;
    public Vector3 Right { get; private set; } = Vector3.UnitX;
    public float Speed { get; set; } = 10f;
    public float Sensitivity { get; set; } = 0.2f;

    private Vector2 _lastMouse;
    private bool _firstMove = true;

    public FreeCamera(Vector3 position, float pitch = 0, float yaw = -90)
    {
        Position = position;
        Pitch = pitch;
        Yaw = yaw;
        UpdateVectors();
    }

    public void Update(float delta, KeyboardState keyboard, MouseState mouse)
    {
        if (_firstMove)
        {
            _lastMouse = mouse.Position;
            _firstMove = false;
        }
        var mouseDelta = mouse.Position - _lastMouse;
        _lastMouse = mouse.Position;
        Yaw += mouseDelta.X * Sensitivity;
        Pitch -= mouseDelta.Y * Sensitivity;
        Pitch = MathHelper.Clamp(Pitch, -89f, 89f);
        UpdateVectors();
        Vector3 move = Vector3.Zero;
        if (keyboard.IsKeyDown(Keys.W)) move += Front;
        if (keyboard.IsKeyDown(Keys.S)) move -= Front;
        if (keyboard.IsKeyDown(Keys.A)) move -= Right;
        if (keyboard.IsKeyDown(Keys.D)) move += Right;
        if (keyboard.IsKeyDown(Keys.Space)) move += Up;
        if (keyboard.IsKeyDown(Keys.LeftShift)) move -= Up;
        if (move.LengthSquared > 0)
        {
            move = Vector3.Normalize(move);
            Position += move * Speed * delta;
        }
    }

    public Matrix4 GetViewMatrix() => Matrix4.LookAt(Position, Position + Front, Up);

    private void UpdateVectors()
    {
        float pitchRad = MathHelper.DegreesToRadians(Pitch);
        float yawRad = MathHelper.DegreesToRadians(Yaw);
        Front = new Vector3(
            MathF.Cos(pitchRad) * MathF.Cos(yawRad),
            MathF.Sin(pitchRad),
            MathF.Cos(pitchRad) * MathF.Sin(yawRad)
        );
        Front = Vector3.Normalize(Front);
        Right = Vector3.Normalize(Vector3.Cross(Front, Vector3.UnitY));
        Up = Vector3.Normalize(Vector3.Cross(Right, Front));
    }
}
