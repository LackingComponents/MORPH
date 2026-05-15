using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OrthoPlanner.Core.Geometry;

/// <summary>
/// Signed Distance Field voxelization + Marching Cubes.
/// Works on ANY triangle soup regardless of topology (open, non-manifold, CT meshes).
/// 
/// SDF convention: negative = inside, positive = outside, 0 = surface.
/// "Inside" = within dilationMm of the mesh surface AND within [zMin,zMax].
/// 
/// Boolean ops:
///   Union     = min(SDF_A, SDF_B)
///   Intersect = max(SDF_A, SDF_B)
///   Difference= max(SDF_A, -SDF_B)
/// </summary>
public sealed class SdfGrid
{
    public readonly float[] Data;  // [iz*NX*NY + iy*NX + ix], + = outside, - = inside
    public readonly int NX, NY, NZ;
    public readonly float OX, OY, OZ;   // world origin (mm) of voxel [0,0,0]
    public readonly float VS;           // voxel size (mm)

    public SdfGrid(int nx, int ny, int nz, float ox, float oy, float oz, float vs)
    {
        NX = nx; NY = ny; NZ = nz;
        OX = ox; OY = oy; OZ = oz; VS = vs;
        Data = new float[nx * ny * nz];
        Array.Fill(Data, float.MaxValue);
    }

    public int Idx(int ix, int iy, int iz) => iz * NX * NY + iy * NX + ix;

    public float WorldX(int ix) => OX + ix * VS;
    public float WorldY(int iy) => OY + iy * VS;
    public float WorldZ(int iz) => OZ + iz * VS;

    // In-place boolean union: this = min(this, other)
    public void UnionWith(SdfGrid other)
    {
        int n = Data.Length;
        for (int i = 0; i < n; i++) Data[i] = MathF.Min(Data[i], other.Data[i]);
    }

    // In-place boolean difference: this = max(this, -other)
    public void SubtractWith(SdfGrid other)
    {
        int n = Data.Length;
        for (int i = 0; i < n; i++) Data[i] = MathF.Max(Data[i], -other.Data[i]);
    }

    // Sample with trilinear interpolation (returns float.MaxValue outside bounds)
    public float Sample(float wx, float wy, float wz)
    {
        float fx = (wx - OX) / VS, fy = (wy - OY) / VS, fz = (wz - OZ) / VS;
        int ix = (int)fx, iy = (int)fy, iz = (int)fz;
        if (ix < 0 || iy < 0 || iz < 0 || ix >= NX-1 || iy >= NY-1 || iz >= NZ-1) return float.MaxValue;
        float tx = fx - ix, ty = fy - iy, tz = fz - iz;
        float c000 = Data[Idx(ix,iy,iz)],     c100 = Data[Idx(ix+1,iy,iz)];
        float c010 = Data[Idx(ix,iy+1,iz)],   c110 = Data[Idx(ix+1,iy+1,iz)];
        float c001 = Data[Idx(ix,iy,iz+1)],   c101 = Data[Idx(ix+1,iy,iz+1)];
        float c011 = Data[Idx(ix,iy+1,iz+1)], c111 = Data[Idx(ix+1,iy+1,iz+1)];
        return (1-tz)*((1-ty)*((1-tx)*c000+tx*c100)+ty*((1-tx)*c010+tx*c110))
              +tz   *((1-ty)*((1-tx)*c001+tx*c101)+ty*((1-tx)*c011+tx*c111));
    }
}

