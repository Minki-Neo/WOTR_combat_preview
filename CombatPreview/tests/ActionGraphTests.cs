using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using CombatPreview;
static class ActionGraphTests
{
    public static void Run()
    {
        var document = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "BoltOfJustice.actiongraph.json")));
        var roots = document["actions"].AsArray().ToArray();
        var kinds = new List<string>();
        bool complete = ActionGraphScan.Visit(roots, Children, node => kinds.Add(node["type"].GetValue<string>()), out int count);
        if (!complete || kinds.Count(kind => kind == "ContextActionSavingThrow") != 4
            || kinds.Count(kind => kind == "ContextActionDealDamage") != 5)
            throw new Exception("real conditional spell action topology must retain every damage and follow-up save");
        if (ActionGraphScan.Visit(roots, Children, _ => { }, out _, maximumDepth: 4))
            throw new Exception("fixture must reproduce the original depth-four truncation");
        if (ActionGraphScan.Visit(roots, Children, _ => { }, out _, maximumNodes: 2))
            throw new Exception("bounded traversal must report incomplete discovery honestly");
        Console.WriteLine("PASS: real spell topology regression, " + count + " actions (not game execution)");
    }
    static IEnumerable<JsonNode> Children(JsonNode node)
        => node.AsObject().Where(property => property.Value is JsonArray)
            .SelectMany(property => property.Value.AsArray());
}
