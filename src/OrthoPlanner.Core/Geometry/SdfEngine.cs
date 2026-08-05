using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OrthoPlanner.Core.Geometry;

/// <summary>
/// Binary voxel solid grid.
/// Solid[i] = 1 → inside/surface, 0 → outside.
/// Uses byte[] (1 byte/voxel) instead of float[] (4 bytes/voxel) → 4× less RAM.
/// At 0.1mm with a ~100M-voxel dental arch grid: ~100MB vs ~400MB per grid.
/// </summary>
public sealed class SdfGrid
{
    public readonly byte[] Solid;
    public readonly int NX, NY, NZ;
    public readonly float OX, OY, OZ, VS;

    public SdfGrid(int nx, int ny, int nz, float ox, float oy, float oz, float vs)
    { NX=nx; NY=ny; NZ=nz; OX=ox; OY=oy; OZ=oz; VS=vs; Solid=new byte[nx*ny*nz]; }

    public int   Idx(int x,int y,int z) => z*NX*NY + y*NX + x;
    public float WorldX(int x) => OX+x*VS;
    public float WorldY(int y) => OY+y*VS;
    public float WorldZ(int z) => OZ+z*VS;

    // Boolean union:      this |= other
    public void UnionWith(SdfGrid o)
    { int n=Solid.Length; for(int i=0;i<n;i++) if(o.Solid[i]>0) Solid[i]=1; }

    // Boolean difference: this &= NOT other
    public void SubtractWith(SdfGrid o)
    { int n=Solid.Length; for(int i=0;i<n;i++) if(o.Solid[i]>0) Solid[i]=0; }
}

public static class SdfOps
{
    /// <summary>
    /// Voxelizes a mesh into a binary SdfGrid, then sphere-dilates by dilationMm.
    /// Pipeline: rasterize triangles → flood-fill inside → BFS sphere dilation.
    /// All O(N) — no per-voxel triangle distance loop.
    /// </summary>
    public static SdfGrid MeshToSdf(
        float[] mesh, float dilationMm,
        float zMin, float zMax,
        float ox, float oy, float oz,
        int nx, int ny, int nz, float vs)
    {
        var grid = new SdfGrid(nx, ny, nz, ox, oy, oz, vs);
        if (mesh == null || mesh.Length < 9) return grid;

        int N = nx*ny*nz;
        // state: 0=unvisited  1=surface  2=outside
        var state = new byte[N];

        // ── 1. Rasterize triangles ─────────────────────────────────────────
        int triCount = mesh.Length / 9;
        object lockObj = new();
        Parallel.For(0, triCount, t =>
        {
            int b = t*9;
            float ax=mesh[b],ay=mesh[b+1],az=mesh[b+2];
            float bx=mesh[b+3],by=mesh[b+4],bz=mesh[b+5];
            float cx=mesh[b+6],cy=mesh[b+7],cz=mesh[b+8];
            float ctz=(az+bz+cz)/3f;
            if(ctz<zMin||ctz>zMax) return;

            float hv=vs*0.5f;
            int x0=C((int)MathF.Floor((MathF.Min(ax,MathF.Min(bx,cx))-ox)/vs)-1,0,nx-1);
            int x1=C((int)MathF.Ceiling((MathF.Max(ax,MathF.Max(bx,cx))-ox)/vs)+1,0,nx-1);
            int y0=C((int)MathF.Floor((MathF.Min(ay,MathF.Min(by,cy))-oy)/vs)-1,0,ny-1);
            int y1=C((int)MathF.Ceiling((MathF.Max(ay,MathF.Max(by,cy))-oy)/vs)+1,0,ny-1);
            int z0=C((int)MathF.Floor((MathF.Min(az,MathF.Min(bz,cz))-oz)/vs)-1,0,nz-1);
            int z1=C((int)MathF.Ceiling((MathF.Max(az,MathF.Max(bz,cz))-oz)/vs)+1,0,nz-1);

            for(int iz=z0;iz<=z1;iz++)
            for(int iy=y0;iy<=y1;iy++)
            for(int ix=x0;ix<=x1;ix++)
            {
                if(TriBoxOverlap(ax,ay,az,bx,by,bz,cx,cy,cz,
                    ox+ix*vs-hv, oy+iy*vs-hv, oz+iz*vs-hv,
                    ox+ix*vs+hv, oy+iy*vs+hv, oz+iz*vs+hv))
                    state[iz*nx*ny+iy*nx+ix]=1;
            }
        });

        // ── 2. Flood-fill outside from 6 boundary faces ───────────────────
        var q = new Queue<int>(256*1024);
        void Seed(int x,int y,int z){ int i=z*nx*ny+y*nx+x; if(state[i]==0){state[i]=2;q.Enqueue(i);} }
        for(int y=0;y<ny;y++) for(int z=0;z<nz;z++){Seed(0,y,z);Seed(nx-1,y,z);}
        for(int x=0;x<nx;x++) for(int z=0;z<nz;z++){Seed(x,0,z);Seed(x,ny-1,z);}
        for(int x=0;x<nx;x++) for(int y=0;y<ny;y++){Seed(x,y,0);Seed(x,y,nz-1);}
        int[] dx6={1,-1,0,0,0,0},dy6={0,0,1,-1,0,0},dz6={0,0,0,0,1,-1};
        while(q.Count>0)
        {
            int idx=q.Dequeue();
            int iz=idx/(nx*ny),rem=idx%(nx*ny),iy=rem/nx,ix=rem%nx;
            for(int d=0;d<6;d++)
            {
                int nx2=ix+dx6[d],ny2=iy+dy6[d],nz2=iz+dz6[d];
                if((uint)nx2>=(uint)nx||(uint)ny2>=(uint)ny||(uint)nz2>=(uint)nz) continue;
                int ni=nz2*nx*ny+ny2*nx+nx2;
                if(state[ni]==0){state[ni]=2;q.Enqueue(ni);}
            }
        }

        // ── 3. Solid = surface (1) or inside (unvisited, 0) ───────────────
        // state==2 → outside, everything else → solid
        for(int i=0;i<N;i++) grid.Solid[i]=(byte)(state[i]==2?0:1);

        // ── 4. Sphere dilation via 26-connected BFS expansion ─────────────
        if(dilationMm > 0f)
        {
            int dilVox = (int)MathF.Ceiling(dilationMm/vs);
            SphereDilate(grid.Solid, nx, ny, nz, dilVox);
        }
        return grid;
    }

