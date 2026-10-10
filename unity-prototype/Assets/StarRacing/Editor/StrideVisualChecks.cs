using System;
using System.Linq;
using UnityEngine;
namespace StarRacingPrototype
{
    public static class StrideVisualChecks
    {
        static void Check(bool valid, string message) { if (!valid) throw new Exception("CLOUDLINE: " + message); }
        public static void BuildCheckedMac() { Run(); PrototypeBuilder.BuildMac(); }
        public static void Run()
        {
            var a = new GameObject("stride-check-red");
            var b = new GameObject("stride-check-blue");
            try
            {
                b.transform.SetPositionAndRotation(new Vector3(12, 7, -9), Quaternion.Euler(15, 73, 4));
                var red = StaticVehicleVisual.Create(a.transform, Color.red, true);
                var blue = StaticVehicleVisual.Create(b.transform, Color.blue, false);
                var ar = red.GetComponentsInChildren<MeshRenderer>().Where(r => !r.name.StartsWith("Exhaust plume")).ToArray();
                var br = blue.GetComponentsInChildren<MeshRenderer>().Where(r => !r.name.StartsWith("Exhaust plume")).ToArray();
                Check(ar.Length == 10 && br.Length == 10, "expected ten material renderers");
                Check(red.GetComponentsInChildren<Collider>().Length == 0, "art imported collision");
                Check(red.GetComponentsInChildren<Rigidbody>().Length == 0, "art imported physics body");
                int triangles = ar.Sum(r => r.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3);
                // FBX audit: 122312 source faces minus exactly 668 zero-area faces removed by Unity.
                Check(triangles == 121644, "geometry triangles changed: " + triangles);
                Check(Mathf.Abs(red.LocalBounds.min.y - VehicleGeometry.ChassisMinY) < .001f, "attachment clearance");
                Check(red.LocalBounds.size.x <= VehicleGeometry.Width + .001f &&
                    Mathf.Abs(red.LocalBounds.size.z - VehicleGeometry.Length) < .001f, "collision envelope fit");
                Check(Vector3.Distance(red.LocalBounds.center, blue.LocalBounds.center) < .001f &&
                    Vector3.Distance(red.LocalBounds.size, blue.LocalBounds.size) < .001f, "rotated parent changes fit");
                Check(red.LocalBounds.size.y > 1 && red.LocalBounds.size.y < 1.3f, "incorrect import up axis");
                foreach (var r in ar)
                {
                    var other = br.Single(x => x.name == r.name);
                    Check(r.GetComponent<MeshFilter>().sharedMesh.subMeshCount == 1, "unused material draw passes");
                    Check(r.sharedMaterial != null && r.sharedMaterial.shader.name == "Universal Render Pipeline/Lit", "URP material");
                    Check(r.sharedMaterial == other.sharedMaterial, "material not shared");
                    Check(r.GetComponent<MeshFilter>().sharedMesh == other.GetComponent<MeshFilter>().sharedMesh, "geometry not shared");
                    if (r.name == "Liquid_Silver")
                    {
                        var block = new MaterialPropertyBlock();
                        r.GetPropertyBlock(block); Check(block.GetColor("_BaseColor") == Color.red, "red color");
                        other.GetPropertyBlock(block); Check(block.GetColor("_BaseColor") == Color.blue, "blue color");
                    }
                    else Check(!r.HasPropertyBlock() && !other.HasPropertyBlock(), "non-paint recolored");
                }
                var rear = ar.Single(x => x.name == "Thin_Red_Taillight");
                Check(a.transform.InverseTransformPoint(rear.bounds.center).z < -1, "tail lights are not rear -Z");
                var anchors = red.GetComponentsInChildren<Transform>().Where(t => t.name == "Exhaust_L" || t.name == "Exhaust_R").ToArray();
                Check(anchors.Length == 2, "exhaust anchors");
                foreach (var anchor in anchors) Check(a.transform.InverseTransformPoint(anchor.position).z < -1, "exhaust not at rear");
                Check(red.Exhaust != null && !red.Exhaust.Emitting, "exhaust inactive initially");
                Debug.Log("CLOUDLINE_VISUAL_CHECKS_OK triangles=" + triangles + " bounds=" + red.LocalBounds + " scale=" + red.ModelScale);
            }
            finally { UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b); }
        }
    }
}
