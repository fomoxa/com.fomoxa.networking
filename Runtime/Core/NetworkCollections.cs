using System;

namespace Fomoxa.Networking
{
    public static class NetworkCollections
    {
        public static void Modify<T>(this NetworkList<T> list, int index, Action<T> action)
            where T : class, new()
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            list.ModifyCore(index, action, static (item, run) => run(item));
        }

        public static void Modify<T, TState>(this NetworkList<T> list, int index, TState state, Action<T, TState> action)
            where T : class, new()
        {
            list.ModifyCore(index, state, action);
        }

        public static void Modify<TKey, TValue>(this NetworkDictionary<TKey, TValue> dictionary, TKey key, Action<TValue> action)
            where TValue : class, new()
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            dictionary.ModifyCore(key, action, static (value, run) => run(value));
        }

        public static void Modify<TKey, TValue, TState>(this NetworkDictionary<TKey, TValue> dictionary, TKey key, TState state, Action<TValue, TState> action)
            where TValue : class, new()
        {
            dictionary.ModifyCore(key, state, action);
        }

        internal static void EnsureNotNull<T>(T value, string name)
        {
            if (value == null)
            {
                throw new ArgumentNullException(name);
            }
        }
    }
}
