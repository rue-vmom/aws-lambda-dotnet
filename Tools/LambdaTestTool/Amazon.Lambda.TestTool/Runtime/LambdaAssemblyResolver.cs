﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.DependencyModel;
using Microsoft.Extensions.DependencyModel.Resolution;

namespace Amazon.Lambda.TestTool.Runtime
{
    // This code was taken from the blog post by Samuel Cragg on CodeProject
    // https://www.codeproject.com/Articles/1194332/Resolving-Assemblies-in-NET-Core
    // Licensed under CPOP: https://www.codeproject.com/info/cpol10.aspx
    public class LambdaAssemblyResolver : IDisposable
    {
        private readonly ICompilationAssemblyResolver assemblyResolver;
        private readonly DependencyContext dependencyContext;
        private readonly AssemblyLoadContext loadContext;

        public LambdaAssemblyResolver(string depsJsonPath)
        {
            this.Assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(depsJsonPath);
            this.dependencyContext = DependencyContext.Load(this.Assembly);
    
            this.assemblyResolver = new CompositeCompilationAssemblyResolver
                                    (new ICompilationAssemblyResolver[]
            {
                new AppBaseCompilationAssemblyResolver(Path.GetDirectoryName(depsJsonPath)),
                new ReferenceAssemblyPathResolver(),
                new PackageCompilationAssemblyResolver()
            });
    
            this.loadContext = AssemblyLoadContext.GetLoadContext(this.Assembly);
            this.loadContext.Resolving += OnResolving;
        }
    
        public Assembly Assembly { get; }

        public Assembly LoadAssembly(string assemblyName)
        {
            return Assembly.Load(assemblyName);
        }
    
        public void Dispose()
        {
            this.loadContext.Resolving -= this.OnResolving;
        }
    
        private Assembly OnResolving(AssemblyLoadContext context, AssemblyName name)
        {
            bool NamesMatch(RuntimeLibrary runtime)
            {
                return string.Equals(runtime.Name, name.Name, StringComparison.OrdinalIgnoreCase);
            }

            bool ResourceAssetPathMatch(RuntimeLibrary runtime)
            {
                foreach(var group in runtime.RuntimeAssemblyGroups)
                {
                    foreach(var path in group.AssetPaths)
                    {
                        if(path.EndsWith("/" + name.Name + ".dll"))
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
    
            RuntimeLibrary library =
                this.dependencyContext.RuntimeLibraries.FirstOrDefault(NamesMatch);

            if(library == null)
                library = this.dependencyContext.RuntimeLibraries.FirstOrDefault(ResourceAssetPathMatch);

            if (library != null)
            {
                var wrapper = new CompilationLibrary(
                    library.Type,
                    library.Name,
                    library.Version,
                    library.Hash,
                    library.RuntimeAssemblyGroups.SelectMany(g => g.AssetPaths),
                    library.Dependencies,
                    library.Serviceable);
    
                var assemblies = new List<string>();
                this.assemblyResolver.TryResolveAssemblyPaths(wrapper, assemblies);
                if (assemblies.Count > 0)
                {
                    return this.loadContext.LoadFromAssemblyPath(assemblies[0]);
                }
            }
    
            return null;
        }
    }
}