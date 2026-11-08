// Same names and signatures as the game's ModApi.dll ("ModApi, Version=0.0.0.0, PublicKeyToken=null").
[assembly: System.Reflection.AssemblyVersion("0.0.0.0")]

namespace Eleon.Modding
{
    public interface IModApi
    {
    }

    public interface IMod
    {
        void Init(IModApi modAPI);
        void Shutdown();
    }
}
