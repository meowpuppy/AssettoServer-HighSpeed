using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using AssettoServer.Server.Ai.Splines;
using AssettoServer.Server.Configuration;
using AssettoServer.Server.Weather;
using AssettoServer.Shared.Model;
using AssettoServer.Shared.Network.Packets.Outgoing;
using AssettoServer.Utils;
using JPBotelho;
using Serilog;

namespace AssettoServer.Server.Ai;

public class AiState
{
    public CarStatus Status { get; } = new();
    public bool Initialized { get; private set; }

    public int CurrentSplinePointId
    {
        get => _currentSplinePointId;
        private set
        {
            _spline.SlowestAiStates.Enter(value, this);
            _spline.SlowestAiStates.Leave(_currentSplinePointId, this);
            _currentSplinePointId = value;
        }
    }

    private int _currentSplinePointId;

    private class FlashInfo
    {
        public bool LastHighBeamsOn;
        public int FlashCount;
        public long FirstFlashTime;
    }

    private readonly Dictionary<byte, FlashInfo> _playerFlashStates = new();
    private const int FlashWindowMs = 5000; // 5 seconds
    private const int RequiredFlashes = 3;  // Number of flashes to trigger

    public long SpawnProtectionEnds { get; set; }
    public float SafetyDistanceSquared { get; set; } = 20 * 20;
    public float Acceleration { get; set; }
    public float CurrentSpeed { get; private set; }
    public float TargetSpeed { get; private set; }
    public float InitialMaxSpeed { get; private set; }
    public float MaxSpeed { get; private set; }
    public Color Color { get; private set; }
    public byte SpawnCounter { get; private set; }
    public float ClosestAiObstacleDistance { get; private set; }
    public EntryCar EntryCar { get; }

    private bool _isScared = false;
    private float _scareFade = 0f; // 0 = not scared, 1 = fully scared
    private float _scareTarget = 0f;
    private float _lastMoveDir = 0f;
    private const float ScareFadeInSpeed = 5.0f;  // seconds to fade in
    private const float ScareFadeOutSpeed = 10.0f; // seconds to fade out

    private bool _isChangingLane = false;
    private int _laneChangeStartIndex;
    private int _laneChangeTargetIndex;
    private float _laneChangeProgress = 0f;
    private float _laneChangeDuration = Random.Shared.Next(2, 7); // seconds, adjust as needed

    private long _laneChangeCooldownMs = 10000;
    private long _lastLaneChangeTime = 0;

    // Add these fields to the class (replace the existing LaneChangeCooldownMs and _lastLaneChangeTime)
    private static readonly int MinLaneChangeCooldownMs = 30_000;   // 30 seconds
    private static readonly int MaxLaneChangeCooldownMs = 120_000;  // 2 minutes

    private int _currentLaneIndex = 0;

    private const float WalkingSpeed = 10 / 3.6f;

    private Vector3 _startTangent;
    private Vector3 _endTangent;

    private float _currentVecLength;
    private float _currentVecProgress;
    private long _lastTick;
    private bool _stoppedForObstacle;
    private long _stoppedForObstacleSince;
    private long _ignoreObstaclesUntil;
    private long _stoppedForCollisionUntil;
    private long _obstacleHonkStart;
    private long _obstacleHonkEnd;
    private CarStatusFlags _indicator = 0;
    private int _nextJunctionId;
    private bool _junctionPassed;
    private float _endIndicatorDistance;
    private float _minObstacleDistance;

    private float _laneDeviationPhase;
    private float _laneDeviationSpeed;
    private float _laneDeviationAmplitude;

    private readonly ACServerConfiguration _configuration;
    private readonly SessionManager _sessionManager;
    private readonly EntryCarManager _entryCarManager;
    private readonly WeatherManager _weatherManager;
    private readonly AiSpline _spline;
    private readonly JunctionEvaluator _junctionEvaluator;

    private static readonly List<Color> CarColors =
    [
        Color.FromArgb(13, 17, 22),
        Color.FromArgb(19, 24, 31),
        Color.FromArgb(28, 29, 33),
        Color.FromArgb(12, 13, 24),
        Color.FromArgb(11, 20, 33),
        Color.FromArgb(151, 154, 151),
        Color.FromArgb(153, 157, 160),
        Color.FromArgb(194, 196, 198),
        Color.FromArgb(234, 234, 234),
        Color.FromArgb(255, 255, 255),
        Color.FromArgb(182, 17, 27),
        Color.FromArgb(218, 25, 24),
        Color.FromArgb(73, 17, 29),
        Color.FromArgb(35, 49, 85),
        Color.FromArgb(28, 53, 81),
        Color.FromArgb(37, 58, 167),
        Color.FromArgb(21, 92, 45),
        Color.FromArgb(18, 46, 43)
    ];

    public AiState(EntryCar entryCar, SessionManager sessionManager, WeatherManager weatherManager, ACServerConfiguration configuration, EntryCarManager entryCarManager, AiSpline spline)
    {
        EntryCar = entryCar;
        _sessionManager = sessionManager;
        _weatherManager = weatherManager;
        _configuration = configuration;
        _entryCarManager = entryCarManager;
        _spline = spline;
        _junctionEvaluator = new JunctionEvaluator(spline);

        // set the _currentlaneIndex to the current lane of the ai using the spline point id
        _currentLaneIndex = _spline.GetLaneIndex(CurrentSplinePointId);

        SetNextLaneChangeCooldown();

        _lastTick = _sessionManager.ServerTimeMilliseconds;
    }

