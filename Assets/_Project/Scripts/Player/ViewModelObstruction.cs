using Unity.Collections;
using UnityEngine;

/// <summary>
/// Сдвигает вид от первого лица из стены. Точки берутся с сетки, подойдёт для других моделей.
/// </summary>
public sealed class ViewModelObstruction
{
    const float Padding = 0.04f;

    static readonly Vector3[] Axes =
    {
        Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back
    };

    struct SurfacePoint
    {
        public Vector3 Vertex;
        public Transform Space;
        public Transform[] Bones;
        public Matrix4x4[] Bindposes;
        public Transform Dominant;
        public int I0, I1, I2, I3;
        public float W0, W1, W2, W3;
        public bool Skinned;
    }

    Transform _view;
    Transform _camera;
    Transform _ignore;
    SurfacePoint[] _points = System.Array.Empty<SurfacePoint>();
    readonly RaycastHit[] _hits = new RaycastHit[16];
    Vector3 _slide;
    Vector3 _slideVelocity;
    bool _warned;

    public void ResetMotion()
    {
        _slide = Vector3.zero;
        _slideVelocity = Vector3.zero;
    }

    public void Apply(Transform viewModel, Transform camera, Transform ignoreRoot, Vector3 restPosition, Vector3 restEuler, float deltaTime)
    {
        if (viewModel == null || camera == null)
            return;

        _camera = camera;
        _ignore = ignoreRoot;
        if (_view != viewModel)
            Bind(viewModel);

        Quaternion restRotation = Quaternion.Euler(restEuler);
        _view.localRotation = restRotation;
        _view.localPosition = restPosition;

        if (_points.Length == 0)
            return;

        bool prevBackfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        try
        {
            Vector3 solved = Vector3.zero;
            for (int iter = 0; iter < 3; iter++)
            {
                _view.localPosition = restPosition + solved;
                if (!FindWorst(out _, out Vector3 correction, out float depth) || depth <= 0.001f)
                    break;

                solved += _camera.InverseTransformDirection(correction);
                ClampSlide(ref solved, restPosition);
            }

            if (solved.sqrMagnitude + 0.0001f >= _slide.sqrMagnitude)
            {
                _slide = solved;
                _slideVelocity = Vector3.zero;
            }
            else
            {
                _slide = Vector3.SmoothDamp(_slide, solved, ref _slideVelocity, 0.08f, Mathf.Infinity, deltaTime);
            }

            _view.localRotation = restRotation;
            _view.localPosition = restPosition + _slide;
        }
        finally
        {
            Physics.queriesHitBackfaces = prevBackfaces;
        }
    }

    static void ClampSlide(ref Vector3 slide, Vector3 restPosition)
    {
        float maxBack = Mathf.Max(0.04f, restPosition.z - 0.07f);
        slide.x = Mathf.Clamp(slide.x, -0.22f, 0.22f);
        slide.y = Mathf.Clamp(slide.y, -0.1f, 0.05f);
        slide.z = Mathf.Clamp(slide.z, -maxBack, 0.02f);
    }

    bool FindWorst(out int index, out Vector3 correction, out float depth)
    {
        index = -1;
        correction = Vector3.zero;
        depth = 0f;

        for (int i = 0; i < _points.Length; i++)
        {
            Vector3 world = World(_points[i]);
            if (!TryCorrection(world, out Vector3 push))
                continue;

            float amount = push.magnitude;
            if (amount <= depth)
                continue;

            depth = amount;
            correction = push;
            index = i;
        }

        return index >= 0;
    }

    bool TryCorrection(Vector3 point, out Vector3 correction)
    {
        correction = Vector3.zero;
        Vector3 origin = _camera.position;
        Vector3 delta = point - origin;
        float distance = delta.magnitude;
        if (distance < 0.12f || distance > 2.5f)
            return false;

        if (Vector3.Dot(delta, _camera.forward) < 0.05f)
            return false;

        Vector3 direction = delta / distance;
        if (!ClosestHit(origin, direction, distance + Padding, out RaycastHit hit))
            return false;

        Vector3 normal = hit.normal;
        if (Vector3.Dot(normal, origin - hit.point) < 0f)
            normal = -normal;

        float signed = Vector3.Dot(point - hit.point, normal);
        float need = Padding - signed;
        if (need <= 0.001f)
            return false;

        correction = normal * need;
        return true;
    }

