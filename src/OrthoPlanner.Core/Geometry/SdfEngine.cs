using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OrthoPlanner.Core.Geometry;

// ── Voxel grid ────────────────────────────────────────────────────────────────
// state[idx]: 0=unvisited  1=surface  2=outside
// After processing: 0=inside  1=surface  2=outside
public sealed class SdfGrid
{
    public readonly float[] Data;   // signed distance (mm): - = inside, + = outside
    public readonly int NX, NY, NZ;
    public readonly float OX, OY, OZ, VS;

    public SdfGrid(int nx, int ny, int nz, float ox, float oy, float oz, float vs)
    { NX=nx; NY=ny; NZ=nz; OX=ox; OY=oy; OZ=oz; VS=vs; Data=new float[nx*ny*nz]; Array.Fill(Data, float.MaxValue); }

    public int  Idx(int x,int y,int z) => z*NX*NY + y*NX + x;
    public float WorldX(int x) => OX+x*VS;
    public float WorldY(int y) => OY+y*VS;
    public float WorldZ(int z) => OZ+z*VS;

    public void UnionWith   (SdfGrid o){ int n=Data.Length; for(int i=0;i<n;i++) Data[i]=MathF.Min(Data[i],o.Data[i]); }
    public void SubtractWith(SdfGrid o){ int n=Data.Length; for(int i=0;i<n;i++) Data[i]=MathF.Max(Data[i],-o.Data[i]); }
}

public static class SdfOps
{
    // ── Public entry: mesh → SDF grid ────────────────────────────────────────
    public static SdfGrid MeshToSdf(
        float[] mesh, float dilationMm,
        float zMin, float zMax,
        float ox, float oy, float oz,
        int nx, int ny, int nz, float vs)
    {
        int N = nx*ny*nz;
        // state: 0=unvisited, 1=surface, 2=outside
        var state = new byte[N];

        // 1. Rasterize triangles → mark surface voxels
        int triCount = mesh.Length / 9;
        Parallel.For(0, triCount, t =>
        {
            int b = t*9;
            float ax=mesh[b],   ay=mesh[b+1], az=mesh[b+2];
            float bx=mesh[b+3], by=mesh[b+4], bz=mesh[b+5];
            float cx=mesh[b+6], cy=mesh[b+7], cz=mesh[b+8];

            // Triangle centroid Z clip
            float ctz=(az+bz+cz)/3f;
            if(ctz<zMin||ctz>zMax) return;

            // AABB in voxel space
            int x0=Clamp((int)MathF.Floor((MathF.Min(ax,MathF.Min(bx,cx))-ox)/vs)-1,0,nx-1);
            int x1=Clamp((int)MathF.Ceiling((MathF.Max(ax,MathF.Max(bx,cx))-ox)/vs)+1,0,nx-1);
            int y0=Clamp((int)MathF.Floor((MathF.Min(ay,MathF.Min(by,cy))-oy)/vs)-1,0,ny-1);
            int y1=Clamp((int)MathF.Ceiling((MathF.Max(ay,MathF.Max(by,cy))-oy)/vs)+1,0,ny-1);
            int z0=Clamp((int)MathF.Floor((MathF.Min(az,MathF.Min(bz,cz))-oz)/vs)-1,0,nz-1);
            int z1=Clamp((int)MathF.Ceiling((MathF.Max(az,MathF.Max(bz,cz))-oz)/vs)+1,0,nz-1);

            float hv = vs*0.5f;
            for(int iz=z0;iz<=z1;iz++)
            for(int iy=y0;iy<=y1;iy++)
            for(int ix=x0;ix<=x1;ix++)
            {
                // Voxel center
                float wx=ox+ix*vs, wy=oy+iy*vs, wz=oz+iz*vs;
                if(TriBoxOverlap(ax,ay,az,bx,by,bz,cx,cy,cz, wx-hv,wy-hv,wz-hv, wx+hv,wy+hv,wz+hv))
                    state[iz*nx*ny+iy*nx+ix]=1;
            }
        });

        // 2. Flood-fill from all 6 boundary faces to mark outside
        FloodFillOutside(state, nx, ny, nz);

        // 3. Saito exact 3D squared-distance transform from surface (state==1) voxels
        //    sqDist[i] = squared voxel distance to nearest surface voxel
        var sqDist = DistanceTransform(state, nx, ny, nz);

        // 4. Build signed SDF:  inside=negative, outside=positive  (in mm)
        //    SDF = sign * sqrt(sqDist) * vs - dilationMm
        //    Marching Cubes extracts iso=0, so voxels where SDF<0 are "inside dilated solid"
        var grid = new SdfGrid(nx, ny, nz, ox, oy, oz, vs);
        for(int i=0;i<N;i++)
        {
            float dist = MathF.Sqrt(sqDist[i]) * vs;          // mm to nearest surface
            float sign = (state[i]==2) ? 1f : -1f;            // outside=+, inside/surface=-
            grid.Data[i] = sign*dist - dilationMm;            // negative = inside dilated solid
        }
        return grid;
    }

