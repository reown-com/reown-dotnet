using System;
using Reown.Sign.Controllers;

namespace Reown.Sign.Models
{
    public class DefaultsLoadingEventArgs : EventArgs
    {
        public DefaultsLoadingEventArgs(in AddressProvider.DefaultData defaults)
        {
            Defaults = defaults;
        }

        public AddressProvider.DefaultData Defaults { get; }
    }
}