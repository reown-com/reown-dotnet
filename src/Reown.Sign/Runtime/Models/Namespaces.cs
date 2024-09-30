using System;
using System.Collections.Generic;
using System.Linq;

namespace Reown.Sign.Models
{
    /// <summary>
    ///     A dictionary of Namespaces based on a chainId key. The chainId
    ///     should follow CAIP-2 format
    ///     chain_id:    namespace + ":" + reference
    ///     namespace:   [-a-z0-9]{3,8}
    ///     reference:   [-_a-zA-Z0-9]{1,32}
    /// </summary>
    public class Namespaces : SortedDictionary<string, Namespace>
    {
        public Namespaces()
        {
        }

        public Namespaces(Namespaces namespaces) : base(namespaces)
        {
        }

        public Namespaces(RequiredNamespaces requiredNamespaces)
        {
            WithProposedNamespaces(requiredNamespaces);
        }

        public Namespaces(Dictionary<string, ProposedNamespace> proposedNamespaces)
        {
            WithProposedNamespaces(proposedNamespaces);
        }

        public Namespaces WithNamespace(string chainNamespace, Namespace nm)
        {
            Add(chainNamespace, nm);
            return this;
        }

        public Namespace At(string chainNamespace)
        {
            return this[chainNamespace];
        }

        public Namespaces WithProposedNamespaces(IDictionary<string, ProposedNamespace> proposedNamespaces)
        {
            foreach (var (chainNamespace, requiredNamespace) in proposedNamespaces)
            {
                Add(chainNamespace, new Namespace(requiredNamespace));
            }

            return this;
        }

        public static Namespaces FromAccounts(string[] accounts)
        {
            var namespaces = new Namespaces();
            foreach (var account in accounts)
            {
                var split = account.Split(":");
                var @namespace = split[0];
                var chainId = split[1];
                if (!namespaces.ContainsKey(@namespace))
                {
                    namespaces[@namespace] = new Namespace
                    {
                        Accounts = new[]
                        {
                            account
                        },
                        Chains = new[]
                        {
                            $"{@namespace}:{chainId}"
                        },
                        Events = Array.Empty<string>()
                    };
                }
                else
                {
                    namespaces[@namespace].Accounts = namespaces[@namespace].Accounts.Append(account).ToArray();
                    namespaces[@namespace].Chains = namespaces[@namespace].Chains.Append($"{@namespace}:{chainId}").ToArray();
                }
            }

            return namespaces;
        }

        public static Namespaces FromAuth(ICollection<string> methods, ICollection<string> accounts)
        {
            var formattedAccounts = accounts.Select(account => account.Replace("did:pkh:", string.Empty)).ToArray();
            var namespaces = FromAccounts(formattedAccounts);

            foreach (var values in namespaces.Values)
            {
                values.Methods = values.Methods == null
                    ? methods.ToArray()
                    : values.Methods.Concat(methods).ToArray();

                values.Events = new[]
                {
                    "chainChanged",
                    "accountsChanged"
                };
            }

            return namespaces;
        }
    }
}