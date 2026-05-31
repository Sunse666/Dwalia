namespace Dwalia.Infrastructure;

public static class ServiceLocator
{
    private static readonly Dictionary<Type, object> _services = new();

    public static void Register<T>(T instance) where T : class
    {
        _services[typeof(T)] = instance;
    }

    public static T Resolve<T>() where T : class
    {
        if (_services.TryGetValue(typeof(T), out var service))
            return (T)service;

        throw new InvalidOperationException($"Service of type {typeof(T).Name} is not registered.");
    }

    public static bool TryResolve<T>(out T service) where T : class
    {
        if (_services.TryGetValue(typeof(T), out var obj))
        {
            service = (T)obj;
            return true;
        }
        service = null!;
        return false;
    }

    public static void Clear()
    {
        _services.Clear();
    }
}