    // ── Flood-fill from all boundary voxels (6 faces) ─────────────────────────
    private static void FloodFillOutside(byte[] state, int nx, int ny, int nz)
    {
        var queue = new Queue<int>(128*1024);
        void Seed(int x, int y, int z)
        {
            int idx = z*nx*ny+y*nx+x;
            if(state[idx]==0){state[idx]=2; queue.Enqueue(idx);}
        }
        // All 6 faces of the bounding box
        for(int y=0;y<ny;y++) for(int z=0;z<nz;z++){Seed(0,y,z);Seed(nx-1,y,z);}
        for(int x=0;x<nx;x++) for(int z=0;z<nz;z++){Seed(x,0,z);Seed(x,ny-1,z);}
        for(int x=0;x<nx;x++) for(int y=0;y<ny;y++){Seed(x,y,0);Seed(x,y,nz-1);}

        int[] dx={1,-1,0,0,0,0}, dy={0,0,1,-1,0,0}, dz={0,0,0,0,1,-1};
        while(queue.Count>0)
        {
            int idx=queue.Dequeue();
            int iz=idx/(nx*ny), rem=idx%(nx*ny), iy=rem/nx, ix=rem%nx;
            for(int d=0;d<6;d++)
            {
                int nx2=ix+dx[d], ny2=iy+dy[d], nz2=iz+dz[d];
                if(nx2<0||ny2<0||nz2<0||nx2>=nx||ny2>=ny||nz2>=nz) continue;
                int ni=nz2*nx*ny+ny2*nx+nx2;
                if(state[ni]==0){state[ni]=2; queue.Enqueue(ni);}
            }
        }
    }

