using UnityEngine;

namespace ZeroEngine.Character.Exploration
{
    public enum ExplorationControlMode
    {
        Disabled = 0,
        Loading = 1,
        Player = 2,
        Scripted = 3,
        Follower = 4,
        Paused = 5,
        Recovering = 6
    }

    public enum ExplorationMovementAuthority
    {
        None = 0,
        Player = 1,
        Scripted = 2,
        Follower = 3,
        Recovery = 4
    }

    public enum ExplorationLocomotionMode
    {
        Idle = 0,
        Walk = 1,
        Run = 2
    }

    public enum Facing8
    {
        North = 0,
        NorthEast = 1,
        East = 2,
        SouthEast = 3,
        South = 4,
        SouthWest = 5,
        West = 6,
        NorthWest = 7
    }

    public enum VisualFacing4
    {
        North = 0,
        East = 1,
        South = 2,
        West = 3
    }

    public enum VisualDirectionMode
    {
        Four = 4,
        Eight = 8
    }

    public enum FourDirectionTieBreakAxis
    {
        Vertical = 0,
        Horizontal = 1
    }

    public enum ExplorationActionOverlay
    {
        None = 0,
        Interact = 1,
        ScriptedAction = 2
    }

    public enum ExplorationRecoveryReason
    {
        None = 0,
        BelowCellThreshold = 1,
        RecoveryVolume = 2,
        InvalidPlacement = 3,
        InvalidContinuePose = 4,
        WorldHandoffFailure = 5,
        ManualRequest = 6
    }

    public readonly struct ExplorationMovementCommand
    {
        public ExplorationMovementCommand(
            long sequence,
            ExplorationMovementAuthority authority,
            Vector2 rawInput,
            Vector2 processedInput,
            bool sprintHeld,
            ExplorationActionOverlay action)
        {
            Sequence = sequence;
            Authority = authority;
            RawInput = rawInput;
            ProcessedInput = processedInput;
            SprintHeld = sprintHeld;
            Action = action;
        }

        public long Sequence { get; }
        public ExplorationMovementAuthority Authority { get; }
        public Vector2 RawInput { get; }
        public Vector2 ProcessedInput { get; }
        public bool SprintHeld { get; }
        public ExplorationActionOverlay Action { get; }
    }

    public readonly struct ExplorationGroundSnapshot
    {
        public ExplorationGroundSnapshot(
            bool isGrounded,
            Vector3 point,
            Vector3 normal,
            float slopeDegrees,
            int surfaceLayer)
        {
            IsGrounded = isGrounded;
            Point = point;
            Normal = normal;
            SlopeDegrees = slopeDegrees;
            SurfaceLayer = surfaceLayer;
        }

        public bool IsGrounded { get; }
        public Vector3 Point { get; }
        public Vector3 Normal { get; }
        public float SlopeDegrees { get; }
        public int SurfaceLayer { get; }

        public static ExplorationGroundSnapshot Airborne =>
            new(false, Vector3.zero, Vector3.up, 0f, -1);
    }

    public readonly struct ExplorationSafePose
    {
        public ExplorationSafePose(
            bool isValid,
            string cellId,
            Vector3 position,
            Quaternion rotation,
            Facing8 facing)
        {
            IsValid = isValid;
            CellId = cellId ?? string.Empty;
            Position = position;
            Rotation = rotation;
            Facing = facing;
        }

        public bool IsValid { get; }
        public string CellId { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public Facing8 Facing { get; }

        public static ExplorationSafePose Invalid =>
            new(false, string.Empty, Vector3.zero, Quaternion.identity, Facing8.South);
    }

    public readonly struct ExplorationMotorSnapshot
    {
        public ExplorationMotorSnapshot(
            long sequence,
            ExplorationControlMode controlMode,
            ExplorationMovementAuthority authority,
            ExplorationLocomotionMode locomotion,
            Facing8 facing,
            ExplorationActionOverlay action,
            Vector2 rawInput,
            Vector2 processedInput,
            Vector3 requestedVelocity,
            Vector3 actualVelocity,
            float verticalVelocity,
            ExplorationGroundSnapshot ground,
            bool isBlocked,
            ExplorationRecoveryReason lastRecoveryReason)
        {
            Sequence = sequence;
            ControlMode = controlMode;
            Authority = authority;
            Locomotion = locomotion;
            Facing = facing;
            Action = action;
            RawInput = rawInput;
            ProcessedInput = processedInput;
            RequestedVelocity = requestedVelocity;
            ActualVelocity = actualVelocity;
            VerticalVelocity = verticalVelocity;
            Ground = ground;
            IsBlocked = isBlocked;
            LastRecoveryReason = lastRecoveryReason;
        }

        public long Sequence { get; }
        public ExplorationControlMode ControlMode { get; }
        public ExplorationMovementAuthority Authority { get; }
        public ExplorationLocomotionMode Locomotion { get; }
        public Facing8 Facing { get; }
        public ExplorationActionOverlay Action { get; }
        public Vector2 RawInput { get; }
        public Vector2 ProcessedInput { get; }
        public Vector3 RequestedVelocity { get; }
        public Vector3 ActualVelocity { get; }
        public float VerticalVelocity { get; }
        public ExplorationGroundSnapshot Ground { get; }
        public bool IsBlocked { get; }
        public ExplorationRecoveryReason LastRecoveryReason { get; }
    }
}
