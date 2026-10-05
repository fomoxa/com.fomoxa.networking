using Fomoxa.Networking.Messaging;

namespace Fomoxa.Networking.Sessions
{
    public enum SendResult
    {
        Queued,
        Full,
        BytesFull,
        TooLargeForQueue,
        TooLarge,
        NotConnected,
        NotObserver,
    }

    public static class SendResults
    {
        public static SendResult From(EnqueueResult result)
        {
            switch (result)
            {
                case EnqueueResult.Queued:
                    return SendResult.Queued;
                case EnqueueResult.Full:
                    return SendResult.Full;
                case EnqueueResult.BytesFull:
                    return SendResult.BytesFull;
                case EnqueueResult.TooLargeForQueue:
                    return SendResult.TooLargeForQueue;
                default:
                    return SendResult.TooLarge;
            }
        }
    }
}