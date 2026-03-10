import re

def patch():
    with open("CondyleSplitWindow.xaml.cs", "r", encoding="utf-8") as f:
        text = f.read()

    # Nav
    text = text.replace("using HelixToolkit.Wpf;", "using HelixToolkit.Wpf.SharpDX;")
    
    # Types
    text = text.replace("List<SphereVisual3D>", "List<MeshGeometryModel3D>")
    text = text.replace("ModelVisual3D", "GroupModel3D")
    
    # SetupStep1
    t1 = """        MainViewport.Children.Clear();
        AddLighting();

        var boneModel = MeshHelper.BuildModel3D(_boneVerts, 200, 190, 180, 220);
        MainViewport.Children.Add(new GroupModel3D { Content = boneModel });"""
    r1 = """        MainGroup.Children.Clear();

        var boneModel = MeshHelper.BuildModel3D(_boneVerts, 200, 190, 180, 220);
        MainGroup.Children.Add(boneModel);"""
    text = text.replace(t1, r1)

    t2 = """    private void ShowPlaneTriangle()
    {
        if (_planeTriangleVisual != null) MainViewport.Children.Remove(_planeTriangleVisual);"""
    r2 = """    private void ShowPlaneTriangle()
    {
        if (_planeTriangleVisual != null) MainGroup.Children.Remove(_planeTriangleVisual);"""
    text = text.replace(t2, r2)
    
    # ShowPlaneTriangle body
    t3 = """        var mesh = new MeshGeometry3D();
        mesh.Positions = new Point3DCollection(new[] { c0, c1, c2, c3 });
        mesh.TriangleIndices = new Int32Collection(new[] { 0, 1, 2, 0, 2, 3 }); // double-sided (with BackMaterial)
        mesh.Freeze();

        var brush = new SolidColorBrush(Color.FromArgb(60, 0, 255, 100)); brush.Freeze();
        var mat = new DiffuseMaterial(brush); mat.Freeze();
        var model = new GeometryModel3D(mesh, mat) { BackMaterial = mat }; model.Freeze();

        // Border
        var border = new LinesVisual3D { Color = Colors.Cyan, Thickness = 2 };
        border.Points.Add(c0); border.Points.Add(c1);
        border.Points.Add(c1); border.Points.Add(c2);
        border.Points.Add(c2); border.Points.Add(c3);
        border.Points.Add(c3); border.Points.Add(c0);

        var parent = new GroupModel3D { Content = model };
        parent.Children.Add(border);
        _planeTriangleVisual = parent;
        MainViewport.Children.Add(parent);"""
    r3 = """        var builder = new MeshBuilder();
        builder.AddQuad(
            new SharpDX.Vector3((float)c0.X, (float)c0.Y, (float)c0.Z),
            new SharpDX.Vector3((float)c1.X, (float)c1.Y, (float)c1.Z),
            new SharpDX.Vector3((float)c2.X, (float)c2.Y, (float)c2.Z),
            new SharpDX.Vector3((float)c3.X, (float)c3.Y, (float)c3.Z));

        var mat = new PhongMaterial { DiffuseColor = new SharpDX.Color4(0, 1.0f, 100/255f, 60/255f) };
        var model = new MeshGeometryModel3D { Geometry = builder.ToMeshGeometry3D(), Material = mat, CullMode = SharpDX.Direct3D11.CullMode.None };

        var lineBuilder = new LineBuilder();
        lineBuilder.AddLine(new SharpDX.Vector3((float)c0.X, (float)c0.Y, (float)c0.Z), new SharpDX.Vector3((float)c1.X, (float)c1.Y, (float)c1.Z));
        lineBuilder.AddLine(new SharpDX.Vector3((float)c1.X, (float)c1.Y, (float)c1.Z), new SharpDX.Vector3((float)c2.X, (float)c2.Y, (float)c2.Z));
        lineBuilder.AddLine(new SharpDX.Vector3((float)c2.X, (float)c2.Y, (float)c2.Z), new SharpDX.Vector3((float)c3.X, (float)c3.Y, (float)c3.Z));
        lineBuilder.AddLine(new SharpDX.Vector3((float)c3.X, (float)c3.Y, (float)c3.Z), new SharpDX.Vector3((float)c0.X, (float)c0.Y, (float)c0.Z));
        
        var border = new LineGeometryModel3D { Geometry = lineBuilder.ToLineGeometry3D(), Color = SharpDX.Color.Cyan, Thickness = 2 };

        var parent = new GroupModel3D();
        parent.Children.Add(model);
        parent.Children.Add(border);
        _planeTriangleVisual = parent;
        MainGroup.Children.Add(parent);"""
    text = text.replace(t3, r3)

    # PerformSplit
    t4 = """                MainViewport.Children.Clear();
                AddLighting();

                if (_craniumVerts != null && _craniumVerts.Count > 0)
                {
                    var cranModel = MeshHelper.BuildModel3D(_craniumVerts, 220, 200, 170);
                    MainViewport.Children.Add(new GroupModel3D { Content = cranModel });
                }
                if (_mandibleVerts != null && _mandibleVerts.Count > 0)
                {
                    var mandModel = MeshHelper.BuildModel3D(_mandibleVerts, 180, 200, 220);
                    MainViewport.Children.Add(new GroupModel3D { Content = mandModel });
                }

                // Condylar axis line
                var axis = new LinesVisual3D { Color = Colors.Red, Thickness = 3 };
                axis.Points.Add(new Point3D(leftC[0], leftC[1], leftC[2]));
                axis.Points.Add(new Point3D(rightC[0], rightC[1], rightC[2]));
                MainViewport.Children.Add(axis);
                AddSphereMarker(leftC, Colors.LimeGreen, 2);
                AddSphereMarker(rightC, Colors.OrangeRed, 2);"""
    r4 = """                MainGroup.Children.Clear();

                if (_craniumVerts != null && _craniumVerts.Count > 0)
                {
                    var cranModel = MeshHelper.BuildModel3D(_craniumVerts, 220, 200, 170);
                    MainGroup.Children.Add(cranModel);
                }
                if (_mandibleVerts != null && _mandibleVerts.Count > 0)
                {
                    var mandModel = MeshHelper.BuildModel3D(_mandibleVerts, 180, 200, 220);
                    MainGroup.Children.Add(mandModel);
                }

                // Condylar axis line
                var lineBuilder = new LineBuilder();
                lineBuilder.AddLine(new SharpDX.Vector3(leftC[0], leftC[1], leftC[2]), new SharpDX.Vector3(rightC[0], rightC[1], rightC[2]));
                var axis = new LineGeometryModel3D { Geometry = lineBuilder.ToLineGeometry3D(), Color = SharpDX.Color.Red, Thickness = 3 };
                MainGroup.Children.Add(axis);
                AddSphereMarker(leftC, SharpDX.Color.LimeGreen, 2);
                AddSphereMarker(rightC, SharpDX.Color.OrangeRed, 2);"""
    text = text.replace(t4, r4)

    # Mouse handling 
    t5 = """                var colors = new[] { Colors.Cyan, Colors.Yellow, Colors.Magenta };
                var marker = new SphereVisual3D
                {
                    Center = hit.Value, Radius = 2,
                    Fill = new SolidColorBrush(colors[_planePoints.Count - 1])
                };
                _planeMarkers.Add(marker);
                MainViewport.Children.Add(marker);"""
    r5 = """                var colors = new[] { SharpDX.Color.Cyan, SharpDX.Color.Yellow, SharpDX.Color.Magenta };
                var builder = new MeshBuilder();
                builder.AddSphere(new SharpDX.Vector3(0,0,0), 2);
                var marker = new MeshGeometryModel3D
                {
                    Geometry = builder.ToMeshGeometry3D(),
                    Material = new PhongMaterial { DiffuseColor = colors[_planePoints.Count - 1] },
                    Transform = new TranslateTransform3D(hit.Value.X, hit.Value.Y, hit.Value.Z)
                };
                _planeMarkers.Add(marker);
                MainGroup.Children.Add(marker);"""
    text = text.replace(t5, r5)

    # Ray pointing
    t6 = """                    // Moving along face normal using standard ray projection to camera-parallel plane
                    var pt2Ray = Viewport3DHelper.Point2DtoRay3D(MainViewport.Viewport, pos);
                    var cam = MainViewport.Viewport.Camera as PerspectiveCamera;

                    if (pt2Ray != null && cam != null)"""
    r6 = """                    // Moving along face normal using standard ray projection to camera-parallel plane
                    var pt2RValue = MainViewport.UnProject(pos);
                    var cam = MainViewport.Camera as PerspectiveCamera;

                    if (pt2RValue.HasValue && cam != null)
                    {
                        var pt2Ray = new Ray3D(new Point3D(pt2RValue.Value.Position.X, pt2RValue.Value.Position.Y, pt2RValue.Value.Position.Z), new Vector3D(pt2RValue.Value.Direction.X, pt2RValue.Value.Direction.Y, pt2RValue.Value.Direction.Z));"""
    text = text.replace(t6, r6)
    
    # Needs balanced bracket fixing since I opened one.
    # Wait, the original was:
    # if (pt2Ray != null && cam != null)
    # {
    # I already included { in my replacement inside r6. So it's fine! 
    # Oh wait, the original had "{ " after if. Let's fix.
    text = text.replace(
"""                    if (pt2RValue.HasValue && cam != null)
                    {
                        var pt2Ray = new Ray3D(new Point3D(pt2RValue.Value.Position.X, pt2RValue.Value.Position.Y, pt2RValue.Value.Position.Z), new Vector3D(pt2RValue.Value.Direction.X, pt2RValue.Value.Direction.Y, pt2RValue.Value.Direction.Z));
                    {""",
"""                    if (pt2RValue.HasValue && cam != null)
                    {
                        var pt2Ray = new Ray3D(new Point3D(pt2RValue.Value.Position.X, pt2RValue.Value.Position.Y, pt2RValue.Value.Position.Z), new Vector3D(pt2RValue.Value.Direction.X, pt2RValue.Value.Direction.Y, pt2RValue.Value.Direction.Z));""")

    
    # Lighting + RebuildBox + AddSphereMarker + GetHitPoint overrides
    t7 = """    private void AddLighting()
    {
        MainViewport.Children.Add(new GroupModel3D { Content = new AmbientLight(Color.FromRgb(100, 100, 100)) });
        MainViewport.Children.Add(new GroupModel3D { Content = new DirectionalLight(Color.FromRgb(160, 155, 145), new Vector3D(-1, -1, -0.5)) });
        MainViewport.Children.Add(new GroupModel3D { Content = new DirectionalLight(Color.FromRgb(80, 80, 90), new Vector3D(1, 0.5, 0.3)) });
        MainViewport.Children.Add(new GroupModel3D { Content = new DirectionalLight(Color.FromRgb(60, 60, 70), new Vector3D(0, 1, 0.5)) });
    }

    private void RebuildBoxVisuals()
    {
        if (_leftBoxVisual != null) MainViewport.Children.Remove(_leftBoxVisual);
        if (_rightBoxVisual != null) MainViewport.Children.Remove(_rightBoxVisual);
        if (_leftCondyleCenter != null)
        { _leftBoxVisual = CreateBoxVisual(_leftCondyleCenter, _leftHalfExtents, Colors.LimeGreen); MainViewport.Children.Add(_leftBoxVisual); }
        if (_rightCondyleCenter != null)
        { _rightBoxVisual = CreateBoxVisual(_rightCondyleCenter, _rightHalfExtents, Colors.OrangeRed); MainViewport.Children.Add(_rightBoxVisual); }
    }

    private GroupModel3D CreateBoxVisual(float[] c, float[] he, Color color)
    {
        double cx = c[0], cy = c[1], cz = c[2], hx = he[0], hy = he[1], hz = he[2];
        var pts = new[]
        {
            new Point3D(cx-hx,cy-hy,cz-hz), new Point3D(cx+hx,cy-hy,cz-hz),
            new Point3D(cx+hx,cy+hy,cz-hz), new Point3D(cx-hx,cy+hy,cz-hz),
            new Point3D(cx-hx,cy-hy,cz+hz), new Point3D(cx+hx,cy-hy,cz+hz),
            new Point3D(cx+hx,cy+hy,cz+hz), new Point3D(cx-hx,cy+hy,cz+hz)
        };
        var mesh = new MeshGeometry3D();
        mesh.Positions = new Point3DCollection(pts);
        mesh.TriangleIndices = new Int32Collection(new[]{0,1,2,0,2,3,4,6,5,4,7,6,0,4,5,0,5,1,2,6,7,2,7,3,0,3,7,0,7,4,1,5,6,1,6,2});
        mesh.Freeze();
        var brush = new SolidColorBrush(Color.FromArgb(40, color.R, color.G, color.B)); brush.Freeze();
        var mat = new DiffuseMaterial(brush); mat.Freeze();
        var model = new GeometryModel3D(mesh, mat) { BackMaterial = mat }; model.Freeze();

        int[,] edges = {{0,1},{1,2},{2,3},{3,0},{4,5},{5,6},{6,7},{7,4},{0,4},{1,5},{2,6},{3,7}};
        var lines = new LinesVisual3D { Color = color, Thickness = 2 };
        for (int e = 0; e < 12; e++) { lines.Points.Add(pts[edges[e,0]]); lines.Points.Add(pts[edges[e,1]]); }

        var parent = new GroupModel3D { Content = model };
        parent.Children.Add(lines);

        // Add corner handle on the lateral side
        double midlineX = _planePoints.Count == 3 ? (_planePoints[0].X + _planePoints[1].X + _planePoints[2].X) / 3.0 : 0;
        double signX = cx > midlineX ? 1.0 : -1.0; // Lateral side
        var cornerSphere = new SphereVisual3D
        { 
            Center = new Point3D(cx + hx * signX, cy + hy, cz + hz), 
            Radius = 3, 
            Fill = new SolidColorBrush(Colors.Yellow) 
        };
        parent.Children.Add(cornerSphere);

        return parent;
    }

    private void AddSphereMarker(float[] c, Color color, double r)
    {
        MainViewport.Children.Add(new SphereVisual3D
        { Center = new Point3D(c[0], c[1], c[2]), Radius = r, Fill = new SolidColorBrush(color) });
    }

    private Point3D? GetHitPoint(Point screenPos)
    {
        var result = Viewport3DHelper.FindHits(MainViewport.Viewport, screenPos);
        if (result != null && result.Count > 0) return result[0].Position;
        return null;
    }"""

    # We must be careful because of previous replaces modifying ModelVisual3D -> GroupModel3D.
    # So I will just regex the whole block from "private void AddLighting()" to "return null;\n    }"

    p = re.compile(r"    private void AddLighting\(\).*?return null;\n    }", re.DOTALL)
    
    r7 = """    private void RebuildBoxVisuals()
    {
        if (_leftBoxVisual != null) MainGroup.Children.Remove(_leftBoxVisual);
        if (_rightBoxVisual != null) MainGroup.Children.Remove(_rightBoxVisual);
        if (_leftCondyleCenter != null)
        { _leftBoxVisual = CreateBoxVisual(_leftCondyleCenter, _leftHalfExtents, SharpDX.Color.LimeGreen); MainGroup.Children.Add(_leftBoxVisual); }
        if (_rightCondyleCenter != null)
        { _rightBoxVisual = CreateBoxVisual(_rightCondyleCenter, _rightHalfExtents, SharpDX.Color.OrangeRed); MainGroup.Children.Add(_rightBoxVisual); }
    }

    private GroupModel3D CreateBoxVisual(float[] c, float[] he, SharpDX.Color color)
    {
        double cx = c[0], cy = c[1], cz = c[2], hx = he[0], hy = he[1], hz = he[2];
        var pts = new[]
        {
            new SharpDX.Vector3((float)(cx-hx),(float)(cy-hy),(float)(cz-hz)), new SharpDX.Vector3((float)(cx+hx),(float)(cy-hy),(float)(cz-hz)),
            new SharpDX.Vector3((float)(cx+hx),(float)(cy+hy),(float)(cz-hz)), new SharpDX.Vector3((float)(cx-hx),(float)(cy+hy),(float)(cz-hz)),
            new SharpDX.Vector3((float)(cx-hx),(float)(cy-hy),(float)(cz+hz)), new SharpDX.Vector3((float)(cx+hx),(float)(cy-hy),(float)(cz+hz)),
            new SharpDX.Vector3((float)(cx+hx),(float)(cy+hy),(float)(cz+hz)), new SharpDX.Vector3((float)(cx-hx),(float)(cy+hy),(float)(cz+hz))
        };
        var builder = new MeshBuilder();
        int[] tris = {0,1,2,0,2,3,4,6,5,4,7,6,0,4,5,0,5,1,2,6,7,2,7,3,0,3,7,0,7,4,1,5,6,1,6,2};
        for (int i = 0; i < tris.Length; i+=3) {
            builder.AddTriangle(pts[tris[i]], pts[tris[i+1]], pts[tris[i+2]]);
        }
        var mat = new PhongMaterial { DiffuseColor = new SharpDX.Color4(color.R/255f, color.G/255f, color.B/255f, 40f/255f) };
        var model = new MeshGeometryModel3D { Geometry = builder.ToMeshGeometry3D(), Material = mat, CullMode = SharpDX.Direct3D11.CullMode.None };

        int[,] edges = {{0,1},{1,2},{2,3},{3,0},{4,5},{5,6},{6,7},{7,4},{0,4},{1,5},{2,6},{3,7}};
        var lineBuilder = new LineBuilder();
        for (int e = 0; e < 12; e++) { lineBuilder.AddLine(pts[edges[e,0]], pts[edges[e,1]]); }
        var lines = new LineGeometryModel3D { Geometry = lineBuilder.ToLineGeometry3D(), Color = color, Thickness = 2 };

        var parent = new GroupModel3D();
        parent.Children.Add(model);
        parent.Children.Add(lines);

        // Add corner handle on the lateral side
        double midlineX = _planePoints.Count == 3 ? (_planePoints[0].X + _planePoints[1].X + _planePoints[2].X) / 3.0 : 0;
        double signX = cx > midlineX ? 1.0 : -1.0; // Lateral side
        
        var sbuild = new MeshBuilder();
        sbuild.AddSphere(new SharpDX.Vector3(0,0,0), 3);
        var cornerSphere = new MeshGeometryModel3D
        { 
            Geometry = sbuild.ToMeshGeometry3D(),
            Material = new PhongMaterial { DiffuseColor = SharpDX.Color.Yellow },
            Transform = new TranslateTransform3D(cx + hx * signX, cy + hy, cz + hz)
        };
        parent.Children.Add(cornerSphere);

        return parent;
    }

    private void AddSphereMarker(float[] c, SharpDX.Color color, double r)
    {
        var builder = new MeshBuilder();
        builder.AddSphere(new SharpDX.Vector3(0,0,0), r);
        var sphere = new MeshGeometryModel3D
        {
            Geometry = builder.ToMeshGeometry3D(),
            Material = new PhongMaterial { DiffuseColor = color },
            Transform = new TranslateTransform3D(c[0], c[1], c[2])
        };
        MainGroup.Children.Add(sphere);
    }

    private Point3D? GetHitPoint(Point screenPos)
    {
        var result = MainViewport.FindHits(screenPos);
        if (result != null && result.Count > 0) return new Point3D(result[0].PointHit.X, result[0].PointHit.Y, result[0].PointHit.Z);
        return null;
    }"""
    
    text = p.sub(r7, text)
    
    with open("CondyleSplitWindow.xaml.cs", "w", encoding="utf-8") as f:
        f.write(text)

patch()
