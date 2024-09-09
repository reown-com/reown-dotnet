using System;
using System.Collections.Generic;

namespace Reown.Core.Common.Model
{
    /// <summary>
    ///     A mock module that represents nothing.
    ///     Useful for creating an emptycontext
    /// </summary>
    public sealed class IsolatedModule : IModule
    {
        private static readonly HashSet<Guid> ActiveModules = new();

        private readonly Guid _guid;

        public IsolatedModule()
        {
            do
            {
                _guid = Guid.NewGuid();
            } while (ActiveModules.Contains(_guid));

            ActiveModules.Add(_guid);
        }

        public string Name
        {
            get => $"isolated-module-{Context}";
        }

        public string Context
        {
            get => $"IM-{_guid.ToString()}";
        }

        public void Dispose()
        {
        }
    }
}