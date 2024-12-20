#!/usr/bin/env dotnet-script

#r "nuget: Newtonsoft.Json, 13.0.0"

using Newtonsoft.Json.Linq;

var srcPath = Path.GetFullPath("src");
var packages = Directory.EnumerateFiles(srcPath, "package.json", SearchOption.AllDirectories)
    .Select(file =>
    {
        var content = File.ReadAllText(file);
        var json = JObject.Parse(content);
        return json["name"]?.ToString();
    })
    .Where(name => !string.IsNullOrEmpty(name))
    .ToList();

foreach (var package in packages)
{
    Console.WriteLine(package);
}