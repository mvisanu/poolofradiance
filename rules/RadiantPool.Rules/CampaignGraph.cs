using System;
using System.Collections.Generic;
using System.Linq;

namespace RadiantPool.Rules
{
    /// <summary>A quest or playable location in a prerequisite graph. Campaign structure
    /// belongs in the pure rules layer so saves, server progression, tests, and editor
    /// tooling all make the same unlock decision.</summary>
    public sealed class CampaignNode
    {
        public string Id { get; }
        public IReadOnlyList<string> Prerequisites { get; }
        public bool StartsAvailable { get; }
        public bool IsFinale { get; }

        public CampaignNode(string id, IEnumerable<string>? prerequisites = null,
            bool startsAvailable = false, bool isFinale = false)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Campaign node id is required.", nameof(id));
            Id = id;
            Prerequisites = (prerequisites ?? Array.Empty<string>()).Distinct().ToArray();
            StartsAvailable = startsAvailable;
            IsFinale = isFinale;
        }
    }

    /// <summary>Deterministic campaign progression for parallel commissions and optional
    /// branches. It replaces the brittle assumption that completing array slot N always
    /// unlocks slot N+1.</summary>
    public static class CampaignGraph
    {
        public static IReadOnlyList<string> Eligible(
            IEnumerable<CampaignNode> nodes,
            IEnumerable<string> completed,
            IEnumerable<string>? started = null)
        {
            var all = nodes.ToList();
            Validate(all);
            var done = new HashSet<string>(completed ?? Array.Empty<string>());
            var begun = new HashSet<string>(started ?? Array.Empty<string>());

            return all.Where(n => !done.Contains(n.Id) && !begun.Contains(n.Id)
                    && (n.StartsAvailable || (n.Prerequisites.Count > 0
                        && n.Prerequisites.All(done.Contains))))
                .Select(n => n.Id).ToArray();
        }

        public static bool FinaleComplete(IEnumerable<CampaignNode> nodes,
            IEnumerable<string> completed)
        {
            var all = nodes.ToList();
            Validate(all);
            var done = new HashSet<string>(completed ?? Array.Empty<string>());
            var finales = all.Where(n => n.IsFinale).ToList();
            return finales.Count > 0 && finales.All(n => done.Contains(n.Id));
        }

        /// <summary>Reject duplicate ids, missing prerequisites, and cycles at startup,
        /// before a campaign can strand a player in an impossible save state.</summary>
        public static void Validate(IEnumerable<CampaignNode> nodes)
        {
            var all = nodes.ToList();
            var byId = new Dictionary<string, CampaignNode>();
            foreach (var node in all)
            {
                if (byId.ContainsKey(node.Id))
                    throw new InvalidOperationException($"Duplicate campaign node '{node.Id}'.");
                byId[node.Id] = node;
            }

            foreach (var node in all)
                foreach (string prerequisite in node.Prerequisites)
                    if (!byId.ContainsKey(prerequisite))
                        throw new InvalidOperationException(
                            $"Campaign node '{node.Id}' requires missing node '{prerequisite}'.");

            var visiting = new HashSet<string>();
            var visited = new HashSet<string>();
            bool Visit(string id)
            {
                if (visiting.Contains(id)) return false;
                if (visited.Contains(id)) return true;
                visiting.Add(id);
                foreach (string prerequisite in byId[id].Prerequisites)
                    if (!Visit(prerequisite)) return false;
                visiting.Remove(id);
                visited.Add(id);
                return true;
            }

            foreach (string id in byId.Keys)
                if (!Visit(id))
                    throw new InvalidOperationException(
                        $"Campaign prerequisite cycle includes '{id}'.");
        }
    }
}
