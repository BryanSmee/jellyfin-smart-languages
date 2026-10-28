using System;
using System.IO;
using System.Reflection;

namespace Jellyfin.Plugin.SmartAudio.Tests.Stubs;

/// <summary>
/// Stand-in for server interfaces: strings are a temporary folder, everything else is the default value.
/// Not sealed: <see cref="DispatchProxy"/> refuses sealed proxy types.
/// </summary>
public class ServerStub : DispatchProxy
{
    public static T Create<T>()
        where T : class
        => Create<T, ServerStub>();

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        Type? returnType = targetMethod?.ReturnType;
        if (returnType == typeof(string))
        {
            return Path.GetTempPath();
        }

        return returnType is { IsValueType: true } && returnType != typeof(void) ? Activator.CreateInstance(returnType) : null;
    }
}
