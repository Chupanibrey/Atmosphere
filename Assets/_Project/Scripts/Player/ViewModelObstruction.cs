using Unity.Collections;
using UnityEngine;

/// <summary>
/// Сдвигает вид от первого лица из стены. Точки берутся с сетки, подойдёт для других моделей.
/// </summary>
public sealed class ViewModelObstruction
{
    const float Padding = 0.08f;
    const float ProbeRadius = 0.032f;
    const float SampleSpacing = 0.025f;
    const int MaxSamples = 48;

    static readonly Vector3[] Directions = BuildDirections();

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
        public bool Shell;
    }

    Transform _view;
    Transform _camera;
    Transform _ignore;
    SurfacePoint[] _points = System.Array.Empty<SurfacePoint>();
    Vector3[] _world = System.Array.Empty<Vector3>();
    readonly RaycastHit[] _hits = new RaycastHit[16];
    Vector3 _slide;
    Vector3 _slideVelocity;
    Quaternion _fold = Quaternion.identity;
    bool _warned;

    public void ResetMotion()
    {
        _slide = Vector3.zero;
        _slideVelocity = Vector3.zero;
        _fold = Quaternion.identity;
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

        if (_world.Length != _points.Length)
            _world = new Vector3[_points.Length];

        bool busy = _slide.sqrMagnitude > 0.0001f || Quaternion.Angle(_fold, Quaternion.identity) > 0.2f;
        Skin(shellOnly: !busy);

        using (BackfaceQueries.Begin())
        {
            // Дальние точки сетки нужны только у стены. В комнате хватает внешнего контура.
            if (!busy && !ShellNear())
            {
                _slide = Vector3.zero;
                _slideVelocity = Vector3.zero;
                _fold = Quaternion.identity;
                _view.localRotation = restRotation;
                _view.localPosition = restPosition;
                return;
            }

            if (!busy)
                Skin(shellOnly: false);

            Vector3 solved = Vector3.zero;
            for (int iter = 0; iter < 2; iter++)
            {
                Vector3 shift = _camera.TransformVector(solved);
                if (!FindWorst(shift, false, out _, out Vector3 correction, out float depth) || depth <= 0.001f)
                    break;

                solved = SlideAlongWall(solved, _camera.InverseTransformDirection(correction), restPosition);
            }

            Vector3 finalShift = _camera.TransformVector(solved);
            Quaternion solvedFold = Quaternion.identity;
            if (FindWorst(finalShift, false, out Vector3 point, out Vector3 leftover, out float left) && left > 0.001f)
                solvedFold = FoldTip(point, leftover, restPosition + solved);

            float nextAngle = Quaternion.Angle(solvedFold, Quaternion.identity);
            if (nextAngle >= Quaternion.Angle(_fold, Quaternion.identity) - 0.1f)
                _fold = solvedFold;
            else
                _fold = Quaternion.Slerp(_fold, solvedFold, 1f - Mathf.Exp(-14f * deltaTime));

            if (solved.sqrMagnitude + 0.0001f >= _slide.sqrMagnitude)
            {
                _slide = solved;
                _slideVelocity = Vector3.zero;
            }
            else
            {
                _slide = Vector3.SmoothDamp(_slide, solved, ref _slideVelocity, 0.08f, Mathf.Infinity, deltaTime);
            }

            _view.localRotation = _fold * restRotation;
            _view.localPosition = restPosition + _slide;
        }
    }

    static Vector3 SlideAlongWall(Vector3 solved, Vector3 push, Vector3 restPosition)
    {
        Vector3 desired = solved + push;
        Vector3 clamped = desired;
        ClampSlide(ref clamped, restPosition);

        Vector3 lost = desired - clamped;
        if (lost.sqrMagnitude < 0.000001f)
            return clamped;

        // К камере ближе нельзя: руки пропадут. Остаток уходит вдоль стены, в плоскость экрана.
        Vector3 lateral = new Vector3(push.x, push.y, 0f);
        if (lateral.sqrMagnitude < 0.0001f)
            lateral = new Vector3(push.x <= 0f ? -1f : 1f, -0.35f, 0f);

        clamped += lateral.normalized * lost.magnitude;
        ClampSlide(ref clamped, restPosition);
        return clamped;
    }

    static void ClampSlide(ref Vector3 slide, Vector3 restPosition)
    {
        float maxBack = Mathf.Max(0.04f, restPosition.z - 0.07f);
        slide.x = Mathf.Clamp(slide.x, -0.32f, 0.16f);
        slide.y = Mathf.Clamp(slide.y, -0.14f, 0.04f);
        slide.z = Mathf.Clamp(slide.z, -maxBack, 0.02f);
    }

    void Skin(bool shellOnly)
    {
        for (int i = 0; i < _points.Length; i++)
        {
            if (shellOnly && !_points[i].Shell)
                continue;

            _world[i] = World(_points[i]);
        }
    }

    bool ShellNear()
    {
        for (int i = 0; i < _points.Length; i++)
        {
            if (!_points[i].Shell)
                continue;

            if (NearWall(_world[i], 0.12f))
                return true;
        }

        return false;
    }

    bool FindWorst(Vector3 shift, bool shellOnly, out Vector3 point, out Vector3 correction, out float depth)
    {
        point = Vector3.zero;
        correction = Vector3.zero;
        depth = 0f;

        for (int i = 0; i < _points.Length; i++)
        {
            if (shellOnly && !_points[i].Shell)
                continue;

            Vector3 world = _world[i] + shift;
            if (!TryCorrection(world, out Vector3 push))
                continue;

            float amount = push.magnitude;
            if (amount <= depth)
                continue;

            depth = amount;
            correction = push;
            point = world;
        }

        return depth > 0f;
    }

    Quaternion FoldTip(Vector3 point, Vector3 correction, Vector3 pivotLocal)
    {
        // Сдвиг уже на пределе. Крутим вокруг рук: крюк далеко, ладони почти не двигаются.
        float inside = Mathf.Max(0f, correction.magnitude - Padding);
        Vector3 push = correction.normalized * (inside + 0.02f);
        Vector3 pivot = _camera.TransformPoint(pivotLocal);
        Vector3 from = point - pivot;
        Vector3 to = from + push;
        if (from.sqrMagnitude < 0.0001f || to.sqrMagnitude < 0.0001f)
            return Quaternion.identity;

        Quaternion world = Quaternion.FromToRotation(from, to);
        Quaternion local = Quaternion.Inverse(_camera.rotation) * world * _camera.rotation;
        return LimitAngle(local, 14f);
    }

    static Quaternion LimitAngle(Quaternion rotation, float maxDegrees)
    {
        rotation.ToAngleAxis(out float angle, out Vector3 axis);
        if (float.IsInfinity(angle) || axis.sqrMagnitude < 0.0001f)
            return Quaternion.identity;

        if (angle > 180f)
            angle -= 360f;

        angle = Mathf.Clamp(angle, -maxDegrees, maxDegrees);
        if (Mathf.Abs(angle) < 0.05f)
            return Quaternion.identity;

        return Quaternion.AngleAxis(angle, axis);
    }

    bool TryCorrection(Vector3 point, out Vector3 correction)
    {
        correction = Vector3.zero;
        if (!Aim(point, 0f, out Vector3 origin, out Vector3 direction, out float distance))
            return false;

        // Радиус перекрывает промежуток между точками, иначе крюк и древко проходят между лучами.
        if (!ClosestHit(origin, direction, distance, ProbeRadius, out RaycastHit hit))
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

    bool NearWall(Vector3 point, float margin)
    {
        if (!Aim(point, margin, out Vector3 origin, out Vector3 direction, out float distance))
            return false;

        return ClosestHit(origin, direction, distance, ProbeRadius, out _);
    }

    bool Aim(Vector3 point, float margin, out Vector3 origin, out Vector3 direction, out float distance)
    {
        origin = _camera.position;
        direction = Vector3.forward;
        Vector3 delta = point - origin;
        distance = delta.magnitude;
        if (distance < 0.12f || distance > 2.5f)
            return false;

        if (Vector3.Dot(delta, _camera.forward) < 0.05f)
            return false;

        direction = delta / distance;
        distance += margin;
        return true;
    }

    bool ClosestHit(Vector3 origin, Vector3 direction, float distance, float radius, out RaycastHit closest)
    {
        int count = Physics.SphereCastNonAlloc(
            origin,
            radius,
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

        int[] chosen = PickVertices(vertices, mesh.bounds, out int shellCount);
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
                Skinned = skinned,
                Shell = i < shellCount
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

    static Vector3[] BuildDirections()
    {
        var directions = new Vector3[26];
        int count = 0;
        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    if (x == 0 && y == 0 && z == 0)
                        continue;

                    directions[count++] = new Vector3(x, y, z).normalized;
                }
            }
        }

        return directions;
    }

    static int[] PickVertices(Vector3[] vertices, Bounds bounds, out int shellCount)
    {
        int count = vertices.Length;
        var kept = new int[Mathf.Min(MaxSamples, count)];
        int keptCount = 0;
        var bestIndex = new int[Directions.Length];
        var bestDot = new float[Directions.Length];
        for (int i = 0; i < Directions.Length; i++)
            bestDot[i] = float.NegativeInfinity;

        Vector3 center = bounds.center;
        for (int v = 0; v < count; v++)
        {
            Vector3 offset = vertices[v] - center;
            for (int d = 0; d < Directions.Length; d++)
            {
                float dot = Vector3.Dot(offset, Directions[d]);
                if (dot <= bestDot[d])
                    continue;

                bestDot[d] = dot;
                bestIndex[d] = v;
            }
        }

        for (int d = 0; d < Directions.Length && keptCount < kept.Length; d++)
            TryKeep(vertices, kept, ref keptCount, bestIndex[d]);

        shellCount = keptCount;

        // Дальше точки ставятся в самые большие промежутки, пока шаг не станет меньше радиуса луча.
        float spacingSqr = SampleSpacing * SampleSpacing;
        var nearest = new float[count];
        for (int v = 0; v < count; v++)
            nearest[v] = float.PositiveInfinity;

        for (int k = 0; k < keptCount; k++)
            NoteKept(vertices, nearest, kept[k]);

        while (keptCount < kept.Length)
        {
            int farthest = -1;
            float farthestSqr = spacingSqr;
            for (int v = 0; v < count; v++)
            {
                if (nearest[v] <= farthestSqr)
                    continue;

                farthestSqr = nearest[v];
                farthest = v;
            }

            if (farthest < 0)
                break;

            kept[keptCount++] = farthest;
            NoteKept(vertices, nearest, farthest);
        }

        var result = new int[keptCount];
        System.Array.Copy(kept, result, keptCount);
        return result;
    }

    static void TryKeep(Vector3[] vertices, int[] kept, ref int keptCount, int index)
    {
        for (int i = 0; i < keptCount; i++)
        {
            if (kept[i] == index)
                return;
        }

        if (keptCount < kept.Length)
            kept[keptCount++] = index;
    }

    static void NoteKept(Vector3[] vertices, float[] nearest, int index)
    {
        Vector3 point = vertices[index];
        nearest[index] = 0f;
        for (int v = 0; v < vertices.Length; v++)
        {
            float distance = (vertices[v] - point).sqrMagnitude;
            if (distance < nearest[v])
                nearest[v] = distance;
        }
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
