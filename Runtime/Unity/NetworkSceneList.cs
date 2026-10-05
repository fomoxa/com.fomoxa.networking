using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fomoxa.Unity
{
    public sealed class NetworkSceneList : ScriptableObject
    {
        [SerializeField] private List<Entry> scenes = new List<Entry>();

        public int Count => scenes.Count;

        public uint SceneIdAt(int index) => scenes[index].SceneId;

        public string PathAt(int index) => scenes[index].Path;

        internal void Set(IEnumerable<(uint SceneId, string Path)> values)
        {
            scenes.Clear();
            foreach ((uint sceneId, string path) in values)
            {
                scenes.Add(new Entry { SceneId = sceneId, Path = path });
            }
        }

        [Serializable]
        private struct Entry
        {
            public uint SceneId;
            public string Path;
        }
    }
}