    // Expand solid by sphereR voxels using BFS tracking squared Euclidean distance
    // At each step only marks voxels within exact sphere radius (not just L1 shell)
    private static void SphereDilate(byte[] solid, int nx, int ny, int nz, int sphereR)
    {
        int rSq = sphereR*sphereR;
        int N   = solid.Length;

        // dist2[i] = squared voxel distance from nearest original solid voxel
        // Use short[] — max value = sphereR² ≤ 10²+10²+10² = 300 for 1mm@0.1mm
        var dist2 = new short[N];
        Array.Fill(dist2, short.MaxValue);

        // Priority queue via bucketed queues (dist²=0..rSq, rSq+1 buckets)
        var buckets = new Queue<int>[rSq+1];
        for(int i=0;i<=rSq;i++) buckets[i]=new Queue<int>();

        // Seed with all surface-or-inside voxels at dist²=0
        for(int i=0;i<N;i++) if(solid[i]>0){dist2[i]=0; buckets[0].Enqueue(i);}

        int[] ddx={1,-1,0,0,0,0,1,-1,1,-1,0,0,1,-1,1,-1,1,-1,1,-1,0,0,1,-1,1,-1};
        int[] ddy={0,0,1,-1,0,0,1,-1,-1,1,1,-1,0,0,0,0,1,-1,1,-1,1,-1,1,-1,-1,1};
        int[] ddz={0,0,0,0,1,-1,0,0,0,0,1,-1,1,-1,1,-1,-1,1,-1,1,1,-1,-1,1,1,-1};
        // Pre-compute the squared distances for each direction (26-connected)
        int[] ddSq=new int[26];
        for(int d=0;d<26;d++) ddSq[d]=ddx[d]*ddx[d]+ddy[d]*ddy[d]+ddz[d]*ddz[d];

        for(int bucket=0;bucket<=rSq;bucket++)
        {
            var bq=buckets[bucket];
            while(bq.Count>0)
            {
                int idx=bq.Dequeue();
                if(dist2[idx]!=(short)bucket) continue; // stale entry
                int iz=idx/(nx*ny),rem=idx%(nx*ny),iy=rem/nx,ix=rem%nx;
                for(int d=0;d<26;d++)
                {
                    int nx2=ix+ddx[d],ny2=iy+ddy[d],nz2=iz+ddz[d];
                    if((uint)nx2>=(uint)nx||(uint)ny2>=(uint)ny||(uint)nz2>=(uint)nz) continue;
                    int nd=dist2[idx]+ddSq[d]; // approximate: propagate dist²+step²
                    if(nd>rSq) continue;
                    int ni=nz2*nx*ny+ny2*nx+nx2;
                    if(nd<dist2[ni]){dist2[ni]=(short)nd; buckets[nd].Enqueue(ni);}
                }
            }
        }

        // Mark all voxels within sphere as solid
        for(int i=0;i<N;i++) if(dist2[i]<=rSq) solid[i]=1;
    }