    public void SetDeviationSettings()
    {
        _laneDeviationPhase = (float)(Random.Shared.NextDouble() * MathF.PI * 2);
        _laneDeviationSpeed = (0.2f + (float)Random.Shared.NextDouble() * 0.3f) * 0.15f;
        _laneDeviationAmplitude = 0.15f + (float)Random.Shared.NextDouble() * 0.15f;
    } 

    public void Despawn()
    {
        Initialized = false;
        SetDeviationSettings();
        _spline.SlowestAiStates.Leave(CurrentSplinePointId, this);

    }

    private void SetRandomSpeed()
    {
        float variation = _configuration.Extra.AiParams.MaxSpeedMs * _configuration.Extra.AiParams.MaxSpeedVariationPercent;

        float fastLaneOffset = 0;
        if (_spline.Points[CurrentSplinePointId].LeftId >= 0)
        {
            fastLaneOffset = _configuration.Extra.AiParams.RightLaneOffsetMs;
        }
        InitialMaxSpeed = _configuration.Extra.AiParams.MaxSpeedMs + fastLaneOffset - (variation / 2) + (float)Random.Shared.NextDouble() * variation;
        CurrentSpeed = InitialMaxSpeed;
        TargetSpeed = InitialMaxSpeed;
        MaxSpeed = InitialMaxSpeed;
    }

    private void SetRandomColor()
    {
        Color = CarColors[Random.Shared.Next(CarColors.Count)];
    }

    public void Teleport(int pointId)
    {
        _junctionEvaluator.Clear();
        CurrentSplinePointId = pointId;

        if (!_junctionEvaluator.TryNext(CurrentSplinePointId, out var nextPointId))
            throw new InvalidOperationException($"Cannot get next spline point for {CurrentSplinePointId}");
        _currentVecLength = (_spline.Points[nextPointId].Position - _spline.Points[CurrentSplinePointId].Position).Length();
        _currentVecProgress = 0;
            
        CalculateTangents();
        
        SetRandomSpeed();
        SetRandomColor();

        var minDist = _configuration.Extra.AiParams.MinAiSafetyDistanceSquared;
        var maxDist = _configuration.Extra.AiParams.MaxAiSafetyDistanceSquared;
        if (_configuration.Extra.AiParams.LaneCountSpecificOverrides.TryGetValue(_spline.GetLanes(CurrentSplinePointId).Length, out var overrides))
        {
            minDist = overrides.MinAiSafetyDistanceSquared;
            maxDist = overrides.MaxAiSafetyDistanceSquared;
        }
        
        if (EntryCar.MinAiSafetyDistanceMetersSquared.HasValue)
            minDist = EntryCar.MinAiSafetyDistanceMetersSquared.Value;
        if (EntryCar.MaxAiSafetyDistanceMetersSquared.HasValue)
            maxDist = EntryCar.MaxAiSafetyDistanceMetersSquared.Value;

        SetNextLaneChangeCooldown();

        SpawnProtectionEnds = _sessionManager.ServerTimeMilliseconds + Random.Shared.Next(EntryCar.AiMinSpawnProtectionTimeMilliseconds, EntryCar.AiMaxSpawnProtectionTimeMilliseconds);
        SafetyDistanceSquared = Random.Shared.Next((int)Math.Round(minDist * (1.0f / _configuration.Extra.AiParams.TrafficDensity)),
            (int)Math.Round(maxDist * (1.0f / _configuration.Extra.AiParams.TrafficDensity)));
        _stoppedForCollisionUntil = 0;
        _ignoreObstaclesUntil = 0;
        _obstacleHonkEnd = 0;
        _obstacleHonkStart = 0;
        _indicator = 0;
        _nextJunctionId = -1;
        _junctionPassed = false;
        _endIndicatorDistance = 0;
        _lastTick = _sessionManager.ServerTimeMilliseconds;
        _minObstacleDistance = Random.Shared.Next(8, 13);

        SetDeviationSettings();

        SpawnCounter++;
        Initialized = true;
        Update();
    }

    private void CalculateTangents()
    {
        if (!_junctionEvaluator.TryNext(CurrentSplinePointId, out var nextPointId))
            throw new InvalidOperationException("Cannot get next spline point");

        var points = _spline.Points;

        if (_junctionEvaluator.TryPrevious(CurrentSplinePointId, out var previousPointId))
        {
            _startTangent = (points[nextPointId].Position - points[previousPointId].Position) * 0.5f;
        }
        else
        {
            _startTangent = (points[nextPointId].Position - points[CurrentSplinePointId].Position) * 0.5f;
        }

        if (_junctionEvaluator.TryNext(CurrentSplinePointId, out var nextNextPointId, 2))
        {
            _endTangent = (points[nextNextPointId].Position - points[CurrentSplinePointId].Position) * 0.5f;
        }
        else
        {
            _endTangent = (points[nextPointId].Position - points[CurrentSplinePointId].Position) * 0.5f;
        }
    }