    // ── Saito 3D exact Euclidean squared distance transform ───────────────────
    // Source: Saito & Toriwaki (1994), O(N) complexity
    private static int[] DistanceTransform(byte[] surface, int nx, int ny, int nz)
    {
        const int INF = int.MaxValue/2;
        int N=nx*ny*nz;
        var sq = new int[N];

        // Phase 1: 1D transform along X for each (y,z)
        Parallel.For(0, nz, iz =>
        {
            for(int iy=0;iy<ny;iy++)
            {
                int[] g=new int[nx];
                for(int ix=0;ix<nx;ix++) g[ix]=(surface[iz*nx*ny+iy*nx+ix]==1)?0:INF;
                // forward pass
                if(g[0]==INF) g[0]=INF; 
                for(int ix=1;ix<nx;ix++) if(g[ix-1]<INF) g[ix]=Math.Min(g[ix],g[ix-1]+1);  // not squared yet — just 1D
                for(int ix=nx-2;ix>=0;ix--) if(g[ix+1]<INF) g[ix]=Math.Min(g[ix],g[ix+1]+1);
                // square
                for(int ix=0;ix<nx;ix++){ sq[iz*nx*ny+iy*nx+ix]=(g[ix]==INF)?INF:g[ix]*g[ix]; }
            }
        });

        // Phase 2: 2D transform along Y for each (x,z)
        var tmp = new int[N];
        Parallel.For(0, nz, iz =>
        {
            var s=new int[ny]; var t=new int[ny]; var q=new int[ny];
            for(int ix=0;ix<nx;ix++)
            {
                int sp=0,w;
                s[0]=0; t[0]=0; q[0]=0;
                for(int iy=1;iy<ny;iy++)
                {
                    int gi=sq[iz*nx*ny+iy*nx+ix];
                    while(sp>0 && F2(t[sp],s[sp],sq[iz*nx*ny+s[sp]*nx+ix])>F2(t[sp],iy,gi)) sp--;

                    sp++;
                    s[sp]=iy; t[sp]=(sp==0)?0:(int)MathF.Floor((gi-(sq[iz*nx*ny+s[sp-1]*nx+ix])+(float)(iy*iy-s[sp-1]*s[sp-1]))/(2f*(iy-s[sp-1])))+1;
                    q[sp]=gi;
                }
                for(int iy=ny-1;iy>=0;iy--)
                {
                    while(sp>0&&t[sp]>iy) sp--;
                    int d=iy-s[sp]; 
                    tmp[iz*nx*ny+iy*nx+ix]=sq[iz*nx*ny+s[sp]*nx+ix]+d*d;
                }
            }
        });
        Array.Copy(tmp, sq, N);

        // Phase 3: 3D transform along Z for each (x,y)
        Parallel.For(0, ny, iy =>
        {
            var s=new int[nz]; var t=new int[nz];
            for(int ix=0;ix<nx;ix++)
            {
                int sp=0;
                s[0]=0; t[0]=0;
                for(int iz=1;iz<nz;iz++)
                {
                    int gi=sq[iz*nx*ny+iy*nx+ix];
                    while(sp>0 && F2(t[sp],s[sp],sq[s[sp]*nx*ny+iy*nx+ix])>F2(t[sp],iz,gi)) sp--;

                    sp++;
                    s[sp]=iz; t[sp]=(sp==0)?0:(int)MathF.Floor((gi-sq[s[sp-1]*nx*ny+iy*nx+ix]+(float)(iz*iz-s[sp-1]*s[sp-1]))/(2f*(iz-s[sp-1])))+1;
                }
                for(int iz=nz-1;iz>=0;iz--)
                {
                    while(sp>0&&t[sp]>iz) sp--;
                    int d=iz-s[sp];
                    tmp[iz*nx*ny+iy*nx+ix]=sq[s[sp]*nx*ny+iy*nx+ix]+d*d;
                }
            }
        });
        return tmp;
    }

    private static int F2(int t, int si, int gi) => (t-si)*(t-si)+gi;
    private static int Clamp(int v, int lo, int hi) => v<lo?lo:v>hi?hi:v;

