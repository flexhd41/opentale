using OpenTK.Mathematics;

namespace VoxelEngine
{
    public static class FrustumCulling
    {
        // Returns true if the AABB is inside/intersects the frustum
        public static bool IsBoxInFrustum(Matrix4 view, Matrix4 proj, Vector3 min, Vector3 max)
        {
            // Compute the combined matrix
            Matrix4 vp = view * proj;
            // Extract frustum planes (in view space)
            Vector4[] planes = new Vector4[6];
            // Left
            planes[0] = new Vector4(
                vp.M14 + vp.M11,
                vp.M24 + vp.M21,
                vp.M34 + vp.M31,
                vp.M44 + vp.M41);
            // Right
            planes[1] = new Vector4(
                vp.M14 - vp.M11,
                vp.M24 - vp.M21,
                vp.M34 - vp.M31,
                vp.M44 - vp.M41);
            // Bottom
            planes[2] = new Vector4(
                vp.M14 + vp.M12,
                vp.M24 + vp.M22,
                vp.M34 + vp.M32,
                vp.M44 + vp.M42);
            // Top
            planes[3] = new Vector4(
                vp.M14 - vp.M12,
                vp.M24 - vp.M22,
                vp.M34 - vp.M32,
                vp.M44 - vp.M42);
            // Near
            planes[4] = new Vector4(
                vp.M14 + vp.M13,
                vp.M24 + vp.M23,
                vp.M34 + vp.M33,
                vp.M44 + vp.M43);
            // Far
            planes[5] = new Vector4(
                vp.M14 - vp.M13,
                vp.M24 - vp.M23,
                vp.M34 - vp.M33,
                vp.M44 - vp.M43);
            // Normalize planes
            for (int i = 0; i < 6; i++)
            {
                float len = (float)System.Math.Sqrt(planes[i].X * planes[i].X + planes[i].Y * planes[i].Y + planes[i].Z * planes[i].Z);
                planes[i] /= len;
            }
            // Test box against all planes
            for (int i = 0; i < 6; i++)
            {
                Vector3 p = new(
                    planes[i].X >= 0 ? max.X : min.X,
                    planes[i].Y >= 0 ? max.Y : min.Y,
                    planes[i].Z >= 0 ? max.Z : min.Z
                );
                float d = planes[i].X * p.X + planes[i].Y * p.Y + planes[i].Z * p.Z + planes[i].W;
                if (d < 0)
                    return false;
            }
            return true;
        }
    }
}