    // ── Triangle-AABB overlap test (Möller 1997) ──────────────────────────
    private static bool TriBoxOverlap(
        float ax,float ay,float az,float bx,float by,float bz,float cx,float cy,float cz,
        float x0,float y0,float z0,float x1,float y1,float z1)
    {
        float hx=(x1-x0)*0.5f,hy=(y1-y0)*0.5f,hz=(z1-z0)*0.5f;
        float mx=x0+hx,my=y0+hy,mz=z0+hz;
        ax-=mx;ay-=my;az-=mz; bx-=mx;by-=my;bz-=mz; cx-=mx;cy-=my;cz-=mz;
        float e0x=bx-ax,e0y=by-ay,e0z=bz-az;
        float e1x=cx-bx,e1y=cy-by,e1z=cz-bz;
        float e2x=ax-cx,e2y=ay-cy,e2z=az-cz;
        if(MathF.Max(ax,MathF.Max(bx,cx))<-hx||MathF.Min(ax,MathF.Min(bx,cx))>hx) return false;
        if(MathF.Max(ay,MathF.Max(by,cy))<-hy||MathF.Min(ay,MathF.Min(by,cy))>hy) return false;
        if(MathF.Max(az,MathF.Max(bz,cz))<-hz||MathF.Min(az,MathF.Min(bz,cz))>hz) return false;
        float nx2=e0y*e1z-e0z*e1y,ny2=e0z*e1x-e0x*e1z,nz2=e0x*e1y-e0y*e1x;
        float planeD=nx2*ax+ny2*ay+nz2*az;
        float r=hx*MathF.Abs(nx2)+hy*MathF.Abs(ny2)+hz*MathF.Abs(nz2);
        if(MathF.Abs(planeD)>r) return false;
        if(!AT(e0z,-e0y,ay,az,cy,cz,hy,hz)) return false;
        if(!AT(-e0z,e0x,ax,az,cx,cz,hx,hz)) return false;
        if(!AT(e0y,-e0x,ax,ay,cx,cy,hx,hy)) return false;
        if(!AT(e1z,-e1y,ay,az,by,bz,hy,hz)) return false;
        if(!AT(-e1z,e1x,ax,az,bx,bz,hx,hz)) return false;
        if(!AT(e1y,-e1x,ax,ay,bx,by,hx,hy)) return false;
        if(!AT(e2z,-e2y,ay,az,by,bz,hy,hz)) return false;
        if(!AT(-e2z,e2x,ax,az,bx,bz,hx,hz)) return false;
        if(!AT(e2y,-e2x,ax,ay,bx,by,hx,hy)) return false;
        return true;
    }
    private static bool AT(float a,float b,float p0,float q0,float p1,float q1,float ha,float hb)
    { float pa=a*p0+b*q0,pb=a*p1+b*q1,mn=MathF.Min(pa,pb),mx=MathF.Max(pa,pb),r2=ha*MathF.Abs(a)+hb*MathF.Abs(b); return !(mn>r2||mx<-r2); }

    private static int C(int v,int lo,int hi)=>v<lo?lo:v>hi?hi:v;

