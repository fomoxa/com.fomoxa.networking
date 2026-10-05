using System;
using System.Collections.Generic;
using System.Text;
using Fomoxa.Networking.Messaging;
using UnityEngine;

namespace Fomoxa.Unity
{
    [RequireComponent(typeof(Animator))]
    public sealed class NetworkAnimator : NetworkBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private float floatStep = 0.01f;

        private readonly List<int> floatHashes = new List<int>();
        private readonly List<int> intHashes = new List<int>();
        private readonly List<int> boolHashes = new List<int>();
        private readonly List<int> triggerHashes = new List<int>();
        private readonly Dictionary<int, int> triggerIndexes = new Dictionary<int, int>();
        private bool layoutRead;
        private bool layoutReported;
        private uint layout;
        private int layerCount;

        public Animator Animator => animator != null ? animator : animator = GetComponent<Animator>();

        public float FloatStep => floatStep;

        internal AnimatorState State { get; } = new AnimatorState();

        public void SetTrigger(string triggerName) => SetTrigger(Animator.StringToHash(triggerName), triggerName);

        public void SetTrigger(int hash) => SetTrigger(hash, hash.ToString());

        public override void OnStartClient()
        {
            if (!CanApply())
            {
                return;
            }

            ApplyParameters();
            Animator target = Animator;
            for (int layer = 0; layer < layerCount; layer++)
            {
                if (State.LayerStates[layer] != 0)
                {
                    target.Play(State.LayerStates[layer], layer, 0f);
                }
            }
        }

        internal override void PrepareState()
        {
            Animator target = Animator;
            State.Speed = target.speed;
            for (int index = 0; index < floatHashes.Count; index++)
            {
                State.Floats[index] = Round(target.GetFloat(floatHashes[index]));
            }

            for (int index = 0; index < intHashes.Count; index++)
            {
                State.Ints[index] = target.GetInteger(intHashes[index]);
            }

            for (int index = 0; index < boolHashes.Count; index++)
            {
                State.Bools[index] = target.GetBool(boolHashes[index]);
            }

            for (int layer = 1; layer < layerCount; layer++)
            {
                State.LayerWeights[layer - 1] = target.GetLayerWeight(layer);
            }

            for (int layer = 0; layer < layerCount; layer++)
            {
                State.LayerStates[layer] = target.GetCurrentAnimatorStateInfo(layer).fullPathHash;
            }
        }

        protected override void OnRegisterState(NetworkState state)
        {
            EnsureLayout();
            state.Use(state.Protocol.AnimatorCodec, State, OnStateReceived);
        }

        private void SetTrigger(int hash, string description)
        {
            EnsureLayout();
            if (!triggerIndexes.TryGetValue(hash, out int index))
            {
                throw new ArgumentException($"{name}: the Animator has no trigger parameter {description}", nameof(hash));
            }

            Animator.SetTrigger(hash);
            NetworkObject networkObject = NetworkObject;
            if (networkObject == null || networkObject.Client == null || networkObject.Server != null)
            {
                State.Triggers[index] = unchecked((byte)(State.Triggers[index] + 1));
            }
        }

        private void OnStateReceived(AnimatorState previous)
        {
            if (!CanApply())
            {
                return;
            }

            ApplyParameters();
            Animator target = Animator;
            for (int index = 0; index < triggerHashes.Count && index < previous.Triggers.Count; index++)
            {
                int steps = unchecked((byte)(State.Triggers[index] - previous.Triggers[index]));
                for (int step = 0; step < steps; step++)
                {
                    target.SetTrigger(triggerHashes[index]);
                }
            }
        }

        private bool CanApply()
        {
            NetworkObject networkObject = NetworkObject;
            if (networkObject == null || networkObject.Server != null)
            {
                return false;
            }

            if (State.Layout == layout)
            {
                return true;
            }

            if (!layoutReported)
            {
                layoutReported = true;
                Debug.LogError($"Fomoxa: {name} received Animator state with layout 0x{State.Layout:X8}, but its Animator has layout 0x{layout:X8}; the Animator controllers differ between server and client, so the state is not applied");
            }

            return false;
        }

        private void ApplyParameters()
        {
            Animator target = Animator;
            target.speed = State.Speed;
            for (int index = 0; index < floatHashes.Count; index++)
            {
                target.SetFloat(floatHashes[index], State.Floats[index]);
            }

            for (int index = 0; index < intHashes.Count; index++)
            {
                target.SetInteger(intHashes[index], State.Ints[index]);
            }

            for (int index = 0; index < boolHashes.Count; index++)
            {
                target.SetBool(boolHashes[index], State.Bools[index]);
            }

            for (int layer = 1; layer < layerCount; layer++)
            {
                target.SetLayerWeight(layer, State.LayerWeights[layer - 1]);
            }
        }

        private void EnsureLayout()
        {
            if (layoutRead)
            {
                return;
            }

            layoutRead = true;
            Animator target = Animator;
            if (!target.isInitialized)
            {
                target.Rebind();
            }

            var description = new StringBuilder();
            foreach (AnimatorControllerParameter parameter in target.parameters)
            {
                description.Append(parameter.name).Append(':').Append((int)parameter.type).Append(';');
                switch (parameter.type)
                {
                    case AnimatorControllerParameterType.Float:
                        floatHashes.Add(parameter.nameHash);
                        State.Floats.Add(0f);
                        break;
                    case AnimatorControllerParameterType.Int:
                        intHashes.Add(parameter.nameHash);
                        State.Ints.Add(0);
                        break;
                    case AnimatorControllerParameterType.Bool:
                        boolHashes.Add(parameter.nameHash);
                        State.Bools.Add(false);
                        break;
                    default:
                        triggerIndexes[parameter.nameHash] = triggerHashes.Count;
                        triggerHashes.Add(parameter.nameHash);
                        State.Triggers.Add(0);
                        break;
                }
            }

            layerCount = target.layerCount;
            description.Append(layerCount);
            for (int layer = 0; layer < layerCount; layer++)
            {
                State.LayerStates.Add(0);
                if (layer > 0)
                {
                    State.LayerWeights.Add(0f);
                }
            }

            layout = PrefabHash.Fnv1a(description.ToString());
            State.Layout = layout;
        }

        private float Round(float value) => floatStep > 0 ? Mathf.Round(value / floatStep) * floatStep : value;
    }
}
