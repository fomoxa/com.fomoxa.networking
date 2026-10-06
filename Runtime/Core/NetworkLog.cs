using System;

namespace Fomoxa.Networking
{
    public sealed class NetworkLog
    {
        private readonly Action<Exception> exception;
        private readonly Action<string> warning;
        private readonly Action<string> error;

        public NetworkLog(Action<Exception> exception, Action<string> warning)
            : this(exception, warning, warning)
        {
        }

        public NetworkLog(Action<Exception> exception, Action<string> warning, Action<string> error)
        {
            this.exception = exception ?? throw new ArgumentNullException(nameof(exception));
            this.warning = warning ?? throw new ArgumentNullException(nameof(warning));
            this.error = error ?? throw new ArgumentNullException(nameof(error));
        }

        public static NetworkLog Console { get; } = new NetworkLog(
            error => System.Console.Error.WriteLine(error),
            message => System.Console.Error.WriteLine(message),
            message => System.Console.Error.WriteLine(message));

        public void Exception(Exception error) => exception(error);

        public void Warning(string message) => warning(message);

        public void Error(string message) => error(message);
    }
}
