using MixedReality.Toolkit;
using MixedReality.Toolkit.Subsystems;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

public class DrawLine : MonoBehaviour
{
    // classe da linha desenhada
    private class Stroke
    {
        public LineRenderer line;
        public readonly List<Vector3> points = new List<Vector3>();
    }

    [Header("Linha")]
    [SerializeField] private float lineWidth = 0.01f;
    [SerializeField] private float minPointDistance = 0.005f;
    [SerializeField] private float smoothing = 0.5f;
    [SerializeField] private Material baseMaterial;

    [Header("Pinch")]
    [SerializeField] private Handedness drawingHand = Handedness.Right;
    [SerializeField] private float pinchStartDistance = 0.02f;
    [SerializeField] private float pinchEndDistance = 0.035f;
    [SerializeField] private float guideShowDistance = 0.06f;

    [Header("Guia e ramos")]
    [SerializeField] private float guideDiameter = 0.015f;
    [SerializeField] private float guideSnapScale = 1.6f;
    [SerializeField] private float snapDistance = 0.02f;

    [Header("Ligar linhas")]
    [SerializeField] private float joinDistance = 0.025f;
    [SerializeField] private float selfIgnoreLength = 0.06f;

    private readonly List<Stroke> strokes = new List<Stroke>();

    private Transform container, guide;
    private Renderer guideRenderer;
    private Material currentMaterial;
    private Stroke currentStroke;
    private bool isPinching, hasJoin;
    private Vector3 smoothedPoint, joinPoint;

    private void Start()
    {
        container = new GameObject("DrawingContainer").transform;

        CreateGuide();

        // TODO: poder mudar o material
        if (baseMaterial != null)
        {
            currentMaterial = new Material(baseMaterial);
            currentMaterial.color = Color.red;
        }
        else
        {
            Debug.LogError("DrawLine: Base Material não foi atribuído!");
        }
    }

    private void CreateGuide()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "DrawGuide";
        Destroy(go.GetComponent<Collider>());

        guide = go.transform;
        guide.localScale = Vector3.one * guideDiameter;
        guideRenderer = go.GetComponent<Renderer>();
        guideRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        guideRenderer.receiveShadows = false;
        go.SetActive(false);
    }

    private void Update()
    {
        if (!TryGetPinchPoint(out Vector3 point, out float distance))
        {
            EndStroke(false);
            guide.gameObject.SetActive(false);
            return;
        }

        if (!isPinching && distance < pinchStartDistance) isPinching = true;
        else if (isPinching && distance > pinchEndDistance) isPinching = false;

        if (!isPinching)
        {
            EndStroke(true);

            if (distance < guideShowDistance)
            {
                bool snapped = TryFindSnapPoint(point, snapDistance, null, out Vector3 snap);
                ShowGuide(snapped ? snap : point, snapped);
            }
            else guide.gameObject.SetActive(false);

            return;
        }

        if (currentStroke == null)
        {
            Vector3 start = TryFindSnapPoint(point, snapDistance, null, out Vector3 snap)
                ? snap : point;

            smoothedPoint = start;
            StartStroke(start);
            ShowGuide(start, false);
            return;
        }

        smoothedPoint = Vector3.Lerp(point, smoothedPoint, smoothing);

        Vector3 last = currentStroke.points[currentStroke.points.Count - 1];
        if ((smoothedPoint - last).sqrMagnitude >= minPointDistance * minPointDistance)
            AddPoint(currentStroke, smoothedPoint);

        hasJoin = TryFindSnapPoint(smoothedPoint, joinDistance, currentStroke, out joinPoint);
        ShowGuide(hasJoin ? joinPoint : smoothedPoint, hasJoin);
    }

    private void ShowGuide(Vector3 point, bool snapped)
    {
        guide.gameObject.SetActive(true);
        guide.position = point;
        guide.localScale = Vector3.one * guideDiameter * (snapped ? guideSnapScale : 1f);

        if (currentMaterial != null)
            guideRenderer.sharedMaterial = currentMaterial;
    }

    private bool TryFindSnapPoint(Vector3 point, float maxDistance, Stroke self, out Vector3 snap)
    {
        snap = point;
        float bestDistance = maxDistance * maxDistance;
        bool found = false;

        foreach (Stroke stroke in strokes)
        {
            List<Vector3> points = stroke.points;
            int limit = points.Count - 1;

            if (stroke == self)
            {
                float length = 0f;
                int index = points.Count - 1;

                while (index > 0 && length < selfIgnoreLength)
                    length += Vector3.Distance(points[index], points[--index]);

                if (index == 0 && length < selfIgnoreLength)
                    continue;

                limit = index;
            }

            for (int i = 0; i <= limit; i++)
            {
                Vector3 candidate = points[i];

                if (i < limit)
                {
                    Vector3 a = points[i];
                    Vector3 ab = points[i + 1] - a;
                    float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) /
                                            Mathf.Max(ab.sqrMagnitude, 1e-9f));
                    candidate = a + ab * t;
                }

                float distance = (candidate - point).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    snap = candidate;
                    found = true;
                }
            }
        }

        return found;
    }

    private void StartStroke(Vector3 start)
    {
        var go = new GameObject("Stroke");
        go.transform.SetParent(container, false);

        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.sharedMaterial = currentMaterial;
        line.widthMultiplier = lineWidth;
        line.numCapVertices = line.numCornerVertices = 4;
        line.alignment = LineAlignment.View;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        var stroke = new Stroke { line = line };
        AddPoint(stroke, start);

        currentStroke = stroke;
        hasJoin = false;
        strokes.Add(stroke);
    }

    private void AddPoint(Stroke stroke, Vector3 point)
    {
        stroke.points.Add(point);
        stroke.line.positionCount = stroke.points.Count;
        stroke.line.SetPosition(stroke.points.Count - 1, point);
    }

    private void EndStroke(bool tryJoin)
    {
        isPinching = false;

        if (currentStroke != null)
        {
            if (tryJoin && hasJoin && currentStroke.points.Count >= 2)
                AddPoint(currentStroke, joinPoint);

            if (currentStroke.points.Count < 2)
            {
                strokes.Remove(currentStroke);
                Destroy(currentStroke.line.gameObject);
            }
        }

        currentStroke = null;
        hasJoin = false;
    }

    private HandsAggregatorSubsystem handsAggregator;

    private void Awake()
    {
        handsAggregator =
            XRSubsystemHelpers.GetFirstRunningSubsystem<HandsAggregatorSubsystem>();
    }

    private bool TryGetPinchPoint(out Vector3 point, out float distance)
    {
        point = Vector3.zero;
        distance = float.MaxValue;

        if (handsAggregator == null)
        {
            handsAggregator =
                XRSubsystemHelpers.GetFirstRunningSubsystem<HandsAggregatorSubsystem>();

            if (handsAggregator == null)
                return false;
        }

        XRNode hand = drawingHand == Handedness.Right
            ? XRNode.RightHand
            : XRNode.LeftHand;

        if (!handsAggregator.TryGetJoint(
            TrackedHandJoint.IndexTip,
            hand,
            out HandJointPose index))
            return false;

        if (!handsAggregator.TryGetJoint(
            TrackedHandJoint.ThumbTip,
            hand,
            out HandJointPose thumb))
            return false;

        point = (index.Position + thumb.Position) * 0.5f;

        distance = Vector3.Distance(
            index.Position,
            thumb.Position
        );

        return true;
    }
}