using System;
using System.Collections.Generic;
using Fomoxa.Net.Transports;

namespace Fomoxa.Networking.Transports
{
    public sealed class CompositeListener : IListenerTransport
    {
        private readonly IListenerTransport[] listeners;
        private readonly int[] budgets;
        private readonly Queue<int> acceptedBudgets = new Queue<int>();
        private int nextIndex;

        public CompositeListener(params IListenerTransport[] listeners)
            : this(listeners, null)
        {
        }

        public CompositeListener(IListenerTransport[] listeners, int[] budgets)
        {
            if (listeners == null || listeners.Length == 0)
            {
                throw new ArgumentException("at least one listener is required", nameof(listeners));
            }

            if (budgets != null && budgets.Length != listeners.Length)
            {
                throw new ArgumentException("every listener needs a frame budget", nameof(budgets));
            }

            this.listeners = (IListenerTransport[])listeners.Clone();
            this.budgets = (int[])budgets?.Clone();
        }

        public AcceptOutcome Accept()
        {
            int errorCount = 0;
            for (int attempt = 0; attempt < listeners.Length; attempt++)
            {
                int index = nextIndex;
                IListenerTransport listener = listeners[index];
                nextIndex = (nextIndex + 1) % listeners.Length;

                AcceptOutcome outcome = listener.Accept();
                if (outcome.Status == AcceptStatus.Accepted)
                {
                    RecordBudget(index);
                }

                if (outcome.Status == AcceptStatus.Accepted || outcome.Status == AcceptStatus.Progress)
                {
                    return outcome;
                }

                if (outcome.Status == AcceptStatus.Error)
                {
                    errorCount++;
                }
            }

            return errorCount == listeners.Length ? AcceptOutcome.Error : AcceptOutcome.Pending;
        }

        public bool TryTakeAcceptedBudget(out int budget)
        {
            if (acceptedBudgets.Count == 0)
            {
                budget = 0;
                return false;
            }

            budget = acceptedBudgets.Dequeue();
            return true;
        }

        public void Dispose()
        {
            foreach (IListenerTransport listener in listeners)
            {
                listener.Dispose();
            }
        }

        private void RecordBudget(int index)
        {
            if (listeners[index] is CompositeListener inner && inner.TryTakeAcceptedBudget(out int innerBudget))
            {
                acceptedBudgets.Enqueue(innerBudget);
            }
            else if (budgets != null)
            {
                acceptedBudgets.Enqueue(budgets[index]);
            }
        }
    }
}