    private bool Move(float progress)
    {
        var points = _spline.Points;
        var junctions = _spline.Junctions;
        
        bool recalculateTangents = false;
        while (progress > _currentVecLength)
        {
            progress -= _currentVecLength;
                
            if (!_junctionEvaluator.TryNext(CurrentSplinePointId, out var nextPointId)
                || !_junctionEvaluator.TryNext(nextPointId, out var nextNextPointId))
            {
                return false;
            }

            CurrentSplinePointId = nextPointId;
            _currentVecLength = (points[nextNextPointId].Position - points[CurrentSplinePointId].Position).Length();
            recalculateTangents = true;

            if (_junctionPassed)
            {
                _endIndicatorDistance -= _currentVecLength;

                if (_endIndicatorDistance < 0)
                {
                    _indicator = 0;
                    _junctionPassed = false;
                    _endIndicatorDistance = 0;
                }
            }
                
            if (_nextJunctionId >= 0 && points[CurrentSplinePointId].JunctionEndId == _nextJunctionId)
            {
                _junctionPassed = true;
                _endIndicatorDistance = junctions[_nextJunctionId].IndicateDistancePost;
                _nextJunctionId = -1;
            }
        }

        if (recalculateTangents)
        {
            CalculateTangents();
        }

        _currentVecProgress = progress;

        return true;
    }

    public bool CanSpawn(int spawnPointId, AiState? previousAi, AiState? nextAi)
    {
        var ops = _spline.Operations;
        ref readonly var spawnPoint = ref ops.Points[spawnPointId];

        if (!IsAllowedLaneCount(spawnPointId))
            return false;
        if (!IsAllowedLane(in spawnPoint))
            return false;
        if (!IsKeepingSafetyDistances(in spawnPoint, previousAi, nextAi))
            return false;

        return EntryCar.CanSpawnAiState(spawnPoint.Position, this);
    }

    private bool IsKeepingSafetyDistances(in SplinePoint spawnPoint, AiState? previousAi, AiState? nextAi)
    {
        if (previousAi != null)
        {
            var distance = MathF.Max(0, Vector3.Distance(spawnPoint.Position, previousAi.Status.Position)
                           - previousAi.EntryCar.VehicleLengthPreMeters
                           - EntryCar.VehicleLengthPostMeters);

            var distanceSquared = distance * distance;
            if (distanceSquared < previousAi.SafetyDistanceSquared || distanceSquared < SafetyDistanceSquared)
                return false;
        }
        
        if (nextAi != null)
        {
            var distance = MathF.Max(0, Vector3.Distance(spawnPoint.Position, nextAi.Status.Position)
                                        - nextAi.EntryCar.VehicleLengthPostMeters
                                        - EntryCar.VehicleLengthPreMeters);

            var distanceSquared = distance * distance;
            if (distanceSquared < nextAi.SafetyDistanceSquared || distanceSquared < SafetyDistanceSquared)
                return false;
        }

        return true;
    }

    private bool IsAllowedLaneCount(int spawnPointId)
    {
        var laneCount = _spline.GetLanes(spawnPointId).Length;
        if (EntryCar.MinLaneCount.HasValue && laneCount < EntryCar.MinLaneCount.Value)
            return false;
        if (EntryCar.MaxLaneCount.HasValue && laneCount > EntryCar.MaxLaneCount.Value)
            return false;
        
        return true;
    }

    private bool IsAllowedLane(in SplinePoint spawnPoint)
    {
        var isAllowedLane = true;
        if (EntryCar.AiAllowedLanes != null)
        {
            isAllowedLane = (EntryCar.AiAllowedLanes.Contains(LaneSpawnBehavior.Middle) && spawnPoint.LeftId >= 0 && spawnPoint.RightId >= 0)
                            || (EntryCar.AiAllowedLanes.Contains(LaneSpawnBehavior.Left) && spawnPoint.LeftId < 0)
                            || (EntryCar.AiAllowedLanes.Contains(LaneSpawnBehavior.Right) && spawnPoint.RightId < 0);
        }

        return isAllowedLane;
    }

