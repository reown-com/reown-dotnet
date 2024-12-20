#!/usr/bin/env dotnet-script

using System.Xml.Linq;

string GetVersion()
{
    var currentDir = Directory.GetCurrentDirectory();
    var rootDir = Path.GetFullPath(Path.Combine(currentDir, "src"));
    var buildPropsPath = Path.Combine(rootDir, "Directory.Build.props");
    
    if (!File.Exists(buildPropsPath))
        throw new Exception($"Could not find Directory.Build.props at {buildPropsPath}");
        
    var doc = XDocument.Load(buildPropsPath);
    
    var version = doc.Descendants()
        .Elements()
        .FirstOrDefault(e => e.Name.LocalName == "DefaultVersion")
        ?.Value;

    if (string.IsNullOrEmpty(version))
        throw new Exception("DefaultVersion not found in Directory.Build.props");

    return version;
}

var version = GetVersion();
Console.WriteLine(version); 