public static class SdfOps
{
    // ── Voxelize a triangle soup into an SDF grid ───────────────────────────
    // For each voxel: SDF = (distance to nearest triangle) - dilationMm.
    // Voxels with Z outside [zMin,zMax] are forced to +MAX (outside).
    // Build an accelerator BVH over triangle centroids using the existing KdTree.
    public static SdfGrid MeshToSdf(
        float[] mesh,           // flat triangle soup: x0y0z0 x1y1z1 x2y2z2 ...
        float dilationMm,       // SDF offset: negative = expand surface outward
        float zMin, float zMax, // world-space Z clip range
        // Grid params
        float ox, float oy, float oz,
        int nx, int ny, int nz,
        float vs)
    {
        var grid = new SdfGrid(nx, ny, nz, ox, oy, oz, vs);

        int triCount = mesh.Length / 9;
        if (triCount == 0) return grid;

        // Build KdTree on triangle centroids for approximate nearest-tri lookup
        var centroids = new float[triCount * 3];
        for (int t = 0; t < triCount; t++)
        {
            int b = t * 9;
            centroids[t*3]   = (mesh[b]+mesh[b+3]+mesh[b+6]) / 3f;
            centroids[t*3+1] = (mesh[b+1]+mesh[b+4]+mesh[b+7]) / 3f;
            centroids[t*3+2] = (mesh[b+2]+mesh[b+5]+mesh[b+8]) / 3f;
        }
        var kd = new KdTree();
        kd.Build(centroids, triCount);

        // Precompute per-triangle max-edge length for K-nearest radius expansion
        var triMaxEdge = new float[triCount];
        for (int t = 0; t < triCount; t++)
        {
            int b = t * 9;
            float dx, dy, dz, e;
            dx=mesh[b+3]-mesh[b]; dy=mesh[b+4]-mesh[b+1]; dz=mesh[b+5]-mesh[b+2]; e=MathF.Sqrt(dx*dx+dy*dy+dz*dz);
            float maxE = e;
            dx=mesh[b+6]-mesh[b+3]; dy=mesh[b+7]-mesh[b+4]; dz=mesh[b+8]-mesh[b+5]; e=MathF.Sqrt(dx*dx+dy*dy+dz*dz);
            if (e>maxE) maxE=e;
            dx=mesh[b]-mesh[b+6]; dy=mesh[b+1]-mesh[b+7]; dz=mesh[b+2]-mesh[b+8]; e=MathF.Sqrt(dx*dx+dy*dy+dz*dz);
            if (e>maxE) maxE=e;
            triMaxEdge[t] = maxE;
        }

        // Parallel over Z slices
        Parallel.For(0, nz, iz =>
        {
            float wz = grid.WorldZ(iz);
            bool zOutside = (wz < zMin || wz > zMax);

            for (int iy = 0; iy < ny; iy++)
            {
                float wy = grid.WorldY(iy);
                for (int ix = 0; ix < nx; ix++)
                {
                    int cell = grid.Idx(ix, iy, iz);

                    if (zOutside) { grid.Data[cell] = float.MaxValue; continue; }

                    float wx = grid.WorldX(ix);

                    // Find nearest centroid, then check nearby triangles
                    // Use K-nearest by expanding radius until we find a stable min
                    float bestDistSq = float.MaxValue;
                    float searchR = vs * 3f; // start radius
                    int iterations = 0;
                    while (iterations++ < 8)
                    {
                        var (idx, dSq) = kd.FindNearest(wx, wy, wz);
                        if (idx < 0) break;
                        float triRadius = triMaxEdge[idx];
                        // Candidate distance = sqrt(dSq to centroid) + triRadius → upper bound on true dist
                        float candidateDist = MathF.Sqrt(dSq) + triRadius;

                        // Find the exact distance by checking all tris within candidateDist of voxel
                        bestDistSq = NearestTriDist(mesh, triCount, wx, wy, wz, candidateDist * 1.5f);
                        break; // one pass is enough given centroid + edge heuristic
                    }

                    // Final: SDF = distance - dilation (negative = inside dilated volume)
                    float dist = MathF.Sqrt(MathF.Max(0, bestDistSq));
                    grid.Data[cell] = dist - dilationMm;
                }
            }
        });

        return grid;
    }

