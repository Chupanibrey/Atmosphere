using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StationGreybox.Editor
{
    // All coordinates in the source plan are XY; V maps them to Unity XZ.
    public static class StationGreyboxGenerator
    {
        const string Source = "Assets/LevelDesign/Station/station_layout_v4_story.json";
        const string Folder = "Assets/LevelDesign/Station/Greybox";
        const string RootName = "STATION_GREYBOX_GENERATED";
        const float DoorHeight = 3f, Thickness = .2f;
        static readonly Rect Pit = Rect.MinMaxRect(48.5f,9,57.5f,16);
        static readonly Rect Basement = Rect.MinMaxRect(94,11,99,16);
        static Transform root;
        static Material wall, floor, accent;
        static int meshIndex;
        static string meshFolder;
        class Region { public string id, name; public List<Vector2> p; public Transform parent; }
        static Vector2 P(JToken a) => new Vector2((float)a[0], (float)a[1]);
        static Vector3 V(Vector2 p, float y=0) => new Vector3(p.x,y,p.y);
        static List<Vector2> Polygon(JToken a) => a.Select(P).ToList();
        static float Cross(Vector2 a, Vector2 b) => a.x*b.y-a.y*b.x;
        static Transform Group(string name, Transform parent) { var g=new GameObject(name); g.transform.SetParent(parent,false); return g.transform; }

        [MenuItem("Tools/Station/Create Test Scene")]
        public static void CreateTestScene()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,Application.isBatchMode?NewSceneMode.Single:NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            Generate();
            EditorSceneManager.SaveScene(scene,AssetDatabase.GenerateUniqueAssetPath(Folder+"/StationGreybox.unity"));
        }

        [MenuItem("Tools/Station/Generate Greybox")]
        public static void Generate()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            var data=JObject.Parse(File.ReadAllText(Source));
            var rooms=ReadRegions(data["rooms"]); var corridors=ReadRegions(data["corridors"]);
            foreach(var r in rooms.Concat(corridors)) Triangulate(r.p); // Validate before modifying the scene.
            var old=SceneManager.GetActiveScene().GetRootGameObjects().Where(g=>g.name==RootName).ToArray();
            if(old.Length>1) throw new InvalidOperationException("Multiple generated roots: select a clean test scene.");
            if(old.Length==1 && !EditorUtility.DisplayDialog("Regenerate station?","Only STATION_GREYBOX_GENERATED will be replaced. Manual edits under this root will be lost. Other objects remain unchanged.","Regenerate","Cancel")) return;
            Directory.CreateDirectory(Folder);
            meshFolder=Folder+"/Meshes_"+Guid.NewGuid().ToString("N"); Directory.CreateDirectory(meshFolder);
            AssetDatabase.Refresh(); meshIndex=0;
            wall=MaterialAsset("Wall",new Color(.48f,.51f,.54f)); floor=MaterialAsset("Floor",new Color(.3f,.34f,.37f)); accent=MaterialAsset("Gate",new Color(.9f,.46f,.12f));
            root=Group(RootName+"_BUILDING",null);
            try
            {
                foreach(var r in rooms.Concat(corridors))
                {
                    bool room=rooms.Contains(r); r.parent=Group((room?"Room_":"Corridor_")+r.id+"_"+r.name,root);
                    var mat=MaterialAsset("Floor_"+r.id,Color.HSVToRGB((rooms.Concat(corridors).ToList().IndexOf(r)*.073f)%1,.15f,.53f));
                    Rect? hole=r.id=="14"?Pit:r.id=="05"?Basement:(Rect?)null;
                    Floor(r.p,0,r.parent,"Floor",mat,hole);
                    var ceiling=Floor(r.p,r.id=="03"?7:4.5f,r.parent,"Ceiling",wall,null); ceiling.SetActive(false);
                }
                var doorLines=data["doors"].Select(d=>Polygon(d["endpoints"])).ToList();
                doorLines.Add(Polygon(data["exterior_intro"]["main_entrance"]["opening_endpoints_xy"]));
                foreach(var r in rooms) Boundary(r,doorLines,null,null,r.id=="03"?7:4.5f);
                foreach(var r in corridors) Boundary(r,doorLines,corridors,rooms,4.5f);
                foreach(var d in data["doors"])
                {
                    var ends=Polygon(d["endpoints"]); var marker=Group("Opening_"+d["room"]+"_"+d["corridor"],root); marker.position=V((ends[0]+ends[1])/2);
                }
                var ext=Group("Exterior_Approach",root);
                Path(Polygon(data["exterior_intro"]["main_approach_centerline_xy"]),4,ext);
                Path(Polygon(data["exterior_intro"]["exploration_spur"]["centerline_xy"]),3,ext);
                var spawn=Group("StationPlayerSpawn",root); var route=Polygon(data["exterior_intro"]["main_approach_centerline_xy"]);
                spawn.position=V(P(data["exterior_intro"]["player_spawn"]["xy"])); spawn.rotation=Quaternion.LookRotation(V(route[1]-route[0]));
                // G1 relocation is deliberate: the original (67,42) marker can be bypassed through C03b.
                Wall(new Vector2(68,42.75f),new Vector2(68,45.25f),0,DoorHeight,root,"Door_G1",accent);
                Wall(new Vector2(97.75f,30),new Vector2(100.25f,30),0,DoorHeight,root,"Door_G2",accent);
                Vertical(rooms); Restricted(rooms.Single(r=>r.id=="05").parent);
                var light=Group("Greybox_WorkLight",root).gameObject.AddComponent<Light>(); light.type=LightType.Directional; light.intensity=1.2f; light.transform.rotation=Quaternion.Euler(50,-30,0);
                root.name=RootName;
                Undo.RegisterCreatedObjectUndo(root.gameObject,"Generate station greybox");
                if(old.Length==1) Undo.DestroyObjectImmediate(old[0]);
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene()); AssetDatabase.SaveAssets();
                Selection.activeGameObject=root.gameObject;
                Debug.Log("Station greybox generated: 15 rooms, 19 corridor polygons. G1/G2 closed. Use Tools/Station/Validate Routes.");
            }
            catch { UnityEngine.Object.DestroyImmediate(root.gameObject); throw; }
        }
        static List<Region> ReadRegions(JToken a) => a.Select(x=>new Region{id=(string)x["id"],name=(string)x["name"],p=Polygon(x["polygon_xy"])}).ToList();
        static Material MaterialAsset(string name,Color color)
        {
            string path=Folder+"/"+name+".mat"; var m=AssetDatabase.LoadAssetAtPath<Material>(path); if(m!=null)return m;
            m=new Material(Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard")); m.color=color; AssetDatabase.CreateAsset(m,path); return m;
        }
        static GameObject Box(string name,Vector3 position,Vector3 scale,Transform parent,Material m)
        {
            var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(parent,false);g.transform.position=position;g.transform.localScale=scale;g.GetComponent<Renderer>().sharedMaterial=m;return g;
        }
        static GameObject Wall(Vector2 a,Vector2 b,float bottom,float height,Transform parent,string name,Material m)
        {
            var g=Box(name,V((a+b)/2,bottom+height/2),new Vector3(Vector2.Distance(a,b),height,Thickness),parent,m);
            g.transform.rotation=Quaternion.Euler(0,-Mathf.Atan2(b.y-a.y,b.x-a.x)*Mathf.Rad2Deg,0);return g;
        }
        static bool On(Vector2 p,Vector2 a,Vector2 b) => Mathf.Abs(Cross(b-a,p-a))<.001f && Vector2.Dot(p-a,p-b)<=.001f;
        static bool Inside(Vector2 p,List<Vector2> poly)
        {
            bool c=false;for(int i=0,j=poly.Count-1;i<poly.Count;j=i++) {var a=poly[i];var b=poly[j];if(On(p,a,b))return true;if((a.y>p.y)!=(b.y>p.y) && p.x<(b.x-a.x)*(p.y-a.y)/(b.y-a.y)+a.x)c=!c;}return c;
        }
        static void Boundary(Region r,List<List<Vector2>> doors,List<Region> corridors,List<Region> rooms,float height,bool addMissingOnly=false)
        {
            for(int i=0;i<r.p.Count;i++)
            {
                var a=r.p[i];var b=r.p[(i+1)%r.p.Count]; var d=b-a; var ts=new List<float>{0,1};
                foreach(var door in doors) foreach(var p in door) if(On(p,a,b)) ts.Add(Vector2.Dot(p-a,d)/d.sqrMagnitude);
                if(corridors!=null) foreach(var other in corridors.Concat(rooms)) for(int j=0;j<other.p.Count;j++)
                {
                    var c=other.p[j];var e=other.p[(j+1)%other.p.Count]-c;float det=Cross(d,e);
                    if(Mathf.Abs(det)>.0001f) {float t=Cross(c-a,e)/det,u=Cross(c-a,d)/det;if(t>0&&t<1&&u>=0&&u<=1)ts.Add(t);}
                    else {if(On(c,a,b))ts.Add(Vector2.Dot(c-a,d)/d.sqrMagnitude);if(On(c+e,a,b))ts.Add(Vector2.Dot(c+e-a,d)/d.sqrMagnitude);}
                }
                ts=ts.OrderBy(t=>t).ToList();
                for(int k=0;k<ts.Count-1;k++)
                {
                    if(ts[k+1]-ts[k]<.0001f)continue;var start=a+d*ts[k];var end=a+d*ts[k+1];var mid=(start+end)/2;
                    if(corridors!=null)
                    {
                        var n=new Vector2(-d.y,d.x).normalized*.01f;
                        // An overlapping edge is internal only when the UNION has floor on BOTH sides.
                        // Collinear outer edges with floor on the same side must retain a wall.
                        if(corridors.Any(c=>Inside(mid+n,c.p)) && corridors.Any(c=>Inside(mid-n,c.p)))continue;
                        if(corridors.TakeWhile(c=>c!=r).Any(c=>Enumerable.Range(0,c.p.Count).Any(j=>On(mid,c.p[j],c.p[(j+1)%c.p.Count]))))continue;
                        if(rooms.Any(c=>Enumerable.Range(0,c.p.Count).Any(j=>On(mid,c.p[j],c.p[(j+1)%c.p.Count]))))continue; // room owns the shared wall
                    }
                    bool opening=doors.Any(q=>On(mid,q[0],q[1])&&On(q[0],a,b)&&On(q[1],a,b));
                    string name=(opening?"Lintel_":"Wall_")+i+"_"+k;
                    if(addMissingOnly && r.parent.Find(name)!=null)continue;
                    var segment=Wall(start,end,opening?DoorHeight:0,opening?height-DoorHeight:height,r.parent,name,wall);
                    if(addMissingOnly)Undo.RegisterCreatedObjectUndo(segment,"Repair corridor perimeter");
                }
            }
        }
        // Ear clipping preserves concave outlines and diagonal walls. No bounding-box floors.
        static List<int> Triangulate(List<Vector2> p)
        {
            if(p.Count<3)throw new InvalidDataException("Polygon needs at least 3 vertices.");
            float area=0;for(int i=0;i<p.Count;i++)area+=Cross(p[i],p[(i+1)%p.Count]);
            var ids=Enumerable.Range(0,p.Count).ToList();if(area<0)ids.Reverse();var result=new List<int>();int guard=0;
            while(ids.Count>3)
            {
                bool found=false;for(int i=0;i<ids.Count;i++)
                {
                    int a=ids[(i+ids.Count-1)%ids.Count],b=ids[i],c=ids[(i+1)%ids.Count];if(Cross(p[b]-p[a],p[c]-p[b])<=.00001f)continue;
                    if(ids.Any(j=>j!=a&&j!=b&&j!=c&&Cross(p[b]-p[a],p[j]-p[a])>=0&&Cross(p[c]-p[b],p[j]-p[b])>=0&&Cross(p[a]-p[c],p[j]-p[c])>=0))continue;
                    result.AddRange(new[]{a,c,b});ids.RemoveAt(i);found=true;break;
                }
                if(!found||guard++>1000)throw new InvalidDataException("Non-simple or degenerate polygon.");
            }
            result.AddRange(new[]{ids[0],ids[2],ids[1]});return result;
        }
        static List<Vector2> Clip(List<Vector2> p,int axis,float boundary,bool greater)
        {
            var output=new List<Vector2>();if(p.Count==0)return output;
            for(int i=0;i<p.Count;i++) {var a=p[i];var b=p[(i+1)%p.Count];float da=(axis==0?a.x:a.y)-boundary,db=(axis==0?b.x:b.y)-boundary;bool ia=greater?da>=0:da<=0,ib=greater?db>=0:db<=0;if(ia)output.Add(a);if(ia!=ib)output.Add(Vector2.Lerp(a,b,da/(da-db)));}return output;
        }
        static GameObject Floor(List<Vector2> p,float y,Transform parent,string name,Material m,Rect? hole)
        {
            var tris=Triangulate(p);var vertices=new List<Vector3>();var indices=new List<int>();
            for(int t=0;t<tris.Count;t+=3)
            {
                var tri=new List<Vector2>{p[tris[t]],p[tris[t+2]],p[tris[t+1]]}; var pieces=new List<List<Vector2>>();
                if(!hole.HasValue)pieces.Add(tri);else
                {
                    var h=hole.Value;pieces.Add(Clip(tri,0,h.xMin,false));pieces.Add(Clip(tri,0,h.xMax,true));var middle=Clip(Clip(tri,0,h.xMin,true),0,h.xMax,false);pieces.Add(Clip(middle,1,h.yMin,false));pieces.Add(Clip(middle,1,h.yMax,true));
                }
                foreach(var poly in pieces) for(int j=1;j<poly.Count-1;j++) {if(Mathf.Abs(Cross(poly[j]-poly[0],poly[j+1]-poly[0]))<.00001f)continue;int n=vertices.Count;vertices.AddRange(new[]{V(poly[0],y),V(poly[j+1],y),V(poly[j],y)});indices.AddRange(new[]{n,n+1,n+2,n+2,n+1,n});}
            }
            return MeshObject(name,vertices.ToArray(),indices.ToArray(),parent,m);
        }
        static GameObject MeshObject(string name,Vector3[] vertices,int[] indices,Transform parent,Material mat)
        {
            var mesh=new Mesh{name=name};mesh.vertices=vertices;mesh.triangles=indices;
            // Two-sided collision triangles share vertices; explicit normals prevent their cancellation.
            var normal=Vector3.Cross(vertices[indices[1]]-vertices[indices[0]],vertices[indices[2]]-vertices[indices[0]]).normalized;
            mesh.normals=Enumerable.Repeat(normal,vertices.Length).ToArray();mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh,meshFolder+"/"+(meshIndex++).ToString("D4")+".asset");
            var g=Group(name,parent).gameObject;g.AddComponent<MeshFilter>().sharedMesh=mesh;g.AddComponent<MeshRenderer>().sharedMaterial=mat;g.AddComponent<MeshCollider>().sharedMesh=mesh;return g;
        }
        static List<Vector2> RectPoly(Rect r) => new List<Vector2>{new Vector2(r.xMin,r.yMin),new Vector2(r.xMax,r.yMin),new Vector2(r.xMax,r.yMax),new Vector2(r.xMin,r.yMax)};
        static void Path(List<Vector2> points,float width,Transform parent)
        {
            for(int i=0;i<points.Count-1;i++) {var a=points[i];var b=points[i+1];var n=new Vector2(-(b-a).y,(b-a).x).normalized*width/2;Floor(new List<Vector2>{a-n,b-n,b+n,a+n},0,parent,"Path_"+i,floor,null);}
            foreach(var p in points)Floor(RectPoly(new Rect(p.x-width/2,p.y-width/2,width,width)),0,parent,"Path_Junction",floor,null);
        }
        static void Ramp(string name,Vector2 a,Vector2 b,float ya,float yb,float width,Transform parent)
        {
            var n=new Vector2(-(b-a).y,(b-a).x).normalized*width/2;
            MeshObject(name,new[]{V(a-n,ya),V(a+n,ya),V(b-n,yb),V(b+n,yb)},new[]{0,1,2,2,1,3,2,1,0,3,1,2},parent,accent);
            Wall(a-n,b-n,Mathf.Min(ya,yb)-.2f,Mathf.Abs(yb-ya)+1.3f,parent,name+"_SideL",wall);
            Wall(a+n,b+n,Mathf.Min(ya,yb)-.2f,Mathf.Abs(yb-ya)+1.3f,parent,name+"_SideR",wall);
        }
        static void Vertical(List<Region> rooms)
        {
            var atrium=Group("Mezzanine_Plus3m",rooms.Single(r=>r.id=="03").parent);
            Floor(RectPoly(Rect.MinMaxRect(46,45,58,48.5f)),3,atrium,"Platform",floor,null);
            Ramp("Ramp_To_Mezzanine",new Vector2(48,36),new Vector2(48,45),0,3,2,atrium);
            Wall(new Vector2(49,45),new Vector2(58,45),3,1.1f,atrium,"Guard_Front",wall);
            Wall(new Vector2(46,45),new Vector2(47,45),3,1.1f,atrium,"Guard_FrontShort",wall);
            Wall(new Vector2(46,45),new Vector2(46,48.5f),3,1.1f,atrium,"Guard_Left",wall);
            Wall(new Vector2(58,45),new Vector2(58,48.5f),3,1.1f,atrium,"Guard_Right",wall);
            Wall(new Vector2(46,48.5f),new Vector2(58,48.5f),3,1.1f,atrium,"Guard_Back",wall);
            var pit=Group("Technical_Pit_Minus1_5m",rooms.Single(r=>r.id=="14").parent);
            Floor(RectPoly(Pit),-1.5f,pit,"Pit_Bottom",floor,null);
            Ramp("Ramp_Into_Pit",new Vector2(50,16),new Vector2(50,11),0,-1.5f,2,pit);
            var corners=RectPoly(Pit);for(int i=0;i<4;i++)Wall(corners[i],corners[(i+1)%4],-1.7f,1.7f,pit,"Pit_Retaining_"+i,wall);
            Wall(new Vector2(48.5f,9),new Vector2(57.5f,9),0,1.1f,pit,"Guard_South",wall);
            Wall(new Vector2(57.5f,9),new Vector2(57.5f,16),0,1.1f,pit,"Guard_East",wall);
            Wall(new Vector2(48.5f,9),new Vector2(48.5f,16),0,1.1f,pit,"Guard_West",wall);
            Wall(new Vector2(51,16),new Vector2(57.5f,16),0,1.1f,pit,"Guard_North",wall);
        }
        static void Restricted(Transform parent)
        {
            var p=Group("PROPOSED_05_Partitions_ReviewInPlayMode",parent);
            // 05a north vestibule -> 05b west -> 05c east; south exit leads to 05d.
            Wall(new Vector2(91,24),new Vector2(95,24),0,4.5f,p,"Partition_05a_West",wall);
            Wall(new Vector2(97.5f,24),new Vector2(111,24),0,4.5f,p,"Partition_05a_East",wall);
            Wall(new Vector2(95,24),new Vector2(97.5f,24),3,1.5f,p,"Lintel_05a_05b",wall);
            Wall(new Vector2(100.5f,17),new Vector2(100.5f,19),0,4.5f,p,"Partition_05b_Lower",wall);
            Wall(new Vector2(100.5f,21.5f),new Vector2(100.5f,24),0,4.5f,p,"Partition_05b_Upper",wall);
            Wall(new Vector2(100.5f,19),new Vector2(100.5f,21.5f),3,1.5f,p,"Lintel_05b_05c",wall);
            Wall(new Vector2(91,17),new Vector2(104,17),0,4.5f,p,"Partition_To_StairWest",wall);
            Wall(new Vector2(106.5f,17),new Vector2(111,17),0,4.5f,p,"Partition_To_StairEast",wall);
            Wall(new Vector2(104,17),new Vector2(106.5f,17),3,1.5f,p,"Lintel_05c_05d",wall);
            // A short westbound ramp terminates at a closed level-2 placeholder, no second level.
            Ramp("Descent_To_Level2",new Vector2(99,13.5f),new Vector2(95,13.5f),0,-1.5f,2.5f,p);
            Floor(RectPoly(Rect.MinMaxRect(94,11,95,16)),-1.5f,p,"Bottom_Landing",floor,null);
            Wall(new Vector2(94,11),new Vector2(94,16),-1.5f,4.5f,p,"Level2_Transition_Placeholder",accent);
            Wall(new Vector2(94,11),new Vector2(99,11),-1.5f,2.6f,p,"Stairwell_South",wall);
            Wall(new Vector2(94,16),new Vector2(99,16),-1.5f,2.6f,p,"Stairwell_North",wall);
        }
        static GameObject FindRoot() => SceneManager.GetActiveScene().GetRootGameObjects().Single(g=>g.name==RootName);
        [MenuItem("Tools/Station/Repair Missing Corridor Walls")]
        public static void RepairCorridorWalls()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play Mode before repair.");
            var r=FindRoot();
            if(r.transform.position!=Vector3.zero||r.transform.rotation!=Quaternion.identity||r.transform.lossyScale!=Vector3.one)throw new InvalidOperationException("Generated root must have identity transform for this repair.");
            var data=JObject.Parse(File.ReadAllText(Source));var rooms=ReadRegions(data["rooms"]);var corridors=ReadRegions(data["corridors"]);
            var doors=data["doors"].Select(d=>Polygon(d["endpoints"])).ToList();
            foreach(var c in corridors)c.parent=r.transform.Find("Corridor_"+c.id+"_"+c.name);
            if(corridors.Any(c=>c.parent==null))throw new InvalidOperationException("A corridor has been renamed; repair cannot map it safely.");
            wall=MaterialAsset("Wall",new Color(.48f,.51f,.54f));
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();
            foreach(var c in corridors)Boundary(c,doors,corridors,rooms,4.5f,true);
            Undo.CollapseUndoOperations(group);Physics.SyncTransforms();EditorSceneManager.MarkSceneDirty(r.scene);
            ValidateCorridorPerimeter();
            Debug.Log("Missing corridor walls repaired additively. Existing objects and door states preserved. Save the scene when ready.");
        }
        [MenuItem("Tools/Station/Validate Corridor Perimeter")]
        public static void ValidateCorridorPerimeter()
        {
            var rootObject=FindRoot();var data=JObject.Parse(File.ReadAllText(Source));var corridors=ReadRegions(data["corridors"]);var rooms=ReadRegions(data["rooms"]);int checkedPoints=0;var gaps=new List<string>();
            Physics.SyncTransforms();
            foreach(var c in corridors)for(int i=0;i<c.p.Count;i++)
            {
                var a=c.p[i];var b=c.p[(i+1)%c.p.Count];var n=new Vector2(-(b-a).y,(b-a).x).normalized*.05f;int count=Mathf.CeilToInt(Vector2.Distance(a,b)/.25f);
                for(int j=0;j<count;j++)
                {
                    var mid=Vector2.Lerp(a,b,(j+.5f)/count);
                    bool left=corridors.Any(q=>Inside(mid+n,q.p)),right=corridors.Any(q=>Inside(mid-n,q.p));
                    if(left==right||rooms.Any(q=>Inside(mid+n,q.p)||Inside(mid-n,q.p)))continue;
                    checkedPoints++;
                    if(!Physics.RaycastAll(V(mid-n*5,1),V(n).normalized,.5f).Any(h=>h.transform.IsChildOf(rootObject.transform)))gaps.Add(c.id+" "+mid);
                }
            }
            string report=$"{(gaps.Count==0?"PASS":"FAIL")} Corridor exterior perimeter: {checkedPoints} samples, {gaps.Count} unprotected edges.\n"+string.Join("\n",gaps);
            File.WriteAllText(Folder+"/PerimeterValidation.txt",report);AssetDatabase.Refresh();
            if(gaps.Count>0)throw new InvalidOperationException(report);Debug.Log(report);
        }
        [MenuItem("Tools/Station/Toggle G1")]
        public static void ToggleG1()=>Toggle("Door_G1");
        [MenuItem("Tools/Station/Toggle G2")]
        public static void ToggleG2()=>Toggle("Door_G2");
        static void Toggle(string name) {var g=FindRoot().transform.Find(name).gameObject;Undo.RecordObject(g,"Toggle gate");g.SetActive(!g.activeSelf);EditorSceneManager.MarkSceneDirty(g.scene);}
        [MenuItem("Tools/Station/Move Selected Player To Spawn")]
        public static void MovePlayer()
        {
            var g=Selection.activeGameObject;if(g==null||!g.TryGetComponent<CharacterController>(out var cc)||EditorUtility.IsPersistent(g))throw new InvalidOperationException("Select a scene player with CharacterController.");
            var spawn=FindRoot().transform.Find("StationPlayerSpawn");Undo.RecordObject(g.transform,"Move player to spawn");
            g.transform.SetPositionAndRotation(spawn.position+Vector3.up*(cc.height/2-cc.center.y+cc.skinWidth+.02f),spawn.rotation);EditorSceneManager.MarkSceneDirty(g.scene);
        }
        [MenuItem("Tools/Station/Validate Routes")]
        public static void ValidateRoutes()
        {
            var r=FindRoot();var g1=r.transform.Find("Door_G1").gameObject;var g2=r.transform.Find("Door_G2").gameObject;bool s1=g1.activeSelf,s2=g2.activeSelf;
            var report=new List<string>{"Physics greybox validation (2m capsule, radius .32m, grid .5m). Not a first-person playtest."};
            try
            {
                foreach(int stage in new[]{0,1,2})
                {
                    g1.SetActive(stage==0);g2.SetActive(stage<2);Physics.SyncTransforms();
                    var walk=new HashSet<Vector2Int>();
                    for(int x=-96;x<=224;x++)for(int z=12;z<=150;z++)
                    {
                        var pos=new Vector3(x*.5f,0,z*.5f);
                        if(!Physics.Raycast(pos+Vector3.up*.25f,Vector3.down,out var hit,.5f)||!hit.transform.IsChildOf(r.transform))continue;
                        if(Physics.OverlapCapsule(pos+Vector3.up*.4f,pos+Vector3.up*1.72f,.32f).Any(c=>c.transform.IsChildOf(r.transform)))continue;
                        walk.Add(new Vector2Int(x,z));
                    }
                    var seen=new HashSet<Vector2Int>();var q=new Queue<Vector2Int>();var start=new Vector2Int(-88,44);if(walk.Contains(start)){q.Enqueue(start);seen.Add(start);}
                    while(q.Count>0){var a=q.Dequeue();foreach(var b in new[]{a+Vector2Int.up,a+Vector2Int.down,a+Vector2Int.left,a+Vector2Int.right})if(walk.Contains(b)&&seen.Add(b))q.Enqueue(b);}
                    foreach(var target in new[]{("01",new Vector2Int(28,80)),("02",new Vector2Int(64,76)),("13",new Vector2Int(60,40)),("14",new Vector2Int(110,40)),("04",new Vector2Int(154,88)),("05",new Vector2Int(198,54))})
                    {
                        bool expected=target.Item1=="04"?stage>=1:target.Item1=="05"?stage>=2:true;bool actual=seen.Contains(target.Item2);
                        report.Add($"{(actual==expected?"PASS":"FAIL")} stage={stage} room={target.Item1} reachable={actual} expected={expected}");
                    }
                }
            }
            finally {g1.SetActive(s1);g2.SetActive(s2);Physics.SyncTransforms();}
            foreach(var ramp in new[]{("Mezzanine",new Vector3(48,0,36),new Vector3(48,3,45)),("Pit",new Vector3(50,0,16),new Vector3(50,-1.5f,11)),("Level2 descent",new Vector3(99,0,13.5f),new Vector3(95,-1.5f,13.5f))})
            {
                bool good=true;
                for(int i=0;i<=100;i++)
                {
                    var pos=Vector3.Lerp(ramp.Item2,ramp.Item3,i/100f);
                    if(!Physics.Raycast(pos+Vector3.up*.2f,Vector3.down,out var hit,.3f)||!hit.transform.IsChildOf(r.transform)||Mathf.Abs(hit.point.y-pos.y)>.03f)good=false;
                    if(Physics.OverlapCapsule(pos+Vector3.up*.4f,pos+Vector3.up*1.72f,.32f).Any(c=>c.transform.IsChildOf(r.transform)))good=false;
                }
                report.Add($"{(good?"PASS":"FAIL")} ramp={ramp.Item1} ground and capsule clearance, 101 samples");
            }
            File.WriteAllLines(Folder+"/Validation.txt",report);AssetDatabase.Refresh();Debug.Log(string.Join("\n",report));
            if(report.Any(x=>x.StartsWith("FAIL")))throw new InvalidOperationException("Station route validation failed; see Validation.txt.");
        }
    }
}
