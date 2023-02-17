using System;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class CardsLayoutGroup : MonoBehaviour
{
    private struct EvenlySpacedDot
    {
        public Vector2 Point;
        public Vector2 Normal;
    }

    private struct ChildData
    {
        public Transform Transform;
        public RectTransform RectTransform;
    }

    [Tooltip(
        "Curve approximation level. The higher this value, the less smooth the calculated curve on which the cards lie and the faster the calculation")]
    [Range(0.001f, 0.5f)]
    [SerializeField]
    private float _resolution = 0.001f;

    [SerializeField] private AnimationCurve _curve;
    [SerializeField] private bool _reverseOrder = false;
    [SerializeField] private bool _updateLayoutInPlayMode = false;

    [Header("Rotation")] [SerializeField] private bool _useTangent = false;
    [SerializeField] private float _rotationMultiplier = 1f;

    [Space] [SerializeField] private float _heightMultiplier = 1f;

    [Space] [SerializeField] private float _minSpace;


    private List<ChildData> _childrenData;
    private RectTransform _rectTransform;

    private void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
        FillChildrenRectTransformsListIfNecessary();
    }

    private void FillChildrenRectTransformsListIfNecessary()
    {
        bool necessary = _childrenData == null || _childrenData.Count != transform.childCount;

        if (!necessary)
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child == _childrenData[i].Transform) continue;
                necessary = true;
                break;
            }
        }

        if (!necessary) return;

        FillChildrenRectTransformsList();
    }

    private void FillChildrenRectTransformsList()
    {
        _childrenData?.Clear();
        _childrenData = new List<ChildData>();
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (transform.GetChild(i).TryGetComponent(out RectTransform rectTransform))
                _childrenData.Add(new ChildData()
                {
                    Transform = child,
                    RectTransform = rectTransform
                });
            else
                throw new Exception("Сhild without RectTransform component");
        }
    }

    private void Start()
    {
        UpdateLayout();
    }

    private void Update()
    {
        if (Application.isPlaying && !_updateLayoutInPlayMode)
            return;

        UpdateLayout();
    }

    private void UpdateLayout()
    {
        FillChildrenRectTransformsListIfNecessary();

        var rectSize = _rectTransform.rect.size;

        var evenlySpacedDots = CalculateEvenlySpacedDotsOnCurve(_childrenData.Count, rectSize.x, rectSize.y);

        int dotsCount = evenlySpacedDots.Count;
        for (int i = 0; i < dotsCount; i++)
        {
            var esd = evenlySpacedDots[_reverseOrder ? (dotsCount - 1 - i) : i];
            var newLocalPosition = esd.Point - rectSize * _rectTransform.pivot;

            if (float.IsNaN(newLocalPosition.x) || float.IsNaN(newLocalPosition.y)) continue;

            var currentChildRectTransform = _childrenData[i].RectTransform;
            currentChildRectTransform.localPosition = esd.Point - rectSize * _rectTransform.pivot;

            if (_useTangent)
            {
                var rotatedNormal = _rectTransform.rotation * esd.Normal;
                currentChildRectTransform.up = rotatedNormal;
            }
            else
            {
                float middle = (float) (dotsCount - 1) / 2;
                var zAngle = (middle - i) * _rotationMultiplier;
                var lea = currentChildRectTransform.localEulerAngles;
                currentChildRectTransform.localEulerAngles = new Vector3(lea.x, lea.y, zAngle);
            }

            currentChildRectTransform.localPosition =
                currentChildRectTransform.localPosition + currentChildRectTransform.up * _heightMultiplier;
        }
    }

    private List<EvenlySpacedDot> CalculateEvenlySpacedDotsOnCurve(int requestedDotsCount, float width, float height)
    {
        if (requestedDotsCount == 0)
            return new List<EvenlySpacedDot>();

        List<Vector2> curveDots = new List<Vector2>();
        float curveDistance = 0f;
        float step = Mathf.Clamp(_resolution, 0.0001f, 0.5f);
        Vector2? prevDot = null;

        Func<float, Vector2> getNextDot = x => new Vector2(x * width, _curve.Evaluate(x) * height);

        for (float i = 0; i < 1; i += step)
        {
            Vector2 nextDot = getNextDot(i);

            if (prevDot.HasValue)
                curveDistance += Vector2.Distance(prevDot.Value, nextDot);

            curveDots.Add(nextDot);
            prevDot = nextDot;
        }

        var lastDot = getNextDot(1);
        curveDots.Add(lastDot);
        curveDistance += Vector2.Distance(prevDot.Value, lastDot);

        float honestEvenSpace = curveDistance / requestedDotsCount;
        float evenSpace = Mathf.Min(honestEvenSpace, _minSpace < 0 ? 0 : _minSpace);

        float halfCurveDistance = curveDistance / 2;
        float halfDistanceFromFirstToLastCard = evenSpace * (requestedDotsCount - 1) / 2;
        float initSpace = halfCurveDistance - halfDistanceFromFirstToLastCard;

        return CalculateEvenlySpacedDotsOnApproximatedCurve(curveDots, initSpace, evenSpace, requestedDotsCount);
    }

    private List<EvenlySpacedDot> CalculateEvenlySpacedDotsOnApproximatedCurve(
        List<Vector2> curveDots,
        float initSpace,
        float evenSpace,
        int requestedDotsCount)
    {
        List<EvenlySpacedDot> evenlySpacedDots = new List<EvenlySpacedDot>();
        int GetNextIndex(int index) => index + 1;
        int startIndex = 0;
        float currentCoveredDistance = 0f;
        bool evenDotFound = false;
        int curvedPointIndex = startIndex;

        float currentSpaceToCover = initSpace;

        while (curvedPointIndex < curveDots.Count - 1)
        {
            int curvedPointNextIndex = GetNextIndex(curvedPointIndex);

            Vector2 fulcrum = evenDotFound
                ? evenlySpacedDots[evenlySpacedDots.Count - 1].Point
                : curveDots[curvedPointIndex];
            float distanceToNextCurveDot = Vector2.Distance(fulcrum, curveDots[curvedPointNextIndex]);

            Vector2? nextEvenlySpacedDot = null;

            if (currentCoveredDistance + distanceToNextCurveDot < currentSpaceToCover)
            {
                currentCoveredDistance += distanceToNextCurveDot;
                curvedPointIndex++;
            }
            else
            {
                float partDistanceToNextCurveDot = currentSpaceToCover - currentCoveredDistance;

                float distanceToNextCurveDotRatio =
                    partDistanceToNextCurveDot / (distanceToNextCurveDot - partDistanceToNextCurveDot);

                nextEvenlySpacedDot = (fulcrum + distanceToNextCurveDotRatio * curveDots[curvedPointNextIndex]) /
                                      (1 + distanceToNextCurveDotRatio);

                currentSpaceToCover = evenSpace;
            }

            evenDotFound = false;
            if (!nextEvenlySpacedDot.HasValue)
                continue;

            evenDotFound = true;
            evenlySpacedDots.Add(new EvenlySpacedDot()
            {
                Point = nextEvenlySpacedDot.Value,
                Normal = GetPerpendicularVector(curveDots[curvedPointNextIndex] - curveDots[curvedPointIndex])
            });
            currentCoveredDistance = 0f;

            if (evenlySpacedDots.Count == requestedDotsCount)
                break;
        }

        while (evenlySpacedDots.Count < requestedDotsCount)
            evenlySpacedDots.Add(evenlySpacedDots[evenlySpacedDots.Count - 1]);

        return evenlySpacedDots;
    }


    private static Vector2 GetPerpendicularVector(Vector2 vector)
    {
        return new Vector2(-vector.y, vector.x).normalized;
    }
}