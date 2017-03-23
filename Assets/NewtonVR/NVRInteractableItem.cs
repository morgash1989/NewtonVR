﻿using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Events;
using System.Linq;

namespace NewtonVR
{
    public class NVRInteractableItem : NVRInteractable
    {
        private const float MaxVelocityChange = 10f;
        private const float MaxAngularVelocityChange = 20f;
        private const float VelocityMagic = 6000f;
        private const float AngularVelocityMagic = 50f;

        public bool DisablePhysicalMaterialsOnAttach = true;

        [Tooltip("If you have a specific point you'd like the object held at, create a transform there and set it to this variable")]
        public Transform InteractionPoint;

        public UnityEvent OnUseButtonDown;
        public UnityEvent OnUseButtonUp;

        public UnityEvent OnHovering;

        public UnityEvent OnBeginInteraction;
        public UnityEvent OnEndInteraction;

        protected Vector3 ExternalVelocity;
        protected Vector3 ExternalAngularVelocity;

        protected Vector3?[] VelocityHistory;
        protected Vector3?[] AngularVelocityHistory;
        protected int CurrentVelocityHistoryStep = 0;

        protected float StartingDrag = -1;
        protected float StartingAngularDrag = -1;

        protected Dictionary<Collider, PhysicMaterial> MaterialCache = new Dictionary<Collider, PhysicMaterial>();

        protected Vector3 PickupPointItem;
        protected Quaternion PickupRotationItem;
        protected Vector3 PickupPointHand;
        protected Quaternion PickupRotationHand;
        protected Vector3 PickupPointDiff;
        protected Quaternion PickupRotationDelta;

        protected override void Awake()
        {
            base.Awake();

            this.Rigidbody.maxAngularVelocity = 100f;
        }

        protected override void Start()
        {
            base.Start();
        }

        protected virtual void FixedUpdate()
        {
            if (IsAttached == true)
            {
                bool dropped = CheckForDrop();

                if (dropped == false)
                {
                    UpdateVelocities();
                }
            }

            AddExternalVelocities();
        }

        private Vector3 CalculatePositionPlusOffset(Vector3 handPosition, Quaternion handRotation, Vector3 localOffset)
        {
            return handPosition + handRotation * localOffset;
        }

        private Quaternion CalculateRotationPlusOffset(Quaternion handRotation, Quaternion localOffset)
        {
            return handRotation * localOffset;
        }

        private float deltaTime = 0;
        protected virtual void UpdateVelocities()
        {
            deltaTime += Time.deltaTime;
            if (Time.deltaTime < NVRPlayer.NewtonVRExpectedDeltaTime) //if we're running too fast, ignore the frame
                return;
            deltaTime = 0;

            float velocityMagic = VelocityMagic / (Time.deltaTime / NVRPlayer.NewtonVRExpectedDeltaTime);
            float angularVelocityMagic = AngularVelocityMagic / (Time.deltaTime / NVRPlayer.NewtonVRExpectedDeltaTime);

            Quaternion rotationDelta = Quaternion.identity;
            Vector3 positionDelta = Vector3.zero;

            float angle;
            Vector3 axis;


            Vector3 targetPosition = CalculatePositionPlusOffset(AttachedHand.Rigidbody.position, AttachedHand.Rigidbody.rotation, PickupPointDiff);
            Quaternion currentHandRotationDelta = AttachedHand.Rigidbody.rotation * Quaternion.Inverse(PickupRotationHand);
            Quaternion targetRotation = currentHandRotationDelta * PickupRotationItem;

            positionDelta = targetPosition - this.Rigidbody.position;
            rotationDelta = targetRotation * Quaternion.Inverse(this.Rigidbody.rotation);

            rotationDelta.ToAngleAxis(out angle, out axis);

            if (angle > 180)
                angle -= 360;

            Vector3 angularTarget = Vector3.zero;
            if (angle != 0)
            {
                angularTarget = angle * axis;
                if (float.IsNaN(angularTarget.x) == false)
                {
                    angularTarget = angularTarget / (NVRPlayer.NewtonVRExpectedDeltaTime * 100);
                }
                else
                {
                    angularTarget = Vector3.zero;
                }
            }
            this.Rigidbody.angularVelocity = Vector3.MoveTowards(this.Rigidbody.angularVelocity, angularTarget, MaxAngularVelocityChange);

            Vector3 velocityTarget = positionDelta / NVRPlayer.NewtonVRExpectedDeltaTime;
            if (float.IsNaN(velocityTarget.x) == true)
            {
                velocityTarget = Vector3.zero;
            }
            this.Rigidbody.velocity = Vector3.MoveTowards(this.Rigidbody.velocity, velocityTarget, MaxVelocityChange);


            if (VelocityHistory != null)
            {
                CurrentVelocityHistoryStep++;
                if (CurrentVelocityHistoryStep >= VelocityHistory.Length)
                {
                    CurrentVelocityHistoryStep = 0;
                }

                VelocityHistory[CurrentVelocityHistoryStep] = this.Rigidbody.velocity;
                AngularVelocityHistory[CurrentVelocityHistoryStep] = this.Rigidbody.angularVelocity;
            }
        }