    // ── Triangle-AABB overlap (Möller 1997, SAT) ──────────────────────────────
    private static bool TriBoxOverlap(
        float ax,float ay,float az, float bx,float by,float bz, float cx,float cy,float cz,
        float x0,float y0,float z0, float x1,float y1,float z1)
    {
        // Translate triangle to box-centered frame
        float hx=(x1-x0)*0.5f, hy=(y1-y0)*0.5f, hz=(z1-z0)*0.5f;
        float mx=x0+hx, my=y0+hy, mz=z0+hz;
        ax-=mx; ay-=my; az-=mz;
        bx-=mx; by-=my; bz-=mz;
        cx-=mx; cy-=my; cz-=cz; cx-=mx; cy-=my; cz=(cz+mz)-mz; // reset
        // redo properly
        ax-=(x0+hx)-mx; // already done above
        // Just use clean names
        float v0x=ax,v0y=ay,v0z=az;
        float v1x=bx,v1y=by,v1z=bz;
        float v2x=cx,v2y=cy,v2z=cz;

        // Recompute cleanly
        v0x=ax; v0y=ay; v0z=az;
        v1x=bx; v1y=by; v1z=bz;
        v2x=cx; v2y=cy; v2z=cz;

        float e0x=v1x-v0x,e0y=v1y-v0y,e0z=v1z-v0z;
        float e1x=v2x-v1x,e1y=v2y-v1y,e1z=v2z-v1z;
        float e2x=v0x-v2x,e2y=v0y-v2y,e2z=v0z-v2z;

        // 3 AABB face normals — simple min/max check
        if(MathF.Max(v0x,MathF.Max(v1x,v2x))<-hx||MathF.Min(v0x,MathF.Min(v1x,v2x))>hx) return false;
        if(MathF.Max(v0y,MathF.Max(v1y,v2y))<-hy||MathF.Min(v0y,MathF.Min(v1y,v2y))>hy) return false;
        if(MathF.Max(v0z,MathF.Max(v1z,v2z))<-hz||MathF.Min(v0z,MathF.Min(v1z,v2z))>hz) return false;

        // Triangle plane
        float nx2=e0y*e1z-e0z*e1y, ny2=e0z*e1x-e0x*e1z, nz2=e0x*e1y-e0y*e1x;
        float planeD=nx2*v0x+ny2*v0y+nz2*v0z;
        float r=hx*MathF.Abs(nx2)+hy*MathF.Abs(ny2)+hz*MathF.Abs(nz2);
        if(MathF.Abs(planeD)>r) return false;

        // 9 axis tests: edge × coordinate axis
        if(!AT2(e0z,-e0y,v0y,v0z,v2y,v2z,hy,hz)) return false;
        if(!AT2(-e0z,e0x,v0x,v0z,v2x,v2z,hx,hz)) return false;
        if(!AT2(e0y,-e0x,v0x,v0y,v2x,v2y,hx,hy)) return false;
        if(!AT2(e1z,-e1y,v0y,v0z,v1y,v1z,hy,hz)) return false;
        if(!AT2(-e1z,e1x,v0x,v0z,v1x,v1z,hx,hz)) return false;
        if(!AT2(e1y,-e1x,v0x,v0y,v1x,v1y,hx,hy)) return false;
        if(!AT2(e2z,-e2y,v0y,v0z,v1y,v1z,hy,hz)) return false;
        if(!AT2(-e2z,e2x,v0x,v0z,v1x,v1z,hx,hz)) return false;
        if(!AT2(e2y,-e2x,v0x,v0y,v1x,v1y,hx,hy)) return false;
        return true;
    }

    // Projects two of the three vertices onto a 2D axis and checks overlap with AABB
    private static bool AT2(float a,float b, float p0,float q0, float p1,float q1, float ha,float hb)
    {
        float pa=a*p0+b*q0, pb=a*p1+b*q1;
        float mn=MathF.Min(pa,pb), mx=MathF.Max(pa,pb);
        float r=ha*MathF.Abs(a)+hb*MathF.Abs(b);
        return !(mn>r||mx<-r);
    }