    private (AiState? ClosestAiState, float ClosestAiStateDistance, float MaxSpeed) SplineLookahead()
    {
        var points = _spline.Points;
        var junctions = _spline.Junctions;
        
        float maxBrakingDistance = PhysicsUtils.CalculateBrakingDistance(CurrentSpeed, EntryCar.AiDeceleration) * 2 + 20;
        AiState? closestAiState = null;
        float closestAiStateDistance = float.MaxValue;
        bool junctionFound = false;
        float distanceTravelled = 0;
        var pointId = CurrentSplinePointId;
        ref readonly var point = ref points[pointId]; 
        float maxSpeed = float.MaxValue;
        float currentSpeedSquared = CurrentSpeed * CurrentSpeed;
        while (distanceTravelled < maxBrakingDistance)
        {
            distanceTravelled += point.Length;
            pointId = _junctionEvaluator.Next(pointId);
            if (pointId < 0)
                break;

            point = ref points[pointId];

            if (!junctionFound && point.JunctionStartId >= 0 && distanceTravelled < junctions[point.JunctionStartId].IndicateDistancePre)
            {
                ref readonly var jct = ref junctions[point.JunctionStartId];
                
                var indicator = _junctionEvaluator.WillTakeJunction(point.JunctionStartId) ? jct.IndicateWhenTaken : jct.IndicateWhenNotTaken;
                if (indicator != 0)
                {
                    _indicator = indicator;
                    _nextJunctionId = point.JunctionStartId;
                    junctionFound = true;
                }
            }

            if (closestAiState == null)
            {
                var slowest = _spline.SlowestAiStates[pointId];

                if (slowest != null)
                {
                    closestAiState = slowest;
                    closestAiStateDistance = MathF.Max(0, Vector3.Distance(Status.Position, closestAiState.Status.Position)
                                                          - EntryCar.VehicleLengthPreMeters
                                                          - closestAiState.EntryCar.VehicleLengthPostMeters);
                }
            }

            float maxCorneringSpeedSquared = PhysicsUtils.CalculateMaxCorneringSpeedSquared(point.Radius, EntryCar.AiCorneringSpeedFactor);
            if (maxCorneringSpeedSquared < currentSpeedSquared)
            {
                float maxCorneringSpeed = MathF.Sqrt(maxCorneringSpeedSquared);
                float brakingDistance = PhysicsUtils.CalculateBrakingDistance(CurrentSpeed - maxCorneringSpeed,
                                            EntryCar.AiDeceleration * EntryCar.AiCorneringBrakeForceFactor)
                                        * EntryCar.AiCorneringBrakeDistanceFactor;

                if (brakingDistance > distanceTravelled)
                {
                    maxSpeed = Math.Min(maxCorneringSpeed, maxSpeed);
                }
            }
        }

        return (closestAiState, closestAiStateDistance, maxSpeed);
    }

