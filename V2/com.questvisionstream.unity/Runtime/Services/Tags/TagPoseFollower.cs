// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using UnityEngine;

namespace QuestVisionStream.Services
{
    /// <summary>
    /// The connecting component that keeps a spawned object aligned to a live
    /// AprilTag: it subscribes to the routing events for ONE tag id and smooths
    /// the transform toward the latest observed pose every frame.
    ///
    /// Chosen over the alternatives deliberately:
    /// parenting under the <see cref="TagPlacementService"/> markers would tie
    /// model lifetime to a debug visual that can be disabled; re-instantiating
    /// per sighting churns allocations and can never smooth; and per-frame raw
    /// poses from the decoder jitter visibly at tag distances. Smoothing plus
    /// freeze-at-last-pose when the tag leaves view (TTL exit) gives a stable
    /// model that self-heals when the tag re-enters — and survives a recenter,
    /// because the re-entered observation carries the corrected world pose.
    /// </summary>
    public sealed class TagPoseFollower : MonoBehaviour
    {
        private ITagRoutingService routing;
        private int tagId;
        private float positionSmoothTime;
        private float rotationLerpSpeed;
        private bool hideWhenLost;

        private Pose target;
        private bool bound;
        private bool lost;
        private Vector3 positionVelocity;

        /// <summary>The tag id this follower is aligned to.</summary>
        public int TagId => tagId;

        /// <summary>False while the tag is unseen past its TTL (the follower freezes at the last pose).</summary>
        public bool IsTracking => bound && !lost;

        /// <summary>
        /// Bind to a tag: snaps to the observation's pose now, then follows every
        /// subsequent sighting of that id with smoothing.
        /// </summary>
        public void Bind(
            ITagRoutingService routing,
            TagObservation observation,
            float positionSmoothTime = 0.15f,
            float rotationLerpSpeed = 12f,
            bool hideWhenLost = false)
        {
            Unbind();

            this.routing = routing;
            tagId = observation.Id;
            this.positionSmoothTime = Mathf.Max(0.01f, positionSmoothTime);
            this.rotationLerpSpeed = Mathf.Max(0.1f, rotationLerpSpeed);
            this.hideWhenLost = hideWhenLost;

            target = observation.WorldPose;
            transform.SetPositionAndRotation(target.position, target.rotation);
            bound = true;
            lost = false;

            routing.TagEntered += OnTagSeen;
            routing.TagUpdated += OnTagSeen;
            routing.TagExited += OnTagExited;
        }

        private void Unbind()
        {
            if (routing != null)
            {
                routing.TagEntered -= OnTagSeen;
                routing.TagUpdated -= OnTagSeen;
                routing.TagExited -= OnTagExited;
                routing = null;
            }

            bound = false;
        }

        private void OnTagSeen(TagObservation observation)
        {
            if (observation.Id != tagId)
            {
                return;
            }

            target = observation.WorldPose;
            if (lost)
            {
                lost = false;
                if (hideWhenLost)
                {
                    gameObject.SetActive(true);
                }
            }
        }

        private void OnTagExited(TagObservation observation)
        {
            if (observation.Id != tagId)
            {
                return;
            }

            lost = true;
            if (hideWhenLost)
            {
                gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            if (!bound || lost)
            {
                return;
            }

            transform.position = Vector3.SmoothDamp(transform.position, target.position, ref positionVelocity, positionSmoothTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, target.rotation, 1f - Mathf.Exp(-rotationLerpSpeed * Time.deltaTime));
        }

        private void OnDestroy() => Unbind();
    }
}
