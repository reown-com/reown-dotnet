using System;
using System.Collections.Generic;
using System.Linq;
using Reown.Core.Interfaces;

namespace Reown.Core.Controllers
{
    /// <summary>
    ///     A mapping of topics to a list of subscription ids
    /// </summary>
    public class TopicMap : ISubscriberMap
    {
        private readonly Dictionary<string, List<string>> _topicMap = new();

        /// <summary>
        ///     An array of topics in this mapping
        /// </summary>
        public string[] Topics
        {
            get => _topicMap.Keys.ToArray();
        }

        /// <summary>
        ///     Add an subscription id to the given topic
        /// </summary>
        /// <param name="topic">The topic to add the subscription id to</param>
        /// <param name="id">The subscription id to add</param>
        public void Set(string topic, string id)
        {
            if (Exists(topic, id)) return;

            if (!_topicMap.ContainsKey(topic))
                _topicMap.Add(topic, new List<string>());

            var ids = _topicMap[topic];
            ids.Add(id);
        }

        /// <summary>
        ///     Get an array of all subscription ids in a given topic
        /// </summary>
        /// <param name="topic">The topic to get subscription ids for</param>
        /// <returns>An array of subscription ids in a given topic</returns>
        public string[] Get(string topic)
        {
            if (!_topicMap.ContainsKey(topic))
                return Array.Empty<string>();

            return _topicMap[topic].ToArray();
        }

        /// <summary>
        ///     Determine whether a subscription id exists in a given topic
        /// </summary>
        /// <param name="topic">The topic to check in</param>
        /// <param name="id">The subscription id to check for</param>
        /// <returns>True if the subscription id is in the topic, false otherwise</returns>
        public bool Exists(string topic, string id)
        {
            var ids = Get(topic);
            return ids.Contains(id);
        }

        /// <summary>
        ///     Delete subscription id from a topic. If no subscription id is given,
        ///     then all subscription ids in the given topic are removed.
        /// </summary>
        /// <param name="topic">The topic to remove from</param>
        /// <param name="id">The subscription id to remove, if set to null then all ids are removed from the topic</param>
        public void Delete(string topic, string id = null)
        {
            if (!_topicMap.TryGetValue(topic, out var ids))
            {
                return;
            }

            if (id == null)
            {
                _topicMap.Remove(topic);
            }
            else
            {
                ids.Remove(id);
                if (ids.Count == 0)
                {
                    _topicMap.Remove(topic);
                }
            }
        }

        /// <summary>
        ///     Clear all entries in this TopicMap
        /// </summary>
        public void Clear()
        {
            _topicMap.Clear();
        }
    }
}