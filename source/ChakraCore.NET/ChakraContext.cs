﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Runtime.InteropServices;
using System.Linq;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Diagnostics;
using ChakraCore.NET.API;
using ChakraCore.NET.Timer;
namespace ChakraCore.NET
{
    /// <summary>
    /// A helper class wraps the key feature of chakracore context
    /// </summary>
    public class ChakraContext : ServiceConsumerBase, IDisposable
    {
        //private static JavaScriptSourceContext currentSourceContext = JavaScriptSourceContext.FromIntPtr(IntPtr.Zero);

        internal JavaScriptContext jsContext;
        private EventWaitHandle syncHandle;
        public CancellationTokenSource shutdownCTS = new CancellationTokenSource();
        private BlockingCollection<JavaScriptValue> promiseTaskQueue = new BlockingCollection<JavaScriptValue>();
        private JavaScriptPromiseContinuationCallback promiseContinuationCallback;
        private JavaScriptValue JSGlobalObject;
        private ContextService contextService;
        private ContextSwitchService contextSwitch;
        private JSTimer timerService;

        /// <summary>
        /// The global object of a context, it is the root of everything inside the context
        /// <para>A context is like an isolated class in javascript, everything directly defined in script is a property of the root object</para>
        /// </summary>
        public JSValue GlobalObject { get; private set; }

        /// <summary>
        /// The ChakraRuntime object this context belongs to
        /// </summary>
        public ChakraRuntime Runtime { get; private set; }

        private bool isDebug;
        internal ChakraContext(JavaScriptContext jsContext, ChakraRuntime runtime, EventWaitHandle handle) : base(runtime.ServiceNode, "ChakraContext")
        {
            jsContext.AddRef();
            this.jsContext = jsContext;
            Runtime = runtime;
            syncHandle = handle;
        }

        internal void Init(bool enableDebug, CancellationTokenSource cts = null)
        {
            isDebug = enableDebug;
            contextSwitch = new ContextSwitchService(jsContext, syncHandle);
            ServiceNode.PushService<IContextSwitchService>(contextSwitch);
            ServiceNode.PushService<IGCSyncService>(new GCSyncService());
            Enter();
            promiseContinuationCallback = delegate (JavaScriptValue task, IntPtr callbackState)
            {
                promiseTaskQueue.Add(task);

                System.Diagnostics.Debug.WriteLine("Promise task added");
                Console.WriteLine("Promise task added");
            };

            if (Native.JsSetPromiseContinuationCallback(promiseContinuationCallback, IntPtr.Zero) != JavaScriptErrorCode.NoError)
            {
                throw new InvalidOperationException("failed to setup callback for ES6 Promise");
            }

            StartPromiseTaskLoop(cts != null ? cts.Token : shutdownCTS.Token);


            JSGlobalObject = JavaScriptValue.GlobalObject;
            GlobalObject = new JSValue(ServiceNode, JSGlobalObject);
            Leave();


            contextService = new ContextService(shutdownCTS);
            ServiceNode.PushService<IContextService>(contextService);
            timerService = GlobalObject.InitTimer();

        }

        private void StartPromiseTaskLoop(CancellationToken token)
        {
            Task.Factory.StartNew((Action)(() =>
            {
                System.Diagnostics.Debug.WriteLine("Promise task loop started");
                Console.WriteLine("Promise task loop started");
                while (true)
                {
                    if (shutdownCTS.IsCancellationRequested)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkRed;
                        Console.WriteLine("Breaking promise task loop");
                        Console.ResetColor();
                        break;
                    }

                    JavaScriptValue task;
                    try
                    {
                        task = promiseTaskQueue.Take();
                        System.Diagnostics.Debug.WriteLine("Promise task taken");
                        Console.WriteLine("Promise task taken");
                    }
                    catch (OperationCanceledException)
                    {
                        System.Diagnostics.Debug.WriteLine("Promise task stop");
                        Console.WriteLine("Promise task stop");
                        return;
                    }
                    catch (Exception)
                    {

                        throw;
                    }

                    try
                    {
                        Enter();
                        task.CallFunction((JavaScriptValue)this.JSGlobalObject);
                    }
                    catch (Exception)
                    {
                        Console.WriteLine("Exception in task loop");
                        break;
                    }
                    finally
                    {
                        Leave();
                    }

                    System.Diagnostics.Debug.WriteLine("Promise task complete");
                    Console.WriteLine("Promise task complete");
                }
            })
                , token
                );
        }

