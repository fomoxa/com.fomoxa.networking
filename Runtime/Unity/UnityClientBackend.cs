using System;
using System.Collections.Generic;
using Fomoxa.Networking;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Prediction;
using Fomoxa.Networking.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fomoxa.Unity
{
    internal sealed class UnityClientEntityBackend : IClientEntityBackend
    {
        private readonly PrefabRegistry prefabs;
        private readonly Dictionary<ulong, NetworkObject> sceneObjects = new Dictionary<ulong, NetworkObject>();

        public UnityClientEntityBackend(PrefabRegistry prefabs)
        {
            this.prefabs = prefabs;
        }

        public Func<IReadOnlyList<NetworkObject>> FindSceneObjects { get; set; } = SceneObjects.InLoadedScenes;

        public Dictionary<uint, Scene> ClientScenes { get; } = new Dictionary<uint, Scene>();

        public SpawnResult CheckPrefab(in SpawnedObject spawned)
        {
            if (!prefabs.TryGet(spawned.PrefabId, out PrefabEntry entry))
            {
                return SpawnResult.UnknownPrefab;
            }

            return entry.Fingerprint == spawned.PrefabFingerprint ? SpawnResult.Spawned : SpawnResult.IncompatiblePrefab;
        }

        public INetworkEntity Create(in SpawnedObject spawned)
        {
            prefabs.TryGet(spawned.PrefabId, out PrefabEntry entry);
            NetworkObject instance = Create(entry, spawned);
            if (spawned.SceneId != 0 && ClientScenes.TryGetValue(spawned.SceneId, out Scene scene))
            {
                SceneManager.MoveGameObjectToScene(instance.gameObject, scene);
            }

            return instance;
        }

        public SpawnResult PlaceSceneObject(in SpawnedObject spawned, out INetworkEntity entity)
        {
            if (!sceneObjects.TryGetValue(spawned.SceneObjectId, out NetworkObject instance) || instance == null)
            {
                entity = null;
                return SpawnResult.UnknownSceneObject;
            }

            entity = instance;
            if (!PrefabHash.TryDescribeSceneObject(instance, out uint fingerprint, out string _) || fingerprint != spawned.PrefabFingerprint)
            {
                return SpawnResult.IncompatibleSceneObject;
            }

            instance.transform.SetPositionAndRotation(spawned.Position.ToUnity(), spawned.Rotation.ToUnity());
            instance.transform.localScale = spawned.Scale.ToUnity();
            instance.CollectBehaviours();
            instance.Fingerprint = fingerprint;
            instance.gameObject.SetActive(true);
            return SpawnResult.Spawned;
        }

        public void End(INetworkEntity entity)
        {
            var instance = (NetworkObject)entity;
            if (sceneObjects.TryGetValue(instance.SceneObjectId, out NetworkObject sceneObject) && sceneObject == instance)
            {
                instance.gameObject.SetActive(false);
                return;
            }

            if (prefabs.TryGet(instance.PrefabId, out PrefabEntry entry))
            {
                Release(entry, instance);
                return;
            }

            instance.DestroyGameObject();
        }

        public void HideOnHost(INetworkEntity entity) => ((NetworkObject)entity).HideOnHost();

        public void ShowOnHost(INetworkEntity entity) => ((NetworkObject)entity).ShowOnHost();

        public void PrepareSceneObjects()
        {
            sceneObjects.Clear();
            Prepare(FindSceneObjects());
            foreach (Scene scene in ClientScenes.Values)
            {
                Prepare(scene);
            }
        }

        public void Prepare(Scene scene)
        {
            var found = new List<NetworkObject>();
            SceneObjects.AddFrom(scene, found);
            Prepare(found);
        }

        public void ForgetSceneObjectsOf(Scene scene)
        {
            var leaving = new List<ulong>();
            foreach (KeyValuePair<ulong, NetworkObject> entry in sceneObjects)
            {
                if (entry.Value == null || entry.Value.gameObject.scene == scene)
                {
                    leaving.Add(entry.Key);
                }
            }

            foreach (ulong sceneObjectId in leaving)
            {
                sceneObjects.Remove(sceneObjectId);
            }
        }

        public void ForgetDestroyed(NetworkObject networkObject)
        {
            if (sceneObjects.TryGetValue(networkObject.SceneObjectId, out NetworkObject sceneObject) && sceneObject == networkObject)
            {
                sceneObjects.Remove(networkObject.SceneObjectId);
            }
        }

        private void Prepare(IReadOnlyList<NetworkObject> found)
        {
            foreach (NetworkObject sceneObject in found)
            {
                if (sceneObject.IsSpawned || !sceneObjects.TryAdd(sceneObject.SceneObjectId, sceneObject))
                {
                    continue;
                }

                sceneObject.gameObject.SetActive(false);
            }
        }

        private static NetworkObject Create(PrefabEntry entry, in SpawnedObject spawnedObject)
        {
            Vector3 position = spawnedObject.Position.ToUnity();
            Quaternion rotation = spawnedObject.Rotation.ToUnity();
            NetworkObject instance;
            if (entry.Create == null)
            {
                instance = UnityEngine.Object.Instantiate(entry.Prefab, position, rotation);
            }
            else
            {
                instance = entry.Create(position, rotation);
                if (instance == null)
                {
                    throw new InvalidOperationException($"the factory of prefab 0x{entry.PrefabId:X8} returned no NetworkObject");
                }

                if (instance.IsSpawned)
                {
                    throw new InvalidOperationException($"the factory of prefab 0x{entry.PrefabId:X8} returned {instance.name}, which is already spawned");
                }
            }

            try
            {
                instance.transform.localScale = spawnedObject.Scale.ToUnity();
                instance.SetPrefabId(entry.PrefabId);
                instance.CollectBehaviours();
            }
            catch
            {
                Release(entry, instance);
                throw;
            }

            return instance;
        }

        private static void Release(PrefabEntry entry, NetworkObject instance)
        {
            if (entry.Release != null)
            {
                entry.Release(instance);
                return;
            }

            instance.DestroyGameObject();
        }
    }

    internal sealed class UnityClientSceneHost : ISceneHost
    {
        private readonly SceneRegistry registry;
        private readonly UnityClientEntityBackend entities;

        public UnityClientSceneHost(SceneRegistry registry, UnityClientEntityBackend entities)
        {
            this.registry = registry;
            this.entities = entities;
        }

        public bool TryLoad(uint sceneId, Action loaded, Action failed)
        {
            if (!registry.TryGet(sceneId, out ISceneLoader loader))
            {
                return false;
            }

            loader.Load(
                scene =>
                {
                    entities.ClientScenes[sceneId] = scene;
                    entities.Prepare(scene);
                    loaded();
                },
                exception =>
                {
                    Debug.LogException(exception);
                    failed();
                });
            return true;
        }

        public void Unload(uint sceneId, Action unloaded)
        {
            if (!entities.ClientScenes.Remove(sceneId, out Scene scene) || !registry.TryGet(sceneId, out ISceneLoader loader))
            {
                unloaded();
                return;
            }

            entities.ForgetSceneObjectsOf(scene);
            loader.Unload(scene, unloaded);
        }
    }

    internal sealed class UnityPredictionBackend : IClientPredictionBackend
    {
        public NetworkPhysics Physics { get; set; }

        public void WorldsOf(INetworkEntity entity, List<IPhysicsSimulation> worlds)
        {
            Physics?.WorldsOf(((NetworkObject)entity).gameObject.scene, worlds);
        }

        public PhysicsHistory HistoryOf(IPhysicsSimulation world, int capacity) => Physics.HistoryOf(world, capacity);

        public void PlaceProxy(INetworkEntity entity) => Physics?.PlaceProxy((NetworkObject)entity);

        public void EndProxy(INetworkEntity entity) => Physics?.EndProxy((NetworkObject)entity);

        public void ForgetDestroyedProxies() => Physics?.ForgetDestroyedProxies();

        public void BeginCorrection(INetworkEntity entity)
        {
            foreach (NetworkBehaviour behaviour in ((NetworkObject)entity).Behaviours)
            {
                (behaviour as NetworkTransform)?.BeginCorrection();
            }
        }

        public void EndCorrection(INetworkEntity entity)
        {
            foreach (NetworkBehaviour behaviour in ((NetworkObject)entity).Behaviours)
            {
                (behaviour as NetworkTransform)?.EndCorrection();
            }
        }
    }
}
