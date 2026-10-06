namespace Fomoxa.Networking
{
    public interface ITransformReceiver
    {
        void ResetReceive();

        void Receive(in TransformSample sample);
    }
}
