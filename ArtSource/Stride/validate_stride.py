"""Read the exported GLB independently of Blender; no third-party dependencies."""
import argparse
import hashlib
import json
import math
import struct
from pathlib import Path


def check(value, message):
    if not value:
        raise ValueError(message)


def mul(a, b):
    return [[sum(a[r][k] * b[k][c] for k in range(4)) for c in range(4)] for r in range(4)]


def matrix(node):
    if "matrix" in node:
        a = node["matrix"]
        return [[a[c * 4 + r] for c in range(4)] for r in range(4)]
    x, y, z, w = node.get("rotation", [0, 0, 0, 1])
    sx, sy, sz = node.get("scale", [1, 1, 1])
    tx, ty, tz = node.get("translation", [0, 0, 0])
    return [
        [(1-2*y*y-2*z*z)*sx, (2*x*y-2*z*w)*sy, (2*x*z+2*y*w)*sz, tx],
        [(2*x*y+2*z*w)*sx, (1-2*x*x-2*z*z)*sy, (2*y*z-2*x*w)*sz, ty],
        [(2*x*z-2*y*w)*sx, (2*y*z+2*x*w)*sy, (1-2*x*x-2*y*y)*sz, tz],
        [0, 0, 0, 1],
    ]


def validate(path, require_detail=False):
    raw = path.read_bytes()
    check(len(raw) >= 20, "GLB too short")
    magic, version, length = struct.unpack_from("<4sII", raw)
    check(magic == b"glTF" and version == 2 and length == len(raw), "Invalid GLB header")
    chunks = []
    cursor = 12
    while cursor < len(raw):
        size, kind = struct.unpack_from("<II", raw, cursor)
        cursor += 8
        check(size % 4 == 0 and cursor + size <= len(raw), "Invalid chunk range/alignment")
        chunks.append((kind, raw[cursor:cursor+size]))
        cursor += size
    check(cursor == length and len(chunks) == 2, "Expected JSON + embedded BIN chunks")
    check(chunks[0][0] == 0x4e4f534a and chunks[1][0] == 0x004e4942, "Wrong chunk order")
    doc = json.loads(chunks[0][1])
    binary = chunks[1][1]
    check(doc["asset"]["version"] == "2.0", "Wrong glTF version")
    check(not doc.get("animations") and not doc.get("cameras"), "Unexpected animation/camera")
    check(not doc.get("extensionsRequired"), "Unexpected decoder dependency")
    for buffer in doc["buffers"]:
        check("uri" not in buffer and buffer["byteLength"] <= len(binary), "External/invalid buffer")
    check(not doc.get("images") and not doc.get("textures"), "Unexpected texture dependency")
    materials = doc["materials"]
    names = [m["name"] for m in materials]
    check(names.count("PlayerPaint") == 1, "Need exactly one reusable PlayerPaint material")
    paint = names.index("PlayerPaint")
    check(materials[paint].get("alphaMode", "OPAQUE") == "OPAQUE", "Paint must be opaque")

    formats = {5120: ("b", 1), 5121: ("B", 1), 5122: ("h", 2), 5123: ("H", 2),
               5125: ("I", 4), 5126: ("f", 4)}
    lanes = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}
    cache = {}

    def values(index):
        if index in cache:
            return cache[index]
        a = doc["accessors"][index]
        check("sparse" not in a and "bufferView" in a, "Unsupported sparse accessor")
        view = doc["bufferViews"][a["bufferView"]]
        check(view.get("buffer", 0) == 0, "Unexpected buffer index")
        code, width = formats[a["componentType"]]
        count = lanes[a["type"]]
        stride = view.get("byteStride", count * width)
        offset = view.get("byteOffset", 0) + a.get("byteOffset", 0)
        end = offset + (a["count"]-1)*stride + count*width
        check(end <= view.get("byteOffset", 0)+view["byteLength"] <= len(binary), "Accessor out of range")
        out = [struct.unpack_from("<" + code*count, binary, offset+i*stride) for i in range(a["count"])]
        check(all(math.isfinite(v) for row in out for v in row), "Non-finite geometry")
        cache[index] = out
        return out

    nodes = doc["nodes"]
    node_names = [n.get("name", "") for n in nodes]
    required = ["STRIDE", "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR",
                "Exhaust_L", "Exhaust_R", "NitroSocket_L", "NitroSocket_R"]
    check(all(node_names.count(n) == 1 for n in required), "Missing/duplicate semantic nodes")
    check(not any("camera" in n.lower() or "ground" in n.lower() or "studio" in n.lower()
                  for n in node_names), "Studio leaked into export")
    detail_meshes = {}
    if require_detail:
        minimums = {
            "HoodPlanarPanel_": 1,
            "SideArmorPanel_": 2,
            "OuterFenderArmor_": 4,
            "FenderVentWell_": 4,
            "FenderVentSlat_": 12,
            "BrakeDisc_Wheel_": 4,
            "BrakeCaliper_Wheel_": 4,
            "EngineVentSlat_": 4,
            "NitroMountFlange_": 2,
            "NitroFlangeBolt_": 4,
            "NitroHeatShield_": 2,
        }
        for prefix, minimum in minimums.items():
            matches = [node for node in nodes if node.get("name", "").startswith(prefix)]
            check(len(matches) >= minimum and all("mesh" in node for node in matches),
                  "Detail assembly missing from exported geometry: " + prefix)
            detail_meshes[prefix] = len(matches)
    for n in nodes:
        check(all(abs(s-1) < 1e-5 for s in n.get("scale", [1, 1, 1])), "Unapplied scale")

    identity = [[1, 0, 0, 0], [0, 1, 0, 0], [0, 0, 1, 0], [0, 0, 0, 1]]
    world = {}
    visiting = set()

    def visit(index, parent):
        check(index not in visiting and index not in world, "Cycle/shared parent in hierarchy")
        visiting.add(index)
        world[index] = mul(parent, matrix(nodes[index]))
        for child in nodes[index].get("children", []):
            visit(child, world[index])
        visiting.remove(index)

    roots = doc["scenes"][doc.get("scene", 0)]["nodes"]
    check(len(roots) == 1 and nodes[roots[0]]["name"] == "STRIDE", "Expected STRIDE root")
    visit(roots[0], identity)
    check(len(world) == len(nodes), "Unreachable exported nodes")
    hood_points, hood_faces = [], []
    points, triangles, painted_triangles, primitives, degenerate = [], 0, 0, 0, 0
    planar_panel_triangles, flat_panel_normal_triangles = 0, 0
    for index, node in enumerate(nodes):
        if "mesh" not in node:
            continue
        transform = world[index]
        for primitive in doc["meshes"][node["mesh"]]["primitives"]:
            check(primitive.get("mode", 4) == 4, "Only triangle primitives are allowed")
            vertices = values(primitive["attributes"]["POSITION"])
            check(len(vertices) > 0 and len(vertices[0]) == 3, "Invalid positions")
            normals = values(primitive["attributes"]["NORMAL"])
            check(len(normals) == len(vertices), "Missing/mismatched normals")
            check(all(.95 < sum(v*v for v in normal) < 1.05 for normal in normals), "Non-unit normals")
            indices = [v[0] for v in values(primitive["indices"])] if "indices" in primitive else list(range(len(vertices)))
            check(len(indices) % 3 == 0 and all(0 <= i < len(vertices) for i in indices), "Invalid indices")
            count = len(indices)//3
            triangles += count
            primitives += 1
            if primitive.get("material") == paint:
                painted_triangles += count
            for k in range(0, len(indices), 3):
                a, b, c = [vertices[i] for i in indices[k:k+3]]
                u, v = [b[j]-a[j] for j in range(3)], [c[j]-a[j] for j in range(3)]
                cross = [u[1]*v[2]-u[2]*v[1], u[2]*v[0]-u[0]*v[2], u[0]*v[1]-u[1]*v[0]]
                if sum(x*x for x in cross) < 1e-16:
                    degenerate += 1
                if node.get("name", "").startswith(("HoodPlanarPanel_", "OuterFenderArmor_", "SideArmorPanel_")):
                    planar_panel_triangles += 1
                    na, nb, nc = [normals[i] for i in indices[k:k+3]]
                    if max(abs(na[j]-other[j]) for other in [nb, nc] for j in range(3)) < 1e-4:
                        flat_panel_normal_triangles += 1
            transformed = [[sum(transform[r][c]*p[c] for c in range(3))+transform[r][3]
                            for r in range(3)] for p in vertices]
            points.extend(transformed)
            if node.get("name", "").startswith("HoodPlanarPanel_"):
                hood_points.extend(transformed)
                for k in range(0, len(indices), 3):
                    a, b, c = [transformed[i] for i in indices[k:k+3]]
                    u, v = [b[j]-a[j] for j in range(3)], [c[j]-a[j] for j in range(3)]
                    normal = [u[1]*v[2]-u[2]*v[1], u[2]*v[0]-u[0]*v[2], u[0]*v[1]-u[1]*v[0]]
                    if normal[1] > 0:
                        hood_faces.append((sum(x*x for x in normal), normal))
    check(0 < triangles < 150000 and painted_triangles > 100, "Unexpected geometry/paint coverage")
    check(degenerate == 0, "Degenerate triangles found: " + str(degenerate))
    low, high = [min(p[i] for p in points) for i in range(3)], [max(p[i] for p in points) for i in range(3)]
    dimensions = [high[i]-low[i] for i in range(3)]
    check(1.5 < dimensions[0] < 3.5 and .7 < dimensions[1] < 2.5 and 3 < dimensions[2] < 6,
          "Wrong meter dimensions/orientation")
    check(low[1] >= -.03, "Model below ground")
    sockets = {}
    for name in ["NitroSocket_L", "NitroSocket_R"]:
        i = node_names.index(name)
        m = world[i]
        position = [m[r][3] for r in range(3)]
        direction = [m[r][2] for r in range(3)]
        check(position[2] < -dimensions[2]*.35, "Nitro socket not at rear")
        check(direction[2] < -.99 and abs(direction[0])+abs(direction[1]) < .02, "Nitro axis is not backward")
        sockets[name] = {"position": position, "direction": direction}
    wheels = {name: [world[node_names.index(name)][r][3] for r in range(3)]
              for name in required if name.startswith("Wheel_")}
    check(wheels["Wheel_FL"][2] > wheels["Wheel_RL"][2], "Front/rear wheel pivots reversed")
    check(wheels["Wheel_FL"][0] < 0 < wheels["Wheel_FR"][0], "Left/right wheel pivots reversed")
    hood_profile = {}
    if hood_points:
        front_z, rear_z = max(p[2] for p in hood_points), min(p[2] for p in hood_points)
        front_y = max(p[1] for p in hood_points if p[2] > front_z-.01)
        rear_y = max(p[1] for p in hood_points if p[2] < rear_z+.01)
        hood_profile = {"front_height_m": front_y, "rear_height_m": rear_y,
                        "drop_m": rear_y-front_y, "run_m": front_z-rear_z,
                        "edge_envelope_angle_degrees": math.degrees(math.atan2(rear_y-front_y, front_z-rear_z)),
                        "downward_angle_degrees": math.degrees(math.atan2(max(hood_faces)[1][2], max(hood_faces)[1][1])),
                        "method": "Main panel plane angle from largest upward exported triangle; edge heights include bevel/thickness."}
    return {"success": True, "path": path.name, "sha256": hashlib.sha256(raw).hexdigest(),
            "bytes": len(raw), "triangles": triangles, "degenerate_triangles": degenerate,
            "nodes": len(nodes), "meshes": len(doc["meshes"]), "primitives": primitives,
            "materials": names, "bounds": {"min": low, "max": high, "dimensions_m": dimensions},
            "painted_triangles": painted_triangles, "wheel_pivots": wheels, "nitro_sockets": sockets,
            "hood_profile": hood_profile,
            "detail_required": require_detail, "detail_meshes": detail_meshes,
            "panel_shading": {"triangles": planar_panel_triangles,
                              "constant_normal_triangles": flat_panel_normal_triangles},
            "external_dependencies": [], "note": "Technical validity does not establish artistic acceptance or in-game performance."}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("model", nargs="?", type=Path, default=Path(__file__).with_name("stride.glb"))
    parser.add_argument("--report", type=Path)
    parser.add_argument("--require-detail", action="store_true", help="Verify F1 detail assemblies exist in the actual exported GLB")
    args = parser.parse_args()
    report = validate(args.model.resolve(), require_detail=args.require_detail)
    output = json.dumps(report, indent=2)
    if args.report:
        args.report.write_text(output + "\n")
    print(output)