    // Brute-force nearest triangle within maxDist — fast enough after centroid pre-filter
    private static float NearestTriDist(float[] mesh, int triCount, float px, float py, float pz, float maxDist)
    {
        float best = maxDist * maxDist;
        float maxDistSq = maxDist * maxDist;

        for (int t = 0; t < triCount; t++)
        {
            int b = t * 9;
            float ax=mesh[b], ay=mesh[b+1], az=mesh[b+2];
            float bx=mesh[b+3], by=mesh[b+4], bz=mesh[b+5];
            float cx=mesh[b+6], cy=mesh[b+7], cz=mesh[b+8];

            // Quick centroid pre-reject
            float cntx=(ax+bx+cx)/3f, cnty=(ay+by+cy)/3f, cntz=(az+bz+cz)/3f;
            float cdx=px-cntx, cdy=py-cnty, cdz=pz-cntz;
            if (cdx*cdx+cdy*cdy+cdz*cdz > maxDistSq*4f) continue;

            float d = PointToTriangleDistSq(px,py,pz, ax,ay,az, bx,by,bz, cx,cy,cz);
            if (d < best) best = d;
        }
        return best;
    }

    // Point-to-triangle squared distance (Christer Ericson formula)
    public static float PointToTriangleDistSq(
        float px, float py, float pz,
        float ax, float ay, float az,
        float bx, float by, float bz,
        float cx, float cy, float cz)
    {
        float abx=bx-ax, aby=by-ay, abz=bz-az;
        float acx=cx-ax, acy=cy-ay, acz=cz-az;
        float apx=px-ax, apy=py-ay, apz=pz-az;

        float d1=abx*apx+aby*apy+abz*apz;
        float d2=acx*apx+acy*apy+acz*apz;
        if (d1<=0 && d2<=0) return apx*apx+apy*apy+apz*apz; // vertex A

        float bpx=px-bx, bpy=py-by, bpz=pz-bz;
        float d3=abx*bpx+aby*bpy+abz*bpz;
        float d4=acx*bpx+acy*bpy+acz*bpz;
        if (d3>=0 && d4<=d3) return bpx*bpx+bpy*bpy+bpz*bpz; // vertex B

        float cpx=px-cx, cpy=py-cy, cpz=pz-cz;
        float d5=abx*cpx+aby*cpy+abz*cpz;
        float d6=acx*cpx+acy*cpy+acz*cpz;
        if (d6>=0 && d5<=d6) return cpx*cpx+cpy*cpy+cpz*cpz; // vertex C

        float vc=d1*d4-d3*d2;
        if (vc<=0 && d1>=0 && d3<=0)
        { float v2=d1/(d1-d3); float rx=apx-v2*abx, ry=apy-v2*aby, rz=apz-v2*abz; return rx*rx+ry*ry+rz*rz; }

        float vb=d5*d2-d1*d6;
        if (vb<=0 && d2>=0 && d6<=0)
        { float w2=d2/(d2-d6); float rx=apx-w2*acx, ry=apy-w2*acy, rz=apz-w2*acz; return rx*rx+ry*ry+rz*rz; }

        float va=d3*d6-d5*d4;
        if (va<=0 && (d4-d3)>=0 && (d5-d6)>=0)
        { float w2=(d4-d3)/((d4-d3)+(d5-d6)); float rx=bpx+w2*(cx-bx), ry=bpy+w2*(cy-by), rz=bpz+w2*(cz-bz); return rx*rx+ry*ry+rz*rz; }

        float denom=1f/(va+vb+vc);
        float vf=vb*denom, wf=vc*denom;
        float qx=apx-(vf*abx+wf*acx), qy=apy-(vf*aby+wf*acy), qz=apz-(vf*abz+wf*acz);
        return qx*qx+qy*qy+qz*qz;
    }

    // ── Build SDF analytically for the horseshoe (closed box-sweep solid) ──
    // Horseshoe is a flat triangle soup we already generate — just voxelize it.

