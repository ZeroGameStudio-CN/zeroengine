// JumpMovementHandler.cs
// 跳跃轨迹计算器
// 使用抛物线物理公式计算跳跃可达性和所需速度

using UnityEngine;

namespace ZeroEngine.Pathfinding2D
{
    /// <summary>
    /// 跳跃计算结果
    /// </summary>
    public struct JumpCalculationResult
    {
        /// <summary>是否可达</summary>
        public bool IsReachable;

        /// <summary>所需的 Y 方向初速度</summary>
        public float VelocityY;

        /// <summary>所需的 X 方向初速度</summary>
        public float VelocityX;

        /// <summary>预计飞行时间</summary>
        public float FlightTime;

        /// <summary>最高点高度</summary>
        public float MaxHeight;

        /// <summary>跳跃轨迹（用于碰撞检测）</summary>
        public Vector2[] Trajectory;

        /// <summary>不可达结果</summary>
        public static JumpCalculationResult NotReachable => new JumpCalculationResult { IsReachable = false };
    }

    /// <summary>
    /// 跳跃轨迹计算器
    /// 使用抛物线物理公式计算跳跃可达性
    /// </summary>
    public static class JumpMovementHandler
    {
        // 默认参数
        private const float DefaultGravity = 9.81f;
        private const float DefaultGravityScale = 3f;
        private const float MinJumpTime = 0.1f;
        private const float MaxJumpTime = 2f;
        private const int TrajectoryPoints = 20;

        /// <summary>
        /// 计算从起点跳跃到终点所需的速度
        /// </summary>
        /// <param name="start">起点位置</param>
        /// <param name="end">终点位置</param>
        /// <param name="maxJumpVelocity">最大跳跃初速度</param>
        /// <param name="gravityScale">重力缩放（Rigidbody2D.gravityScale）</param>
        /// <param name="overshoot">过冲系数（1.0 = 刚好到达）</param>
        /// <returns>跳跃计算结果</returns>
        public static JumpCalculationResult CalculateJump(
            Vector2 start,
            Vector2 end,
            float maxJumpVelocity,
            float gravityScale = DefaultGravityScale,
            float overshoot = 1.2f,
            float maxHorizontalSpeed = 0f)
        {
            float deltaX = end.x - start.x;
            float deltaY = end.y - start.y;
            float gravity = DefaultGravity * gravityScale;

            // 如果目标在下方，直接下落即可
            if (deltaY < -0.5f && Mathf.Abs(deltaX) < 2f)
            {
                float fallTime = Mathf.Sqrt(2f * Mathf.Abs(deltaY) / gravity);
                float fallVelocityX = deltaX / fallTime;
                if (ExceedsHorizontalSpeed(fallVelocityX, maxHorizontalSpeed))
                {
                    return JumpCalculationResult.NotReachable;
                }

                return new JumpCalculationResult
                {
                    IsReachable = true,
                    VelocityY = 0f,
                    VelocityX = fallVelocityX,
                    FlightTime = fallTime,
                    MaxHeight = 0f
                };
            }

            // 计算所需的跳跃高度
            // 需要跳到比目标高一点，以便落到目标上
            float requiredHeight = deltaY > 0 ? deltaY * overshoot : Mathf.Max(0.5f, Mathf.Abs(deltaX) * 0.3f);

            // 计算所需的初始 Y 速度: v = sqrt(2 * g * h)
            float requiredVelocityY = Mathf.Sqrt(2f * gravity * requiredHeight);

            // 检查是否超过最大跳跃能力
            if (requiredVelocityY > maxJumpVelocity)
            {
                return JumpCalculationResult.NotReachable;
            }

            float selectedHeight = SelectJumpHeightForHorizontalSpeed(
                requiredHeight,
                deltaX,
                deltaY,
                gravity,
                maxJumpVelocity,
                maxHorizontalSpeed);
            if (selectedHeight < 0f)
            {
                return JumpCalculationResult.NotReachable;
            }

            requiredVelocityY = Mathf.Sqrt(2f * gravity * selectedHeight);
            float totalTime = CalculateFlightTime(selectedHeight, deltaY, gravity);

            // 限制飞行时间
            if (totalTime < MinJumpTime || totalTime > MaxJumpTime)
            {
                return JumpCalculationResult.NotReachable;
            }

            // 计算所需的 X 速度
            float requiredVelocityX = deltaX / totalTime;

            if (ExceedsHorizontalSpeed(requiredVelocityX, maxHorizontalSpeed))
            {
                return JumpCalculationResult.NotReachable;
            }

            // 生成轨迹点
            var trajectory = GenerateTrajectory(start, requiredVelocityX, requiredVelocityY, gravity, totalTime);

            return new JumpCalculationResult
            {
                IsReachable = true,
                VelocityY = requiredVelocityY,
                VelocityX = requiredVelocityX,
                FlightTime = totalTime,
                MaxHeight = selectedHeight,
                Trajectory = trajectory
            };
        }