    bool ClosestHit(Vector3 origin, Vector3 direction, float distance, out RaycastHit closest)
    {
        int count = Physics.RaycastNonAlloc(
            origin,
            direction,
            _hits,
            distance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        float nearest = distance;
        int found = -1;
        for (int i = 0; i < count; i++)
        {
            Transform hit = _hits[i].transform;
            if (hit == null || IsIgnored(hit))
                continue;

            if (_hits[i].distance >= nearest)
                continue;

            nearest = _hits[i].distance;
            found = i;
        }

        if (found < 0)
        {
            closest = default;
            return false;
        }

        closest = _hits[found];
        return true;
    }

    bool IsIgnored(Transform hit)
    {
        if (_ignore == null)
            return false;

        return hit == _ignore || hit.IsChildOf(_ignore);
    }

    Vector3 World(in SurfacePoint point)
    {
        if (!point.Skinned || point.Bones == null || point.Bindposes == null)
            return point.Space != null ? point.Space.TransformPoint(point.Vertex) : point.Vertex;

        Vector3 sum = Vector3.zero;
        float weight = 0f;
        AddBone(ref sum, ref weight, point, point.W0, point.I0);
        AddBone(ref sum, ref weight, point, point.W1, point.I1);
        AddBone(ref sum, ref weight, point, point.W2, point.I2);
        AddBone(ref sum, ref weight, point, point.W3, point.I3);
        if (weight <= 0.001f)
            return point.Space != null ? point.Space.TransformPoint(point.Vertex) : sum;

        return sum / weight;
    }

    static void AddBone(ref Vector3 sum, ref float weight, in SurfacePoint point, float boneWeight, int index)
    {
        if (boneWeight <= 0f || (uint)index >= (uint)point.Bones.Length)
            return;

        Transform bone = point.Bones[index];
        if (bone == null)
            return;

        sum += boneWeight * bone.TransformPoint(point.Bindposes[index].MultiplyPoint3x4(point.Vertex));
        weight += boneWeight;
    }

    void Bind(Transform viewModel)
    {
        _view = viewModel;
        ResetMotion();

        var found = new System.Collections.Generic.List<SurfacePoint>(24);
        foreach (var renderer in viewModel.GetComponentsInChildren<Renderer>(true))
            Collect(renderer, found);

        _points = found.ToArray();
    }

    void Collect(Renderer renderer, System.Collections.Generic.List<SurfacePoint> found)
    {
        Mesh mesh = null;
        Transform[] bones = null;
        var skin = renderer as SkinnedMeshRenderer;
        bool skinned = skin != null;
        if (skin != null)
        {
            mesh = skin.sharedMesh;
            bones = skin.bones;
        }
        else if (renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter filter))
        {
            mesh = filter.sharedMesh;
        }

        if (mesh == null || !mesh.isReadable)
        {
            WarnUnreadable(renderer.name);
            return;
        }

        Vector3[] vertices = mesh.vertices;
        if (vertices.Length == 0)
            return;

        Matrix4x4[] bindposes = skinned ? mesh.bindposes : null;
        if (skinned && (bones == null || bindposes == null || bones.Length != bindposes.Length || bones.Length == 0))
            skinned = false;

        int[] chosen = PickVertices(vertices, mesh.bounds);
        var weights = new BoneWeight[chosen.Length];
        if (skinned)
            ReadWeights(mesh, chosen, weights);

        for (int i = 0; i < chosen.Length; i++)
        {
            int vertex = chosen[i];
            var point = new SurfacePoint
            {
                Vertex = vertices[vertex],
                Space = renderer.transform,
                Bones = bones,
                Bindposes = bindposes,
                Skinned = skinned
            };

            if (skinned)
            {
                BoneWeight weight = weights[i];
                point.I0 = weight.boneIndex0;
                point.I1 = weight.boneIndex1;
                point.I2 = weight.boneIndex2;
                point.I3 = weight.boneIndex3;
                point.W0 = weight.weight0;
                point.W1 = weight.weight1;
                point.W2 = weight.weight2;
                point.W3 = weight.weight3;
                point.Dominant = Dominant(bones, weight);
            }
            else
            {
                point.Dominant = renderer.transform;
            }

            found.Add(point);
        }
    }

    static Transform Dominant(Transform[] bones, BoneWeight weight)
    {
        int index = weight.boneIndex0;
        float best = weight.weight0;
        if (weight.weight1 > best)
        {
            best = weight.weight1;
            index = weight.boneIndex1;
        }

        if (weight.weight2 > best)
        {
            best = weight.weight2;
            index = weight.boneIndex2;
        }

        if (weight.weight3 > best)
            index = weight.boneIndex3;

        if ((uint)index >= (uint)bones.Length)
            return null;

        return bones[index];
    }