    // ── Marching Cubes ────────────────────────────────────────────────────────
    public static float[] MarchingCubes(SdfGrid g, float isoValue=0f)
    {
        var tris=new System.Collections.Generic.List<float>(1<<20);
        int nx=g.NX,ny=g.NY,nz=g.NZ;

        for(int iz=0;iz<nz-1;iz++)
        for(int iy=0;iy<ny-1;iy++)
        for(int ix=0;ix<nx-1;ix++)
        {
            Span<float> v=stackalloc float[8];
            v[0]=g.Data[g.Idx(ix,  iy,  iz  )]; v[1]=g.Data[g.Idx(ix+1,iy,  iz  )];
            v[2]=g.Data[g.Idx(ix+1,iy+1,iz  )]; v[3]=g.Data[g.Idx(ix,  iy+1,iz  )];
            v[4]=g.Data[g.Idx(ix,  iy,  iz+1)]; v[5]=g.Data[g.Idx(ix+1,iy,  iz+1)];
            v[6]=g.Data[g.Idx(ix+1,iy+1,iz+1)]; v[7]=g.Data[g.Idx(ix,  iy+1,iz+1)];

            bool anyMax=false;
            for(int k=0;k<8;k++) if(v[k]==float.MaxValue){anyMax=true;break;}
            if(anyMax) continue;

            int ci=0; for(int k=0;k<8;k++) if(v[k]<isoValue) ci|=(1<<k);
            if(ci==0||ci==255) continue;

            float wx0=g.WorldX(ix),wx1=g.WorldX(ix+1);
            float wy0=g.WorldY(iy),wy1=g.WorldY(iy+1);
            float wz0=g.WorldZ(iz),wz1=g.WorldZ(iz+1);

            Span<(float x,float y,float z)> p=stackalloc (float,float,float)[8];
            p[0]=(wx0,wy0,wz0);p[1]=(wx1,wy0,wz0);p[2]=(wx1,wy1,wz0);p[3]=(wx0,wy1,wz0);
            p[4]=(wx0,wy0,wz1);p[5]=(wx1,wy0,wz1);p[6]=(wx1,wy1,wz1);p[7]=(wx0,wy1,wz1);

            Span<(float x,float y,float z)> e=stackalloc (float,float,float)[12];
            int em=MarchingCubesTables.EdgeTable[ci];
            if((em&   1)!=0)e[0] =Lerp(p[0],v[0],p[1],v[1],isoValue);
            if((em&   2)!=0)e[1] =Lerp(p[1],v[1],p[2],v[2],isoValue);
            if((em&   4)!=0)e[2] =Lerp(p[2],v[2],p[3],v[3],isoValue);
            if((em&   8)!=0)e[3] =Lerp(p[3],v[3],p[0],v[0],isoValue);
            if((em&  16)!=0)e[4] =Lerp(p[4],v[4],p[5],v[5],isoValue);
            if((em&  32)!=0)e[5] =Lerp(p[5],v[5],p[6],v[6],isoValue);
            if((em&  64)!=0)e[6] =Lerp(p[6],v[6],p[7],v[7],isoValue);
            if((em& 128)!=0)e[7] =Lerp(p[7],v[7],p[4],v[4],isoValue);
            if((em& 256)!=0)e[8] =Lerp(p[0],v[0],p[4],v[4],isoValue);
            if((em& 512)!=0)e[9] =Lerp(p[1],v[1],p[5],v[5],isoValue);
            if((em&1024)!=0)e[10]=Lerp(p[2],v[2],p[6],v[6],isoValue);
            if((em&2048)!=0)e[11]=Lerp(p[3],v[3],p[7],v[7],isoValue);

            int[] tt=MarchingCubesTables.TriTable[ci];
            for(int k=0;tt[k]!=-1;k+=3)
            {
                var a=e[tt[k]]; var b=e[tt[k+1]]; var c=e[tt[k+2]];
                tris.Add(a.x);tris.Add(a.y);tris.Add(a.z);
                tris.Add(b.x);tris.Add(b.y);tris.Add(b.z);
                tris.Add(c.x);tris.Add(c.y);tris.Add(c.z);
            }
        }
        return tris.ToArray();
    }

    private static (float x,float y,float z) Lerp(
        (float x,float y,float z) a,float va,
        (float x,float y,float z) b,float vb,float iso)
    {
        if(MathF.Abs(va-vb)<1e-9f)return a;
        float t=(iso-va)/(vb-va);
        return(a.x+t*(b.x-a.x),a.y+t*(b.y-a.y),a.z+t*(b.z-a.z));
    }
}