        /// <summary>
        /// Try switch context to current thread
        /// </summary>
        /// <returns>true if release is required, false if context already running at current thread(no release call required)</returns>
        public bool Enter()
        {
            return ServiceNode.GetService<IContextSwitchService>().Enter();
        }

        /// <summary>
        /// If chakracore is running at current thread
        /// <para>True if context is running at current thread, otherwise false</para>
        /// </summary>
        public bool IsCurrentContext => ServiceNode.GetService<IContextSwitchService>().IsCurrentContext;

        /// <summary>
        /// Release the context from current thread, this method should be called before you call <see cref="Enter"/> on another thread
        /// </summary>
        public void Leave()
        {
            ServiceNode.GetService<IContextSwitchService>().Leave();

        }

        /// <summary>
        /// Execute a javascript and returns the script result in string
        /// </summary>
        /// <param name="script">Script text</param>
        /// <returns>Script running result</returns>
        public string RunScript(string script)
        {
            return ServiceNode.GetService<IContextService>().RunScript(script);
        }

        /// <summary>
        /// Execute a ES6 module
        /// </summary>
        /// <param name="script">script content</param>
        /// <param name="loadModuleCallback">callback to load imported script content</param>
        public void RunModule(string script, Func<string, string> loadModuleCallback)
        {
            ServiceNode.GetService<IContextService>().RunModule(script, loadModuleCallback);
        }
        /// <summary>
        /// Load a module script, create an instance of specified exported class and map it as a global variable
        /// </summary>
        /// <param name="projectTo">the global variable name mapped to</param>
        /// <param name="moduleName">module name</param>
        /// <param name="className">class name to create an instance</param>
        /// <param name="loadModuleCallback">local module script by name callback </param>
        /// <returns>the mapped value</returns>
        public JSValue ProjectModuleClass(string moduleName, string className, Func<string, string> loadModuleCallback, string projectTo = null)
        {
            string template = "import { {className} } from '{moduleName}'; {projectTo}=new {className}();";
            return ProjectModuleClass(template, moduleName, className, loadModuleCallback, projectTo);
        }


        public JSValue ProjectModuleClass(string proxyModuleScriptTemplate, string moduleName, string className, Func<string, string> loadModuleCallback, string projectTo = null)
        {
            if (string.IsNullOrWhiteSpace(projectTo))
            {
                projectTo = "X" + Guid.NewGuid().ToString().Replace('-', '_');
            }
            string script_setRootObject = $"var {projectTo}={{}};";
            string script_importModule = proxyModuleScriptTemplate
                                            .Replace("{className}", className)
                                            .Replace("{moduleName}", moduleName)
                                            .Replace("{projectTo}", projectTo);
            RunScript(script_setRootObject);
            RunModule(script_importModule, loadModuleCallback);
            return GlobalObject.ReadProperty<JSValue>(projectTo);
        }

        /// <summary>
        /// Parses a script and returns a function representing the script. 
        /// </summary>
        /// <remarks>The script will be wrapped into a javascript function, then return to the caller. Useful for support moduling in javascript
        /// </remarks>
        /// <param name="script">Script text</param>
        /// <returns>A javascript function in <see cref="JavaScriptValue"/></returns>
        public JavaScriptValue ParseScript(string script)
        {
            return ServiceNode.GetService<IContextService>().ParseScript(script);
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    shutdownCTS.Cancel();
                    timerService.ReleaseAll();
                    contextSwitch.Dispose();
                    jsContext.Release();
                }
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
        #endregion


    }
}
