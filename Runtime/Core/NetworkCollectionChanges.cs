namespace Fomoxa.Networking
{
    public enum NetworkListOperation
    {
        Add,
        Insert,
        Set,
        RemoveAt,
        Clear,
        Reset,
    }

    public enum NetworkDictionaryOperation
    {
        Add,
        Set,
        Remove,
        Clear,
        Reset,
    }

    public enum NetworkSetOperation
    {
        Add,
        Remove,
        Clear,
        Reset,
    }

    public delegate void NetworkListChanged<T>(NetworkListOperation operation, int index, T oldItem, T newItem);

    public delegate void NetworkDictionaryChanged<TKey, TValue>(NetworkDictionaryOperation operation, TKey key, TValue oldValue, TValue newValue);

    public delegate void NetworkSetChanged<T>(NetworkSetOperation operation, T oldItem, T newItem);
}