    static int[] PickVertices(Vector3[] vertices, Bounds bounds)
    {
        var chosen = new int[Axes.Length + 8];
        var score = new float[Axes.Length + 8];
        for (int i = 0; i < Axes.Length; i++)
            score[i] = float.NegativeInfinity;

        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;
        var corners = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            corners[i] = center + new Vector3(
                (i & 1) == 0 ? extents.x : -extents.x,
                (i & 2) == 0 ? extents.y : -extents.y,
                (i & 4) == 0 ? extents.z : -extents.z);
            score[Axes.Length + i] = float.PositiveInfinity;
        }

        for (int v = 0; v < vertices.Length; v++)
        {
            Vector3 vertex = vertices[v];
            for (int a = 0; a < Axes.Length; a++)
            {
                float dot = Vector3.Dot(vertex, Axes[a]);
                if (dot <= score[a])
                    continue;

                score[a] = dot;
                chosen[a] = v;
            }

            for (int c = 0; c < 8; c++)
            {
                float distance = (vertex - corners[c]).sqrMagnitude;
                int slot = Axes.Length + c;
                if (distance >= score[slot])
                    continue;

                score[slot] = distance;
                chosen[slot] = v;
            }
        }

        int count = 0;
        var unique = new int[chosen.Length];
        for (int i = 0; i < chosen.Length; i++)
        {
            bool duplicate = false;
            for (int j = 0; j < count; j++)
            {
                if (unique[j] != chosen[i])
                    continue;

                duplicate = true;
                break;
            }

            if (!duplicate)
                unique[count++] = chosen[i];
        }

        var result = new int[count];
        System.Array.Copy(unique, result, count);
        return result;
    }

    static void ReadWeights(Mesh mesh, int[] chosen, BoneWeight[] destination)
    {
        var perVertex = mesh.GetBonesPerVertex();
        var all = mesh.GetAllBoneWeights();
        try
        {
            if (perVertex.Length != mesh.vertexCount || all.Length == 0)
            {
                BoneWeight[] fallback = mesh.boneWeights;
                if (fallback == null || fallback.Length != mesh.vertexCount)
                    return;

                for (int i = 0; i < chosen.Length; i++)
                    destination[i] = fallback[chosen[i]];
                return;
            }

            int cursor = 0;
            int next = 0;
            var order = new int[chosen.Length];
            for (int i = 0; i < order.Length; i++)
                order[i] = i;
            System.Array.Sort(order, (a, b) => chosen[a].CompareTo(chosen[b]));

            for (int v = 0; v < perVertex.Length && next < order.Length; v++)
            {
                int influences = perVertex[v];
                int slot = order[next];
                if (v == chosen[slot])
                {
                    destination[slot] = Strongest(all, cursor, influences);
                    next++;
                    while (next < order.Length && chosen[order[next]] == v)
                    {
                        destination[order[next]] = destination[slot];
                        next++;
                    }
                }

                cursor += influences;
            }
        }
        finally
        {
            perVertex.Dispose();
            all.Dispose();
        }
    }

    static BoneWeight Strongest(NativeArray<BoneWeight1> weights, int start, int count)
    {
        var result = new BoneWeight();
        float w0 = 0f, w1 = 0f, w2 = 0f, w3 = 0f;
        int i0 = 0, i1 = 0, i2 = 0, i3 = 0;
        int take = Mathf.Min(count, 4);
        for (int i = 0; i < take; i++)
        {
            BoneWeight1 influence = weights[start + i];
            if (influence.weight >= w0)
            {
                w3 = w2; i3 = i2;
                w2 = w1; i2 = i1;
                w1 = w0; i1 = i0;
                w0 = influence.weight;
                i0 = influence.boneIndex;
            }
            else if (influence.weight >= w1)
            {
                w3 = w2; i3 = i2;
                w2 = w1; i2 = i1;
                w1 = influence.weight;
                i1 = influence.boneIndex;
            }
            else if (influence.weight >= w2)
            {
                w3 = w2; i3 = i2;
                w2 = influence.weight;
                i2 = influence.boneIndex;
            }
            else if (influence.weight >= w3)
            {
                w3 = influence.weight;
                i3 = influence.boneIndex;
            }
        }

        result.weight0 = w0;
        result.weight1 = w1;
        result.weight2 = w2;
        result.weight3 = w3;
        result.boneIndex0 = i0;
        result.boneIndex1 = i1;
        result.boneIndex2 = i2;
        result.boneIndex3 = i3;
        return result;
    }

    void WarnUnreadable(string rendererName)
    {
        if (_warned)
            return;

        _warned = true;
        Debug.LogWarning("ViewModelObstruction: у «" + rendererName + "» выключено Read/Write, оружие не сможет упираться в стену.");
    }
}
