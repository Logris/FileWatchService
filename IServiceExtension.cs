
using System.Windows;

namespace Miracle
{
    namespace Service
    {
        public interface IServiceExtension
        {
            string Name { get; }
            UIElement Content { get; }

            void Load(object manager);

            void Stop();
            void OnSaveProperties();
        }
    }
}