        private static float SelectJumpHeightForHorizontalSpeed(
            float minimumHeight,
            float deltaX,
            float deltaY,
            float gravity,
            float maxJumpVelocity,
            float maxHorizontalSpeed)
        {
            float maxHeight = maxJumpVelocity * maxJumpVelocity / (2f * gravity);
            if (minimumHeight > maxHeight)
            {
                return -1f;
            }

            float minimumTime = CalculateFlightTime(minimumHeight, deltaY, gravity);
            if (minimumTime <= 0f)
            {
                return -1f;
            }

            if (!ExceedsHorizontalSpeed(deltaX / minimumTime, maxHorizontalSpeed))
            {
                return minimumHeight;
            }

            float maxTime = CalculateFlightTime(maxHeight, deltaY, gravity);
            if (maxTime <= 0f || ExceedsHorizontalSpeed(deltaX / maxTime, maxHorizontalSpeed))
            {
                return -1f;
            }

            float low = minimumHeight;
            float high = maxHeight;
            for (int i = 0; i < 12; i++)
            {
                float mid = (low + high) * 0.5f;
                float midTime = CalculateFlightTime(mid, deltaY, gravity);
                float midVelocityX = deltaX / midTime;

                if (ExceedsHorizontalSpeed(midVelocityX, maxHorizontalSpeed))
                    low = mid;
                else
                    high = mid;
            }

            return high;
        }

        private static float CalculateFlightTime(float jumpHeight, float deltaY, float gravity)
        {
            float fallHeight = jumpHeight - deltaY;
            if (fallHeight < 0f)
                return -1f;

            float timeToApex = Mathf.Sqrt(2f * jumpHeight / gravity);
            float timeToFall = Mathf.Sqrt(2f * fallHeight / gravity);
            return timeToApex + timeToFall;
        }

        private static bool ExceedsHorizontalSpeed(float velocityX, float maxHorizontalSpeed)
        {
            return maxHorizontalSpeed > 0f && Mathf.Abs(velocityX) > maxHorizontalSpeed;
        }

