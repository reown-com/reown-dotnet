using System;

namespace Reown.Core.Common.Utils
{
    // https://docs.unity3d.com/ScriptReference/Scripting.PreserveAttribute.html
    [AttributeUsage(AttributeTargets.Method
                    | AttributeTargets.Class
                    | AttributeTargets.Field
                    | AttributeTargets.Property
                    | AttributeTargets.Constructor
                    | AttributeTargets.Interface
                    | AttributeTargets.Delegate
                    | AttributeTargets.Event
                    | AttributeTargets.Struct
                    | AttributeTargets.Assembly
                    | AttributeTargets.Enum, Inherited = false)]
    public class PreserveAttribute : Attribute
    {
    }
}