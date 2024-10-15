using Newtonsoft.Json.Serialization;

namespace Reown.Core.Common.Utils
{
    /// <summary>
    ///     Newtonsoft.Json naming strategy that converts property names to lower case
    /// </summary>
    public class LowerCaseNamingStrategy : NamingStrategy
    {
        protected override string ResolvePropertyName(string name)
        {
            return name.ToLowerInvariant();
        }
    }
}