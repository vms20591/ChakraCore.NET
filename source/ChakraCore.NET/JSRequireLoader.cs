﻿using System;
using System.Collections.Generic;
using System.Text;

namespace ChakraCore.NET
{
    public class JSRequireLoader
    {
        public string RootPath { get; set; } = string.Empty;
        public Func<string, string> LoadModuleCallback { get; set; }
        private bool isFileLoaded = true;
        private Dictionary<string, string> scriptCache = new Dictionary<string, string>();
        public string LoadLib(string name)
        {
            if (!scriptCache.ContainsKey(name))
            {
                scriptCache.Add(name, load(name));
            }
            return scriptCache[name];

        }

        [Obsolete("Requires is notlonger supported. for moduling system, please use the ES6 module")]
        public static void EnableRequire(ChakraContext context,string rootPath=null)
        {
            JSRequireLoader loader = new JSRequireLoader() { RootPath = rootPath };
            context.GlobalObject.Binding.SetFunction<string, string>("_loadLib", loader.LoadLib);
            context.RunScript(Properties.Resources.ResourceManager.GetString("JSRequire"));
        }

        [Obsolete("Requires is notlonger supported. for moduling system, please use the ES6 module")]
        public static void EnableRequire(ChakraContext context, Func<string, string> loadModuleCallback)
        {
            JSRequireLoader loader = new JSRequireLoader() { LoadModuleCallback = loadModuleCallback, isFileLoaded = false };
            context.GlobalObject.Binding.SetFunction<string, string>("_loadLib", loader.LoadLib);
            context.RunScript(Properties.Resources.ResourceManager.GetString("JSRequire"));
        }

        private string load(string name)
        {
            if (this.isFileLoaded)
            {
                return this.loadFromFile(name);
            }

            return this.LoadModuleCallback(name);
        }

        private string loadFromFile(string name)
        {
            System.IO.DirectoryInfo info = new System.IO.DirectoryInfo(System.IO.Directory.GetCurrentDirectory() + "\\" + RootPath);

            string fileName = name + ".js";
            var files = info.GetFiles(fileName);
            if (files.Length == 1)
            {
                return files[0].OpenText().ReadToEnd();
            }
            else
            {
                throw new System.IO.FileNotFoundException("cannot find rquired js file", fileName);
            }
        }
    }
}
