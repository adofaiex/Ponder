#!/usr/bin/env dotnet-script

using System.IO.Compression;

var projectDir = Directory.GetCurrentDirectory();
var distDir = Path.Combine(projectDir, "dist");
var coreOut = Path.Combine(projectDir, "core", "out");
var modName = "Ponder";

Directory.CreateDirectory(distDir);

void AddDir(ZipArchive zip, string sourceDir, string prefix)
{
    if (!Directory.Exists(sourceDir)) return;
    foreach (var f in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
    {
        var rel = Path.GetRelativePath(sourceDir, f).Replace('\\', '/');
        zip.CreateEntryFromFile(f, prefix + rel);
    }
}

void Pack(string loader, string prefix)
{
    var outDir = Path.Combine(projectDir, "loaders", loader, "out");
    if (!Directory.Exists(outDir))
    {
        Console.WriteLine($"  skip {loader}: out/ not found");
        return;
    }
    var path = Path.Combine(distDir, $"{modName}_{loader}.zip");
    using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
    AddDir(zip, outDir, prefix);
    AddDir(zip, Path.Combine(coreOut, "Resources"), prefix);
    Console.WriteLine($"  {path}");
}

Pack("umm", $"Mods/{modName}/");
Pack("melon", "Mods/");
Pack("bepinex", $"BepInEx/plugins/{modName}/");

Console.WriteLine($"Done → {distDir}/");
