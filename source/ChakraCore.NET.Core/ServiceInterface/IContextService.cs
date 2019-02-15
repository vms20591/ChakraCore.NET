
using ChakraCore.NET.API;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace ChakraCore.NET
{
    public interface IContextService:IService
    {
        string RunScript(string script);
        string RunScript(string script, byte[] buffer);
        JavaScriptValue ParseScript(string script);
        byte[] SerializeScript(string script);
        void RunModule(string script, Func<string, string> loadModuleCallback);
        CancellationTokenSource ContextShutdownCTS { get; }
    }
}