    // ── Marching Cubes ──────────────────────────────────────────────────────
    public static float[] MarchingCubes(SdfGrid grid, float isoValue = 0f)
    {
        var tris = new List<float>(1 << 20);
        int nx=grid.NX, ny=grid.NY, nz=grid.NZ;

        for (int iz = 0; iz < nz-1; iz++)
        for (int iy = 0; iy < ny-1; iy++)
        for (int ix = 0; ix < nx-1; ix++)
        {
            // 8 cube corners
            float[] v = new float[8];
            v[0]=grid.Data[grid.Idx(ix,  iy,  iz  )];
            v[1]=grid.Data[grid.Idx(ix+1,iy,  iz  )];
            v[2]=grid.Data[grid.Idx(ix+1,iy+1,iz  )];
            v[3]=grid.Data[grid.Idx(ix,  iy+1,iz  )];
            v[4]=grid.Data[grid.Idx(ix,  iy,  iz+1)];
            v[5]=grid.Data[grid.Idx(ix+1,iy,  iz+1)];
            v[6]=grid.Data[grid.Idx(ix+1,iy+1,iz+1)];
            v[7]=grid.Data[grid.Idx(ix,  iy+1,iz+1)];

            // Skip if any corner is +MAX (outside grid / z-clipped)
            bool hasMax = false;
            for (int k=0;k<8;k++) if (v[k]==float.MaxValue){hasMax=true;break;}
            if (hasMax) continue;

            int cubeIdx = 0;
            for (int k=0;k<8;k++) if (v[k]<isoValue) cubeIdx|=(1<<k);
            if (cubeIdx==0||cubeIdx==255) continue;

            // World positions of the 8 corners
            float wx0=grid.WorldX(ix),   wx1=grid.WorldX(ix+1);
            float wy0=grid.WorldY(iy),   wy1=grid.WorldY(iy+1);
            float wz0=grid.WorldZ(iz),   wz1=grid.WorldZ(iz+1);
            (float x,float y,float z)[] p =
            {
                (wx0,wy0,wz0),(wx1,wy0,wz0),(wx1,wy1,wz0),(wx0,wy1,wz0),
                (wx0,wy0,wz1),(wx1,wy0,wz1),(wx1,wy1,wz1),(wx0,wy1,wz1)
            };

            // Interpolated edge vertices
            var e = new (float x,float y,float z)[12];
            int edgeMask = MarchingCubesTables.EdgeTable[cubeIdx];
            if((edgeMask&   1)!=0) e[0] =Lerp(p[0],v[0],p[1],v[1],isoValue);
            if((edgeMask&   2)!=0) e[1] =Lerp(p[1],v[1],p[2],v[2],isoValue);
            if((edgeMask&   4)!=0) e[2] =Lerp(p[2],v[2],p[3],v[3],isoValue);
            if((edgeMask&   8)!=0) e[3] =Lerp(p[3],v[3],p[0],v[0],isoValue);
            if((edgeMask&  16)!=0) e[4] =Lerp(p[4],v[4],p[5],v[5],isoValue);
            if((edgeMask&  32)!=0) e[5] =Lerp(p[5],v[5],p[6],v[6],isoValue);
            if((edgeMask&  64)!=0) e[6] =Lerp(p[6],v[6],p[7],v[7],isoValue);
            if((edgeMask& 128)!=0) e[7] =Lerp(p[7],v[7],p[4],v[4],isoValue);
            if((edgeMask& 256)!=0) e[8] =Lerp(p[0],v[0],p[4],v[4],isoValue);
            if((edgeMask& 512)!=0) e[9] =Lerp(p[1],v[1],p[5],v[5],isoValue);
            if((edgeMask&1024)!=0) e[10]=Lerp(p[2],v[2],p[6],v[6],isoValue);
            if((edgeMask&2048)!=0) e[11]=Lerp(p[3],v[3],p[7],v[7],isoValue);

            int[] triTable = MarchingCubesTables.TriTable[cubeIdx];
            for (int k=0; triTable[k]!=-1; k+=3)
            {
                var a=e[triTable[k]];
                var b=e[triTable[k+1]];
                var c=e[triTable[k+2]];
                tris.Add(a.x);tris.Add(a.y);tris.Add(a.z);
                tris.Add(b.x);tris.Add(b.y);tris.Add(b.z);
                tris.Add(c.x);tris.Add(c.y);tris.Add(c.z);
            }
        }
        return tris.ToArray();
    }

    private static (float x,float y,float z) Lerp(
        (float x,float y,float z) a, float va,
        (float x,float y,float z) b, float vb,
        float iso)
    {
        if (MathF.Abs(va-vb)<1e-9f) return a;
        float t=(iso-va)/(vb-va);
        return (a.x+t*(b.x-a.x), a.y+t*(b.y-a.y), a.z+t*(b.z-a.z));
    }
}
