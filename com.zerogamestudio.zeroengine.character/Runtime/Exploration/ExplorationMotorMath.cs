using UnityEngine;

namespace ZeroEngine.Character.Exploration
{
    public static class ExplorationMotorMath
    {
        public static ExplorationLocomotionMode ResolveLocomotion(
            bool canMove,
            float inputMagnitude,
            float inputDeadZone,
            bool sprintHeld,
            bool canSprint)
        {
            if (!canMove || inputMagnitude <= Mathf.Max(0f, inputDeadZone))
            {
                return ExplorationLocomotionMode.Idle;
            }

            return sprintHeld && canSprint
                ? ExplorationLocomotionMode.Run
                : ExplorationLocomotionMode.Walk;
        }

        public static float GetTargetSpeed(
            ExplorationLocomotionMode locomotion,
            float walkSpeed,
            float runSpeed)
        {
            return locomotion switch
            {
                ExplorationLocomotionMode.Walk => Mathf.Max(0f, walkSpeed),
                ExplorationLocomotionMode.Run => Mathf.Max(0f, runSpeed),
                _ => 0f
            };
        }

        public static float IntegrateVerticalVelocity(
            float currentVelocity,
            bool isGrounded,
            float groundStickVelocity,
            float gravity,
            float maxFallSpeed,
            float deltaTime)
        {
            if (isGrounded && currentVelocity < 0f)
            {
                return groundStickVelocity;
            }

            var integrated = currentVelocity + gravity * Mathf.Max(0f, deltaTime);
            return Mathf.Max(integrated, -Mathf.Abs(maxFallSpeed));
        }

        public static int GetSubstepCount(
            float displacementMagnitude,
            float maxDisplacementPerStep,
            int maxSubsteps)
        {
            var safeMaxSubsteps = Mathf.Max(1, maxSubsteps);
            if (displacementMagnitude <= 0f || maxDisplacementPerStep <= 0f)
            {
                return 1;
            }

            return Mathf.Clamp(
                Mathf.CeilToInt(displacementMagnitude / maxDisplacementPerStep),
                1,
                safeMaxSubsteps);
        }

        public static bool IsBlocked(
            Vector3 requestedVelocity,
            Vector3 actualVelocity,
            float minimumRequestedSpeed,
            float maximumActualRatio)
        {
            requestedVelocity.y = 0f;
            actualVelocity.y = 0f;
            var requestedSpeed = requestedVelocity.magnitude;
            if (requestedSpeed < Mathf.Max(0f, minimumRequestedSpeed))
            {
                return false;
            }

            var actualRatio = actualVelocity.magnitude / Mathf.Max(requestedSpeed, Mathf.Epsilon);
            return actualRatio <= Mathf.Clamp01(maximumActualRatio);
        }
    }
}
