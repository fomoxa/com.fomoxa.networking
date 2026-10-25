using System.Collections.Generic;
using System.Linq;
using Fomoxa.Networking;

namespace Fomoxa.Unity.Tests.Support
{
    public sealed class InventoryBehaviour : NetworkBehaviour
    {
        public readonly List<string> Changes = new List<string>();

        public NetworkList<CounterState> Items { get; } = new NetworkList<CounterState>();

        public NetworkDictionary<string, int> Scores { get; } = new NetworkDictionary<string, int>();

        public NetworkSet<uint> Tags { get; } = new NetworkSet<uint>();

        public string StartedWith { get; private set; }

        public string Describe() =>
            $"items [{string.Join(",", Items.Select(item => item.Value))}] scores [{string.Join(",", Scores.OrderBy(entry => entry.Key).Select(entry => $"{entry.Key}={entry.Value}"))}] tags [{string.Join(",", Tags.OrderBy(tag => tag))}]";

        public override void OnStartClient()
        {
            StartedWith = Describe();
        }

        protected override void OnRegisterState(NetworkState state)
        {
            state.Use(2, Tags, (operation, oldItem, newItem) => Changes.Add($"tags {operation} {oldItem} {newItem}"));
            state.Use(1, Items, StateCodecs.Counter, (operation, index, oldItem, newItem) => Changes.Add($"items {operation} {index} {oldItem?.Value} {newItem?.Value}"));
            state.Use(0, Scores, (operation, key, oldValue, newValue) => Changes.Add($"scores {operation} {key} {oldValue} {newValue}"));
        }
    }
}
