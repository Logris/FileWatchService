
using System.Windows;

namespace MiracleAdmin
{
    public interface IServiceExtension
    {
        string Name { get; }
        UIElement Content { get; }

        void Load(object manager);

        void ProccessUdpMessage(byte[] message);

        void Stop();
        void OnSaveProperties();
    }
}