        protected virtual void AddExternalVelocities()
        {
            if (ExternalVelocity != Vector3.zero)
            {
                this.Rigidbody.velocity = Vector3.Lerp(this.Rigidbody.velocity, ExternalVelocity, 0.5f);
                ExternalVelocity = Vector3.zero;
            }

            if (ExternalAngularVelocity != Vector3.zero)
            {
                this.Rigidbody.angularVelocity = Vector3.Lerp(this.Rigidbody.angularVelocity, ExternalAngularVelocity, 0.5f);
                ExternalAngularVelocity = Vector3.zero;
            }
        }

        public override void AddExternalVelocity(Vector3 velocity)
        {
            if (ExternalVelocity == Vector3.zero)
            {
                ExternalVelocity = velocity;
            }
            else
            {
                ExternalVelocity = Vector3.Lerp(ExternalVelocity, velocity, 0.5f);
            }
        }

        public override void AddExternalAngularVelocity(Vector3 angularVelocity)
        {
            if (ExternalAngularVelocity == Vector3.zero)
            {
                ExternalAngularVelocity = angularVelocity;
            }
            else
            {
                ExternalAngularVelocity = Vector3.Lerp(ExternalAngularVelocity, angularVelocity, 0.5f);
            }
        }

        public override void BeginInteraction(NVRHand hand)
        {
            base.BeginInteraction(hand);

            StartingDrag = Rigidbody.drag;
            StartingAngularDrag = Rigidbody.angularDrag;
            Rigidbody.drag = 0;
            Rigidbody.angularDrag = 0.05f;

            if (DisablePhysicalMaterialsOnAttach == true)
            {
                DisablePhysicalMaterials();
            }

            if (InteractionPoint != null)
            {
                PickupPointItem = InteractionPoint.position;
                PickupRotationItem = InteractionPoint.rotation;
            }
            else
            {
                PickupPointItem = this.Rigidbody.position;
                PickupRotationItem = this.Rigidbody.rotation;
            }

            PickupPointHand = hand.Rigidbody.position;
            PickupRotationHand = hand.Rigidbody.rotation;
            PickupPointDiff = hand.transform.InverseTransformPoint(PickupPointItem);
            PickupRotationDelta = Quaternion.Inverse(PickupRotationHand) * PickupRotationItem;

            ResetVelocityHistory();

            if (OnBeginInteraction != null)
            {
                OnBeginInteraction.Invoke();
            }
        }