    private bool ShouldIgnorePlayerObstacles()
    {
        if (_configuration.Extra.AiParams.IgnorePlayerObstacleSpheres != null)
        {
            foreach (var sphere in _configuration.Extra.AiParams.IgnorePlayerObstacleSpheres)
            {
                if (Vector3.DistanceSquared(Status.Position, sphere.Center) < sphere.RadiusMeters * sphere.RadiusMeters)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private (EntryCar? entryCar, float distance) FindClosestPlayerObstacle()
    {
        if (!ShouldIgnorePlayerObstacles())
        {
            EntryCar? closestCar = null;
            float minDistance = float.MaxValue;
            for (var i = 0; i < _entryCarManager.EntryCars.Length; i++)
            {
                var playerCar = _entryCarManager.EntryCars[i];
                if (playerCar.Client?.HasSentFirstUpdate == true)
                {
                    float distance = Vector3.DistanceSquared(playerCar.Status.Position, Status.Position);

                    if (distance < minDistance && GetAngleToCar(playerCar.Status) is > 166 and < 194)
                    {
                        minDistance = distance;
                        closestCar = playerCar;
                    }
                }
            }

            if (closestCar != null)
            {
                return (closestCar, MathF.Sqrt(minDistance));
            }
        }

        return (null, float.MaxValue);
    }

    private bool IsObstacle(EntryCar playerCar)
    {
        float aiRectWidth = 4; // Lane width
        float halfAiRectWidth = aiRectWidth / 2;
        float aiRectLength = 10; // length of rectangle infront of ai traffic
        float aiRectOffset = 1; // offset of the rectangle from ai position

        float obstacleRectWidth = 1; // width of obstacle car 
        float obstacleRectLength = 1; // length of obstacle car
        float halfObstacleRectWidth = obstacleRectWidth / 2;
        float halfObstanceRectLength = obstacleRectLength / 2;

        Vector3 forward = Vector3.Transform(-Vector3.UnitX, Matrix4x4.CreateRotationY(Status.Rotation.X));
        Matrix4x4 aiViewMatrix = Matrix4x4.CreateLookAt(Status.Position, Status.Position + forward, Vector3.UnitY);

        Matrix4x4 targetWorldViewMatrix = Matrix4x4.CreateRotationY(playerCar.Status.Rotation.X) * Matrix4x4.CreateTranslation(playerCar.Status.Position) * aiViewMatrix;

        Vector3 targetFrontLeft = Vector3.Transform(new Vector3(-halfObstanceRectLength, 0, halfObstacleRectWidth), targetWorldViewMatrix);
        Vector3 targetFrontRight = Vector3.Transform(new Vector3(-halfObstanceRectLength, 0, -halfObstacleRectWidth), targetWorldViewMatrix);
        Vector3 targetRearLeft = Vector3.Transform(new Vector3(halfObstanceRectLength, 0, halfObstacleRectWidth), targetWorldViewMatrix);
        Vector3 targetRearRight = Vector3.Transform(new Vector3(halfObstanceRectLength, 0, -halfObstacleRectWidth), targetWorldViewMatrix);

        static bool IsPointInside(Vector3 point, float width, float length, float offset)
            => MathF.Abs(point.X) >= width || (-point.Z >= offset && -point.Z <= offset + length);

        bool isObstacle = IsPointInside(targetFrontLeft, halfAiRectWidth, aiRectLength, aiRectOffset)
                          || IsPointInside(targetFrontRight, halfAiRectWidth, aiRectLength, aiRectOffset)
                          || IsPointInside(targetRearLeft, halfAiRectWidth, aiRectLength, aiRectOffset)
                          || IsPointInside(targetRearRight, halfAiRectWidth, aiRectLength, aiRectOffset);

        return isObstacle;
    }

    public void DetectObstacles()
    {
        if (!Initialized) return;
            
        if (_sessionManager.ServerTimeMilliseconds < _ignoreObstaclesUntil)
        {
            SetTargetSpeed(MaxSpeed);
            return;
        }

        if (_sessionManager.ServerTimeMilliseconds < _stoppedForCollisionUntil)
        {
            SetTargetSpeed(0);
            return;
        }
            
        float targetSpeed = InitialMaxSpeed;
        float maxSpeed = InitialMaxSpeed;
        bool hasObstacle = false;

        var splineLookahead = SplineLookahead();
        var playerObstacle = FindClosestPlayerObstacle();

        ClosestAiObstacleDistance = splineLookahead.ClosestAiState != null ? splineLookahead.ClosestAiStateDistance : -1;

        if (playerObstacle.distance < _minObstacleDistance || splineLookahead.ClosestAiStateDistance < _minObstacleDistance)
        {
            targetSpeed = 0;
            hasObstacle = true;
        }

        else if (playerObstacle.distance < splineLookahead.ClosestAiStateDistance && playerObstacle.entryCar != null)
        {
            float playerSpeed = playerObstacle.entryCar.Status.Velocity.Length();

            if (playerSpeed < 0.1f)
            {
                playerSpeed = 0;
            }

            if ((playerSpeed < CurrentSpeed || playerSpeed == 0)
                && playerObstacle.distance < PhysicsUtils.CalculateBrakingDistance(CurrentSpeed - playerSpeed, EntryCar.AiDeceleration) * 2 + 20)
            {
                targetSpeed = Math.Max(WalkingSpeed, playerSpeed);
                hasObstacle = true;
            }
        }
        else if (splineLookahead.ClosestAiState != null)
        {
            float closestTargetSpeed = Math.Min(splineLookahead.ClosestAiState.CurrentSpeed, splineLookahead.ClosestAiState.TargetSpeed);
            if ((closestTargetSpeed < CurrentSpeed || splineLookahead.ClosestAiState.CurrentSpeed == 0)
                && splineLookahead.ClosestAiStateDistance < PhysicsUtils.CalculateBrakingDistance(CurrentSpeed - closestTargetSpeed, EntryCar.AiDeceleration) * 2 + 20)
            {
                targetSpeed = Math.Max(WalkingSpeed, closestTargetSpeed);
                hasObstacle = true;
            }
        }

        targetSpeed = Math.Min(splineLookahead.MaxSpeed, targetSpeed);

        if (CurrentSpeed == 0 && !_stoppedForObstacle)
        {
            _stoppedForObstacle = true;
            _stoppedForObstacleSince = _sessionManager.ServerTimeMilliseconds;
            _obstacleHonkStart = _stoppedForObstacleSince + Random.Shared.Next(3000, 7000);
            _obstacleHonkEnd = _obstacleHonkStart + Random.Shared.Next(500, 1500);
            Log.Verbose("AI {SessionId} stopped for obstacle", EntryCar.SessionId);
        }
        else if (CurrentSpeed > 0 && _stoppedForObstacle)
        {
            _stoppedForObstacle = false;
            Log.Verbose("AI {SessionId} no longer stopped for obstacle", EntryCar.SessionId);
        }
        else if (_stoppedForObstacle && _sessionManager.ServerTimeMilliseconds - _stoppedForObstacleSince > _configuration.Extra.AiParams.IgnoreObstaclesAfterMilliseconds)
        {
            _ignoreObstaclesUntil = _sessionManager.ServerTimeMilliseconds + 10_000;
            Log.Verbose("AI {SessionId} ignoring obstacles until {IgnoreObstaclesUntil}", EntryCar.SessionId, _ignoreObstaclesUntil);
        }

        float deceleration = EntryCar.AiDeceleration;
        if (!hasObstacle)
        {
            deceleration *= EntryCar.AiCorneringBrakeForceFactor;
        }
        
        MaxSpeed = maxSpeed;
        SetTargetSpeed(targetSpeed, deceleration, EntryCar.AiAcceleration);
    }

    public void StopForCollision()
    {
        if (!ShouldIgnorePlayerObstacles())
        {
            _stoppedForCollisionUntil = _sessionManager.ServerTimeMilliseconds + Random.Shared.Next(EntryCar.AiMinCollisionStopTimeMilliseconds, EntryCar.AiMaxCollisionStopTimeMilliseconds);
        }
    }

    public float GetAngleToCar(CarStatus car)
    {
        float challengedAngle = (float) (Math.Atan2(Status.Position.X - car.Position.X, Status.Position.Z - car.Position.Z) * 180 / Math.PI);
        if (challengedAngle < 0)
            challengedAngle += 360;
        float challengedRot = Status.GetRotationAngle();

        challengedAngle += challengedRot;
        challengedAngle %= 360;

        return challengedAngle;
    }

    private void SetTargetSpeed(float speed, float deceleration, float acceleration)
    {
        TargetSpeed = speed;
        if (speed < CurrentSpeed)
        {
            Acceleration = -deceleration;
        }
        else if (speed > CurrentSpeed)
        {
            Acceleration = acceleration;
        }
        else
        {
            Acceleration = 0;
        }
    }

    private void SetTargetSpeed(float speed)
    {
        SetTargetSpeed(speed, EntryCar.AiDeceleration, EntryCar.AiAcceleration);
    }

    public bool IsPlayerCarInBoundingArea(Vector3 center, float radius)
    {
        foreach (var playerCar in _entryCarManager.EntryCars)
        {
            if (playerCar.Client?.HasSentFirstUpdate == true)
            {
                float distance = Vector3.Distance(playerCar.Status.Position, center);
                if (distance < radius)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private Vector3 CalculateScare(Vector3 deviatedPosition, Vector3 right, CatmullRom.CatmullRomPoint smoothPos, long dt)
    {
        bool playerNearby = IsPlayerCarInBoundingArea(Status.Position, 3f);

        float scareFactor = 0f;
        float moveDir = _lastMoveDir;

        if (playerNearby)
        {
            EntryCar? closestPlayer = null;
            float minDist = float.MaxValue;
            foreach (var playerCar in _entryCarManager.EntryCars)
            {
                if (playerCar.Client?.HasSentFirstUpdate == true)
                {
                    float dist = Vector3.Distance(playerCar.Status.Position, Status.Position);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        closestPlayer = playerCar;
                    }
                }
            }

            if (closestPlayer != null)
            {
                Vector3 toPlayer = Vector3.Normalize(closestPlayer.Status.Position - smoothPos.Position);
                float sideDot = Vector3.Dot(right, toPlayer);

                scareFactor = Math.Clamp((8.0f - minDist) / 8.0f, 0, 1) * Math.Clamp((closestPlayer.Status.Velocity.Length() - 40.0f) / 30.0f, 0, 1);

                moveDir = sideDot > 0 ? -1f : 1f;
            }
        }

        if (scareFactor > 0.01f)
        {
            if (!_isScared && Random.Shared.Next(25) == 0)
            {
                _isScared = true;
                _scareTarget = scareFactor;
                _lastMoveDir = moveDir;
            }
            else
            {
                _scareTarget = MathF.Max(_scareTarget, scareFactor);
            }
        }
        else
        {
            if (_isScared)
            {
                _isScared = false;
                _scareTarget = 0f;
            }
        }

        float fadeSpeed = _isScared ? ScareFadeInSpeed : ScareFadeOutSpeed;
        if (_scareFade < _scareTarget)
            _scareFade = MathF.Min(_scareTarget, _scareFade + (dt / 1000.0f) / fadeSpeed);
        else if (_scareFade > _scareTarget)
            _scareFade = MathF.Max(_scareTarget, _scareFade - (dt / 1000.0f) / fadeSpeed);

        if (_scareFade > 0.001f)
        {
            float scareOffset = Math.Clamp(_scareFade * 6f, 0, 2f);
            deviatedPosition += right * _lastMoveDir * scareOffset;
        }

        return deviatedPosition;
    }

    private void TryLaneChange(bool direction)
    {
        try
        {
            if (IsCarNextToMe())
                return;

            var currentLanes = _spline.GetLanes(CurrentSplinePointId);

            var currentLane = _spline.GetLaneIndex(CurrentSplinePointId);

            if (currentLanes.Length <= 1 || _isChangingLane)
                return;

            var targetLane = direction ? currentLane + 1 : currentLane - 1;

            var nextPoint = _junctionEvaluator.Next(CurrentSplinePointId);
            if (nextPoint < 0)
                return;

            var nextLanes = _spline.GetLanes(nextPoint);
            if (_spline.GetLaneIndex(CurrentSplinePointId) >= nextLanes.Length || targetLane >= nextLanes.Length)
                return; 

            float t = _currentVecProgress / _currentVecLength;

            int currentLaneId = currentLanes[currentLane];
            int targetLaneId = currentLanes[targetLane];
            int nextCurrentLaneId = nextLanes[currentLane];
            int nextTargetLaneId = nextLanes[targetLane];

            Vector3 startPos = Vector3.Lerp(_spline.Points[currentLaneId].Position, _spline.Points[nextCurrentLaneId].Position, t);
            Vector3 targetPos = Vector3.Lerp(_spline.Points[targetLaneId].Position, _spline.Points[nextTargetLaneId].Position, t);

            _isChangingLane = true;
            _laneChangeStartIndex = currentLane;
            _laneChangeTargetIndex = targetLane;
            _laneChangeProgress = 0f;

            if (direction)
            {
                _indicator = CarStatusFlags.IndicateRight;
            } else
            {
                _indicator = CarStatusFlags.IndicateLeft;
            }

        } catch (IndexOutOfRangeException e)
        {
            return;
        }

    }

    private void SetNextLaneChangeCooldown()
    {
        _laneChangeCooldownMs = Random.Shared.Next(MinLaneChangeCooldownMs, MaxLaneChangeCooldownMs + 1);
    }

    private void HandlePlayerFlash()
    {
        foreach (var playerCar in _entryCarManager.EntryCars)
        {
            if (playerCar.Client?.HasSentFirstUpdate == true)
            {
                // check whether the player is within 15 meters behind the ai and limit the distance left to right be 5 meters
                float distance = Vector3.Distance(playerCar.Status.Position, Status.Position);
                if (distance < 18f)
                {
                    float angle = GetAngleToCar(playerCar.Status);
                    if (angle > 350 || angle < 10)
                    {
                        // Use a unique identifier for each player, e.g., SessionId
                        byte sessionId = playerCar.SessionId; // Replace with actual session id property

                        if (!_playerFlashStates.TryGetValue(sessionId, out var flashInfo))
                        {
                            flashInfo = new FlashInfo();
                            _playerFlashStates[sessionId] = flashInfo;
                        }

                        bool highBeamsOn = !playerCar.Status.StatusFlag.HasFlag(CarStatusFlags.HighBeamsOff);
                        long now = _sessionManager.ServerTimeMilliseconds;

                        if (flashInfo.FlashCount > 0 && now - flashInfo.FirstFlashTime > FlashWindowMs)
                        {
                            flashInfo.FlashCount = 0;
                            flashInfo.FirstFlashTime = 0;
                        }

                        if (highBeamsOn && !flashInfo.LastHighBeamsOn)
                        {
                            if (flashInfo.FlashCount == 0)
                            {
                                flashInfo.FirstFlashTime = now;
                            }
                            flashInfo.FlashCount++;


                            if (flashInfo.FlashCount >= RequiredFlashes && now - flashInfo.FirstFlashTime <= FlashWindowMs)
                            {
                                //Log.Information("AI {SessionId} detected player {PlayerSessionId} flashing high beams", EntryCar.SessionId, sessionId);

                                bool direction = Random.Shared.Next(2) == 0;
                                TryLaneChange(direction);

                                // Reset after reaction
                                flashInfo.FlashCount = 0;
                                flashInfo.FirstFlashTime = 0;
                            }
                        }

                        flashInfo.LastHighBeamsOn = highBeamsOn;
                    }
                }
            }
        }
    }

    public bool IsCarNextToMe()
    {
        var lanes = _spline.GetLanes(CurrentSplinePointId);
        if (lanes.Length <= 1)
            return false;

        int currentLaneIndex = _spline.GetLaneIndex(CurrentSplinePointId);
        int leftLaneIndex = currentLaneIndex - 1;
        int rightLaneIndex = currentLaneIndex + 1;

        if (leftLaneIndex >= 0 && _spline.SlowestAiStates[lanes[leftLaneIndex]] != null)
            return true;

        if (rightLaneIndex < lanes.Length && _spline.SlowestAiStates[lanes[rightLaneIndex]] != null)
            return true;

        return false;
    }

    public void Update()
    {
        if (!Initialized)
            return;

        var ops = _spline.Operations;

        long currentTime = _sessionManager.ServerTimeMilliseconds;
        long dt = currentTime - _lastTick;
        _lastTick = currentTime;

        if (Acceleration != 0)
        {
            CurrentSpeed += Acceleration * (dt / 1000.0f);

            if ((Acceleration < 0 && CurrentSpeed < TargetSpeed) || (Acceleration > 0 && CurrentSpeed > TargetSpeed))
            {
                CurrentSpeed = TargetSpeed;
                Acceleration = 0;
            }
        }

        if (IsCarNextToMe())
        {
            Log.Information("There is a AI Car Next to AI {SessionId}", EntryCar.SessionId);
        }

        float moveMeters = (dt / 1000.0f) * CurrentSpeed;
        if (!Move(_currentVecProgress + moveMeters) || !_junctionEvaluator.TryNext(CurrentSplinePointId, out var nextPoint))
        {
            Log.Debug("Car {SessionId} reached spline end, despawning", EntryCar.SessionId);
            Despawn();
            return;
        }

        CatmullRom.CatmullRomPoint smoothPos = CatmullRom.Evaluate(ops.Points[CurrentSplinePointId].Position,
                ops.Points[nextPoint].Position,
                _startTangent,
                _endTangent,
                _currentVecProgress / _currentVecLength);

        float deviation = MathF.Sin(_laneDeviationPhase + currentTime * 0.001f * _laneDeviationSpeed * MathF.Tau) * _laneDeviationAmplitude;

        Vector3 forward = Vector3.Normalize(ops.Points[nextPoint].Position - ops.Points[CurrentSplinePointId].Position);
        forward = Vector3.Normalize(forward); // ensure normalization
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));

        HandlePlayerFlash();

        if (!_isChangingLane && _sessionManager.ServerTimeMilliseconds - _lastLaneChangeTime > _laneChangeCooldownMs)
        {
            // Random chance to consider a lane change
            if (Random.Shared.NextDouble() < 0.01)
            {
                // Randomly pick left or right
                bool direction = Random.Shared.Next(2) == 0;
                TryLaneChange(direction);
                _lastLaneChangeTime = _sessionManager.ServerTimeMilliseconds;
                SetNextLaneChangeCooldown();
            }
        }

        // Apply deviation to the position
        Vector3 deviatedPosition = smoothPos.Position;
        if (!_isChangingLane)
            deviatedPosition += right * deviation;

        // Handle lane change interpolation
        if (_isChangingLane)
        {
            _laneChangeProgress += (dt / 1000.0f) / _laneChangeDuration;
            _laneChangeProgress = MathF.Min(_laneChangeProgress, 1.0f);

            var lanes = _spline.GetLanes(CurrentSplinePointId);

            // Bounds check
            if (_laneChangeStartIndex < 0 || _laneChangeStartIndex >= lanes.Length ||
                _laneChangeTargetIndex < 0 || _laneChangeTargetIndex >= lanes.Length)
            {
                // Abort lane change if indices are invalid
                _isChangingLane = false;
            }
            else
            {
                Vector3 startLanePos = _spline.Points[lanes[_laneChangeStartIndex]].Position;
                Vector3 targetLanePos = _spline.Points[lanes[_laneChangeTargetIndex]].Position;

                // Interpolate laterally between lanes
                deviatedPosition = Vector3.Lerp(startLanePos, targetLanePos, _laneChangeProgress)
                    + smoothPos.Tangent * (_currentVecProgress / _currentVecLength) * _currentVecLength;

                if (_laneChangeProgress >= 1.0f)
                {
                    // Finish lane change
                    _isChangingLane = false;
                    _currentLaneIndex = _laneChangeTargetIndex;
                    CurrentSplinePointId = lanes[_currentLaneIndex];
                    _indicator = 0;
                    _laneDeviationPhase = Random.Shared.NextSingle() * MathF.Tau;
                    //Log.Debug("AI {SessionId} completed lane change to {Lane}", EntryCar.SessionId, _currentLaneIndex);
                }
            }
        }

        Vector3 rotation = new Vector3
        {
            X = MathF.Atan2(smoothPos.Tangent.Z, smoothPos.Tangent.X) - MathF.PI / 2,
            Y = (MathF.Atan2(new Vector2(smoothPos.Tangent.Z, smoothPos.Tangent.X).Length(), smoothPos.Tangent.Y) - MathF.PI / 2) * -1f,
            Z = ops.GetCamber(CurrentSplinePointId, _currentVecProgress / _currentVecLength)
        };

        float tyreAngularSpeed = GetTyreAngularSpeed(CurrentSpeed, EntryCar.TyreDiameterMeters);
        byte encodedTyreAngularSpeed = (byte)(Math.Clamp(MathF.Round(MathF.Log10(tyreAngularSpeed + 1.0f) * 20.0f) * Math.Sign(tyreAngularSpeed), -100.0f, 154.0f) + 100.0f);

        deviatedPosition = CalculateScare(deviatedPosition, right, smoothPos, dt);

        Status.Timestamp = _sessionManager.ServerTimeMilliseconds;
        Status.Position = deviatedPosition with { Y = smoothPos.Position.Y + EntryCar.AiSplineHeightOffsetMeters };
        Status.Rotation = rotation;
        Status.Velocity = smoothPos.Tangent * CurrentSpeed;
        Status.SteerAngle = 127;
        Status.WheelAngle = 127;
        Status.TyreAngularSpeed[0] = encodedTyreAngularSpeed;
        Status.TyreAngularSpeed[1] = encodedTyreAngularSpeed;
        Status.TyreAngularSpeed[2] = encodedTyreAngularSpeed;
        Status.TyreAngularSpeed[3] = encodedTyreAngularSpeed;
        Status.EngineRpm = (ushort)MathUtils.Lerp(EntryCar.AiIdleEngineRpm, EntryCar.AiMaxEngineRpm, CurrentSpeed / _configuration.Extra.AiParams.MaxSpeedMs);
        Status.StatusFlag = CarStatusFlags.LightsOn
                            | CarStatusFlags.HighBeamsOff
                            | (_sessionManager.ServerTimeMilliseconds < _stoppedForCollisionUntil || CurrentSpeed < 20 / 3.6f ? CarStatusFlags.HazardsOn : 0)
                            | (CurrentSpeed == 0 || Acceleration < 0 ? CarStatusFlags.BrakeLightsOn : 0)
                            | (_stoppedForObstacle && _sessionManager.ServerTimeMilliseconds > _obstacleHonkStart && _sessionManager.ServerTimeMilliseconds < _obstacleHonkEnd ? CarStatusFlags.Horn : 0)
                            | GetWiperSpeed(_weatherManager.CurrentWeather.RainIntensity)
                            | _indicator;
        Status.Gear = 2;
    }

    private static float GetTyreAngularSpeed(float speed, float wheelDiameter)
    {
        return speed / (MathF.PI * wheelDiameter) * 6;
    }

    private static CarStatusFlags GetWiperSpeed(float rainIntensity)
    {
        return rainIntensity switch
        {
            < 0.05f => 0,
            < 0.25f => CarStatusFlags.WiperLevel1,
            < 0.5f => CarStatusFlags.WiperLevel2,
            _ => CarStatusFlags.WiperLevel3
        };
    }
}
