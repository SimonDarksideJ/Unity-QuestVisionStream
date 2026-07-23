// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System.Collections.Generic;
using Ethar.UXTraining.Settings;
using UnityEngine;

namespace Ethar.UXTraining.Components
{
    /// <summary>
    /// Places a world-space UX window (startup card, training step form) per the
    /// configured <see cref="WindowPlacementMode"/>:
    ///
    ///   - <b>Fixed</b> — the window is anchored in front of the user whenever the
    ///     owner calls <see cref="Reanchor"/> (step change, re-present) and then
    ///     stays put. With <see cref="autoRecover"/> it also glides back into view
    ///     if the user leaves it far behind (used by the startup card, which is
    ///     built before tracking settles).
    ///   - <b>HeadLocked</b> — the window lazily follows the user's view. It is
    ///     label-aware: while a registered world label is in view, the window's
    ///     direction is clamped so it never encroaches on the label — it slides to
    ///     a stop at the clearance boundary like a sliding door, and resumes the
    ///     smooth follow once the user looks back the other way.
    ///
    /// World labels register themselves as obstacles via
    /// <see cref="RegisterObstacle"/> / <see cref="UnregisterObstacle"/>
    /// (inactive obstacles are ignored).
    /// </summary>
    [AddComponentMenu("")]
    public sealed class WindowFollower : MonoBehaviour
    {
        private static readonly List<Transform> obstacles = new List<Transform>();

        private WindowPlacementMode mode = WindowPlacementMode.Fixed;
        private float distanceMeters = 1.25f;
        private float heightOffsetMeters;
        private float followSeconds = 0.3f;
        private float clearanceDegrees = 20f;
        private bool autoRecover;
        private bool anchored;
        private bool recovering;

        /// <summary>Make a world label (or any world UX) a no-go zone for head-locked windows.</summary>
        public static void RegisterObstacle(Transform label)
        {
            if (label != null && !obstacles.Contains(label))
            {
                obstacles.Add(label);
            }
        }

        public static void UnregisterObstacle(Transform label) => obstacles.Remove(label);

        /// <summary>Add (or reconfigure) a follower on <paramref name="window"/> from the shared settings.</summary>
        public static WindowFollower Attach(GameObject window, UxSettings settings,
            float distanceMeters, float heightOffsetMeters = 0f, bool autoRecover = false)
        {
            var follower = window.GetComponent<WindowFollower>();
            if (follower == null)
            {
                follower = window.AddComponent<WindowFollower>();
            }

            settings = settings != null ? settings : UxSettings.Defaults;
            follower.mode = settings.WindowPlacement;
            follower.distanceMeters = distanceMeters;
            follower.heightOffsetMeters = heightOffsetMeters;
            follower.followSeconds = Mathf.Max(0.01f, settings.WindowFollowSeconds);
            follower.clearanceDegrees = Mathf.Max(0f, settings.LabelClearanceDegrees);
            follower.autoRecover = autoRecover;
            return follower;
        }

        /// <summary>Snap the window to its desired pose now (show / re-present / step change).</summary>
        public void Reanchor()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            MoveTo(DesiredPosition(camera), camera);
            anchored = true;
            recovering = false;
        }

        private void OnDestroy() => obstacles.Remove(transform);

        private void LateUpdate()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            if (!anchored)
            {
                Reanchor();
                return;
            }

            if (mode == WindowPlacementMode.HeadLocked)
            {
                SmoothFollow(camera);
                return;
            }

            // Fixed: stay put, but optionally glide back when left far behind.
            if (autoRecover)
            {
                if (!recovering && IsLost(camera))
                {
                    recovering = true;
                }

                if (recovering)
                {
                    var arrived = SmoothFollow(camera);
                    recovering = !arrived;
                }
            }
        }

        /// <summary>Lazy exponential follow toward the (label-clamped) desired pose. True once settled.</summary>
        private bool SmoothFollow(Camera camera)
        {
            var desired = DesiredPosition(camera);
            var t = 1f - Mathf.Exp(-Time.deltaTime / followSeconds);
            MoveTo(Vector3.Lerp(transform.position, desired, t), camera);
            return (transform.position - desired).sqrMagnitude < 0.05f * 0.05f;
        }

        private void MoveTo(Vector3 position, Camera camera)
        {
            transform.position = position;
            var look = position - camera.transform.position;
            if (look.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(look);
            }
        }

        /// <summary>
        /// The window's target point: straight ahead at the configured distance,
        /// with its yaw clamped away from any active obstacle — the sliding-door
        /// stop at the clearance boundary.
        /// </summary>
        private Vector3 DesiredPosition(Camera camera)
        {
            var cameraPosition = camera.transform.position;
            var forward = camera.transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude < 0.001f ? Vector3.forward : forward.normalized;

            var desiredYaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;

            if (clearanceDegrees > 0f)
            {
                foreach (var obstacle in obstacles)
                {
                    if (obstacle == null || !obstacle.gameObject.activeInHierarchy || obstacle == transform)
                    {
                        continue;
                    }

                    var toObstacle = obstacle.position - cameraPosition;
                    toObstacle.y = 0f;
                    if (toObstacle.sqrMagnitude < 0.01f)
                    {
                        continue;
                    }

                    var obstacleYaw = Mathf.Atan2(toObstacle.x, toObstacle.z) * Mathf.Rad2Deg;
                    var delta = Mathf.DeltaAngle(obstacleYaw, desiredYaw);
                    if (Mathf.Abs(delta) >= clearanceDegrees)
                    {
                        continue;
                    }

                    // Inside the label's cone — stop at the boundary, on the side
                    // the window currently sits (falling back to the gaze side).
                    var side = delta != 0f ? Mathf.Sign(delta) : CurrentSideOf(obstacleYaw, cameraPosition);
                    desiredYaw = obstacleYaw + side * clearanceDegrees;
                }
            }

            var direction = Quaternion.Euler(0f, desiredYaw, 0f) * Vector3.forward;
            return cameraPosition + direction * distanceMeters + Vector3.up * heightOffsetMeters;
        }

        /// <summary>Which side of the obstacle the window is on right now (+1 right, -1 left).</summary>
        private float CurrentSideOf(float obstacleYaw, Vector3 cameraPosition)
        {
            var toWindow = transform.position - cameraPosition;
            toWindow.y = 0f;
            if (toWindow.sqrMagnitude < 0.01f)
            {
                return 1f;
            }

            var windowYaw = Mathf.Atan2(toWindow.x, toWindow.z) * Mathf.Rad2Deg;
            var delta = Mathf.DeltaAngle(obstacleYaw, windowYaw);
            return delta >= 0f ? 1f : -1f;
        }

        /// <summary>The window ended up behind the user or unreasonably far away.</summary>
        private bool IsLost(Camera camera)
        {
            var toWindow = transform.position - camera.transform.position;
            if (toWindow.magnitude > Mathf.Max(3f, distanceMeters * 2.5f))
            {
                return true;
            }

            var forward = camera.transform.forward;
            forward.y = 0f;
            toWindow.y = 0f;
            return forward.sqrMagnitude > 0.001f && toWindow.sqrMagnitude > 0.001f &&
                   Vector3.Dot(forward.normalized, toWindow.normalized) < -0.1f;
        }
    }
}