    // ── Marching Cubes on binary SdfGrid ─────────────────────────────────
    // On-the-fly SDF: Solid=1 → -0.5 (inside), Solid=0 → +0.5 (outside)
    // MC places iso=0 surface at exact voxel boundary → 0.05mm accuracy at 0.1mm voxels
    public static float[] MarchingCubes(SdfGrid g, float isoValue=0f)
    {
        var tris=new List<float>(1<<20);
        int nx=g.NX,ny=g.NY,nz=g.NZ;

        Span<float> v=stackalloc float[8];
        Span<(float x,float y,float z)> p=stackalloc (float,float,float)[8];
        Span<(float x,float y,float z)> e=stackalloc (float,float,float)[12];

        for(int iz=0;iz<nz-1;iz++)
        for(int iy=0;iy<ny-1;iy++)
        for(int ix=0;ix<nx-1;ix++)
        {
            v[0]=g.Solid[g.Idx(ix,  iy,  iz  )]>0?-0.5f:0.5f;
            v[1]=g.Solid[g.Idx(ix+1,iy,  iz  )]>0?-0.5f:0.5f;
            v[2]=g.Solid[g.Idx(ix+1,iy+1,iz  )]>0?-0.5f:0.5f;
            v[3]=g.Solid[g.Idx(ix,  iy+1,iz  )]>0?-0.5f:0.5f;
            v[4]=g.Solid[g.Idx(ix,  iy,  iz+1)]>0?-0.5f:0.5f;
            v[5]=g.Solid[g.Idx(ix+1,iy,  iz+1)]>0?-0.5f:0.5f;
            v[6]=g.Solid[g.Idx(ix+1,iy+1,iz+1)]>0?-0.5f:0.5f;
            v[7]=g.Solid[g.Idx(ix,  iy+1,iz+1)]>0?-0.5f:0.5f;

            int ci=0; for(int k=0;k<8;k++) if(v[k]<isoValue) ci|=(1<<k);
            if(ci==0||ci==255) continue;

            float wx0=g.WorldX(ix),wx1=g.WorldX(ix+1);
            float wy0=g.WorldY(iy),wy1=g.WorldY(iy+1);
            float wz0=g.WorldZ(iz),wz1=g.WorldZ(iz+1);
            p[0]=(wx0,wy0,wz0);p[1]=(wx1,wy0,wz0);p[2]=(wx1,wy1,wz0);p[3]=(wx0,wy1,wz0);
            p[4]=(wx0,wy0,wz1);p[5]=(wx1,wy0,wz1);p[6]=(wx1,wy1,wz1);p[7]=(wx0,wy1,wz1);

            int em = OrthoPlanner.Core.Imaging.MarchingCubes.GetEdgeFlags(ci);
            if((em&   1)!=0)e[0] =L(p[0],v[0],p[1],v[1],isoValue);
            if((em&   2)!=0)e[1] =L(p[1],v[1],p[2],v[2],isoValue);
            if((em&   4)!=0)e[2] =L(p[2],v[2],p[3],v[3],isoValue);
            if((em&   8)!=0)e[3] =L(p[3],v[3],p[0],v[0],isoValue);
            if((em&  16)!=0)e[4] =L(p[4],v[4],p[5],v[5],isoValue);
            if((em&  32)!=0)e[5] =L(p[5],v[5],p[6],v[6],isoValue);
            if((em&  64)!=0)e[6] =L(p[6],v[6],p[7],v[7],isoValue);
            if((em& 128)!=0)e[7] =L(p[7],v[7],p[4],v[4],isoValue);
            if((em& 256)!=0)e[8] =L(p[0],v[0],p[4],v[4],isoValue);
            if((em& 512)!=0)e[9] =L(p[1],v[1],p[5],v[5],isoValue);
            if((em&1024)!=0)e[10]=L(p[2],v[2],p[6],v[6],isoValue);
            if((em&2048)!=0)e[11]=L(p[3],v[3],p[7],v[7],isoValue);

            var tt = OrthoPlanner.Core.Imaging.MarchingCubes.GetTriangles(ci);
            for(int k=0;tt[k]!=-1;k+=3)
            {
                var a=e[tt[k]];var b=e[tt[k+1]];var c=e[tt[k+2]];
                tris.Add(a.x);tris.Add(a.y);tris.Add(a.z);
                tris.Add(b.x);tris.Add(b.y);tris.Add(b.z);
                tris.Add(c.x);tris.Add(c.y);tris.Add(c.z);
            }
        }
        return tris.ToArray();
    }

    private static (float x,float y,float z) L(
        (float x,float y,float z) a,float va,
        (float x,float y,float z) b,float vb,float iso)
    {
        if(MathF.Abs(va-vb)<1e-9f)return a;
        float t=(iso-va)/(vb-va);
        return(a.x+t*(b.x-a.x),a.y+t*(b.y-a.y),a.z+t*(b.z-a.z));
    }
}