        public override void EndInteraction()
        {
            base.EndInteraction();

            Rigidbody.drag = StartingDrag;
            Rigidbody.angularDrag = StartingAngularDrag;

            if (DisablePhysicalMaterialsOnAttach == true)
            {
                EnablePhysicalMaterials();
            }

            ApplyVelocityHistory();
            ResetVelocityHistory();

            if (OnEndInteraction != null)
            {
                OnEndInteraction.Invoke();
            }
        }

        public override void HoveringUpdate(NVRHand hand, float forTime)
        {
            base.HoveringUpdate(hand, forTime);

            if (OnHovering != null)
            {
                OnHovering.Invoke();
            }
        }

        public override void ResetInteractable()
        {
            EndInteraction();
            base.ResetInteractable();
        }

        public override void UseButtonDown()
        {
            base.UseButtonDown();

            if (OnUseButtonDown != null)
            {
                OnUseButtonDown.Invoke();
            }
        }

        public override void UseButtonUp()
        {
            base.UseButtonUp();

            if (OnUseButtonUp != null)
            {
                OnUseButtonUp.Invoke();
            }
        }

        protected virtual void ApplyVelocityHistory()
        {
            if (VelocityHistory != null)
            {
                Vector3? meanVelocity = GetMeanVector(VelocityHistory);
                if (meanVelocity != null)
                {
                    this.Rigidbody.velocity = meanVelocity.Value;
                }

                Vector3? meanAngularVelocity = GetMeanVector(AngularVelocityHistory);
                if (meanAngularVelocity != null)
                {
                    this.Rigidbody.angularVelocity = meanAngularVelocity.Value;
                }
            }
        }

        protected virtual void ResetVelocityHistory()
        {
            if (NVRPlayer.Instance.VelocityHistorySteps > 0)
            {
                CurrentVelocityHistoryStep = 0;

                VelocityHistory = new Vector3?[NVRPlayer.Instance.VelocityHistorySteps];
                AngularVelocityHistory = new Vector3?[NVRPlayer.Instance.VelocityHistorySteps];
            }
        }

        protected Vector3? GetMeanVector(Vector3?[] positions)
        {
            float x = 0f;
            float y = 0f;
            float z = 0f;

            int count = 0;
            for (int index = 0; index < positions.Length; index++)
            {
                if (positions[index] != null)
                {
                    x += positions[index].Value.x;
                    y += positions[index].Value.y;
                    z += positions[index].Value.z;

                    count++;
                }
            }

            if (count > 0)
            {
                return new Vector3(x / count, y / count, z / count);
            }

            return null;
        }

        protected void DisablePhysicalMaterials()
        {
            for (int colliderIndex = 0; colliderIndex < Colliders.Length; colliderIndex++)
            {
                if (Colliders[colliderIndex] == null)
                {
                    continue;
                }

                MaterialCache[Colliders[colliderIndex]] = Colliders[colliderIndex].sharedMaterial;
                Colliders[colliderIndex].sharedMaterial = null;
            }
        }

        protected void EnablePhysicalMaterials()
        {
            for (int colliderIndex = 0; colliderIndex < Colliders.Length; colliderIndex++)
            {
                if (Colliders[colliderIndex] == null)
                {
                    continue;
                }

                if (MaterialCache.ContainsKey(Colliders[colliderIndex]) == true)
                {
                    Colliders[colliderIndex].sharedMaterial = MaterialCache[Colliders[colliderIndex]];
                }
            }
        }

        public override void UpdateColliders()
        {
            base.UpdateColliders();

            if (DisablePhysicalMaterialsOnAttach == true)
            {
                for (int colliderIndex = 0; colliderIndex < Colliders.Length; colliderIndex++)
                {
                    if (MaterialCache.ContainsKey(Colliders[colliderIndex]) == false)
                    {
                        MaterialCache.Add(Colliders[colliderIndex], Colliders[colliderIndex].sharedMaterial);

                        if (IsAttached == true)
                        {
                            Colliders[colliderIndex].sharedMaterial = null;
                        }
                    }
                }
            }
        }
    }
}