        /// <summary>
        /// 验证跳跃轨迹是否有障碍物阻挡
        /// </summary>
        /// <param name="trajectory">轨迹点数组</param>
        /// <param name="obstacleMask">障碍物层</param>
        /// <param name="colliderRadius">碰撞体半径</param>
        /// <returns>是否通畅</returns>
        public static bool ValidateTrajectory(Vector2[] trajectory, LayerMask obstacleMask, float colliderRadius = 0.3f)
        {
            if (trajectory == null || trajectory.Length < 2)
                return false;

            for (int i = 0; i < trajectory.Length - 1; i++)
            {
                Vector2 from = trajectory[i];
                Vector2 to = trajectory[i + 1];
                float distance = Vector2.Distance(from, to);

                RaycastHit2D hit = Physics2D.CircleCast(from, colliderRadius, (to - from).normalized, distance, obstacleMask);
                if (hit.collider != null)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 计算自由落体到目标的时间和水平速度
        /// </summary>
        public static JumpCalculationResult CalculateFall(
            Vector2 start,
            Vector2 end,
            float gravityScale = DefaultGravityScale,
            float maxHorizontalSpeed = 0f)
        {
            float deltaX = end.x - start.x;
            float deltaY = start.y - end.y; // 正值表示下落高度
            float gravity = DefaultGravity * gravityScale;

            if (deltaY <= 0)
            {
                return JumpCalculationResult.NotReachable;
            }

            // 自由落体时间: t = sqrt(2h/g)
            float fallTime = Mathf.Sqrt(2f * deltaY / gravity);
            float velocityX = deltaX / fallTime;
            if (ExceedsHorizontalSpeed(velocityX, maxHorizontalSpeed))
            {
                return JumpCalculationResult.NotReachable;
            }

            return new JumpCalculationResult
            {
                IsReachable = true,
                VelocityY = 0f,
                VelocityX = velocityX,
                FlightTime = fallTime,
                MaxHeight = 0f
            };
        }

        /// <summary>
        /// 生成跳跃轨迹点
        /// </summary>
        private static Vector2[] GenerateTrajectory(Vector2 start, float vx, float vy, float gravity, float totalTime)
        {
            var points = new Vector2[TrajectoryPoints];
            float dt = totalTime / (TrajectoryPoints - 1);

            for (int i = 0; i < TrajectoryPoints; i++)
            {
                float t = dt * i;
                // x(t) = x0 + vx * t
                // y(t) = y0 + vy * t - 0.5 * g * t^2
                float x = start.x + vx * t;
                float y = start.y + vy * t - 0.5f * gravity * t * t;
                points[i] = new Vector2(x, y);
            }

            return points;
        }

        /// <summary>
        /// 估算行走时间
        /// </summary>
        public static float EstimateWalkTime(Vector2 start, Vector2 end, float walkSpeed)
        {
            return Vector2.Distance(start, end) / walkSpeed;
        }

        /// <summary>
        /// 检查是否可以直接行走到目标（同一平台）
        /// 增加容错：允许1次检测失败，多高度射线检测
        /// </summary>
        public static bool CanWalkTo(Vector2 start, Vector2 end, LayerMask groundMask, float maxHeightDiff = 0.5f)
        {
            // 高度差太大不能行走
            if (Mathf.Abs(end.y - start.y) > maxHeightDiff)
            {
                return false;
            }

            // 检查中间是否有间隙
            float distance = Vector2.Distance(start, end);
            int checkCount = Mathf.CeilToInt(distance / 0.5f);

            int missCount = 0;
            const int maxMissAllowed = 1;  // 允许1次检测失败，增加容错

            for (int i = 0; i <= checkCount; i++)
            {
                float t = (float)i / checkCount;
                Vector2 checkPos = Vector2.Lerp(start, end, t);

                // 多高度射线检测，增加鲁棒性
                bool foundGround = false;
                for (float offset = 0.3f; offset <= 1.0f; offset += 0.3f)
                {
                    RaycastHit2D hit = Physics2D.Raycast(
                        checkPos + Vector2.up * offset,
                        Vector2.down,
                        offset + 0.5f,
                        groundMask
                    );

                    if (hit.collider != null)
                    {
                        foundGround = true;
                        break;
                    }
                }

                if (!foundGround)
                {
                    missCount++;
                    if (missCount > maxMissAllowed)
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 验证跳跃轨迹是否有障碍物阻挡（排除起点和终点平台）
        /// 改进版：忽略起跳初期的短距离遮挡（解决突出平台问题）
        /// </summary>
        /// <param name="trajectory">轨迹点数组</param>
        /// <param name="obstacleMask">障碍物层</param>
        /// <param name="colliderRadius">碰撞体半径</param>
        /// <param name="fromPlatform">起点平台碰撞体（排除）</param>
        /// <param name="toPlatform">终点平台碰撞体（排除）</param>
        /// <param name="ignoreInitialDistance">忽略起跳初期的检测距离（默认0.8m）</param>
        /// <returns>是否通畅</returns>
        public static bool ValidateTrajectory(
            Vector2[] trajectory,
            LayerMask obstacleMask,
            float colliderRadius,
            Collider2D fromPlatform,
            Collider2D toPlatform,
            float ignoreInitialDistance = 0.8f)
        {
            if (trajectory == null || trajectory.Length < 2)
                return false;

            Vector2 startPos = trajectory[0];
            Vector2 endPos = trajectory[trajectory.Length - 1];
            float totalTrajectoryDistance = CalculateTrajectoryDistance(trajectory);
            float endpointTolerance = Mathf.Max(colliderRadius * 2f + 0.1f, 0.35f);
            float traveledDistance = 0f;

            for (int i = 0; i < trajectory.Length - 1; i++)
            {
                Vector2 from = trajectory[i];
                Vector2 to = trajectory[i + 1];
                float segmentDist = Vector2.Distance(from, to);

                // 忽略起跳初期的检测（解决突出平台下方起跳被阻挡的问题）
                // 在起跳的前 ignoreInitialDistance 米内，不进行碰撞检测
                if (traveledDistance < ignoreInitialDistance)
                {
                    traveledDistance += segmentDist;
                    continue;
                }

                var hits = Physics2D.CircleCastAll(
                    from, colliderRadius, (to - from).normalized, segmentDist, obstacleMask);

                for (int hitIndex = 0; hitIndex < hits.Length; hitIndex++)
                {
                    var hit = hits[hitIndex];
                    if (hit.collider == null)
                        continue;

                    float hitPathDistance = traveledDistance + hit.distance;
                    if (ShouldIgnoreEndpointPlatformHit(
                            hit,
                            fromPlatform,
                            toPlatform,
                            startPos,
                            endPos,
                            hitPathDistance,
                            totalTrajectoryDistance,
                            endpointTolerance,
                            ignoreInitialDistance))
                    {
                        continue;
                    }

                    // 额外检查：如果碰撞点在起点附近（1.5m内），且是侧面擦边（非正面撞头），忽略
                    // 这处理了突出平台底部"擦边"起跳的情况
                    // 修复：如果碰撞点在起点正上方（水平距离很小），说明会撞头，不应忽略
                    float distFromStart = Vector2.Distance(startPos, hit.point);
                    float horizontalDistFromStart = Mathf.Abs(hit.point.x - startPos.x);
                    if (distFromStart < 1.5f && hit.point.y > startPos.y && horizontalDistFromStart > 0.5f)
                    {
                        // 只有侧面擦边才忽略（水平距离 > 0.5m）
                        continue;
                    }

                    return false;
                }

                traveledDistance += segmentDist;
            }
            return true;
        }

        private static float CalculateTrajectoryDistance(Vector2[] trajectory)
        {
            float distance = 0f;
            for (int i = 0; i < trajectory.Length - 1; i++)
            {
                distance += Vector2.Distance(trajectory[i], trajectory[i + 1]);
            }

            return distance;
        }

        private static bool ShouldIgnoreEndpointPlatformHit(
            RaycastHit2D hit,
            Collider2D fromPlatform,
            Collider2D toPlatform,
            Vector2 startPos,
            Vector2 endPos,
            float hitPathDistance,
            float totalTrajectoryDistance,
            float endpointTolerance,
            float ignoreInitialDistance)
        {
            bool isFromPlatform = hit.collider == fromPlatform;
            bool isToPlatform = hit.collider == toPlatform;

            if (isFromPlatform || isToPlatform)
            {
                float startTolerance = Mathf.Max(ignoreInitialDistance, endpointTolerance);
                float remainingDistance = totalTrajectoryDistance - hitPathDistance;
                bool nearStart = isFromPlatform &&
                                 (hitPathDistance <= startTolerance ||
                                  Vector2.Distance(hit.point, startPos) <= startTolerance);
                bool nearEnd = isToPlatform &&
                               (remainingDistance <= endpointTolerance ||
                                Vector2.Distance(hit.point, endPos) <= endpointTolerance ||
                                Vector2.Distance(hit.centroid, endPos) <= endpointTolerance);

                return nearStart || nearEnd;
            }

            return false;
        }
    }
}
