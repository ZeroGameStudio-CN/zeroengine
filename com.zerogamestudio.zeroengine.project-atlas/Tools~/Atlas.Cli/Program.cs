using ZeroEngine.ProjectAtlas;

if (args.Length != 2 || (args[0] != "read" && args[0] != "check"))
{
    Console.Error.WriteLine("Usage: atlas <read|check> <project-root>. read regenerates the local schema-2 index; check validates authored structure only. Unity coverage remains required.");
    return 2;
}
try
{
    var graph = ProjectAtlasCatalogLoader.LoadAuthoringProject(Path.GetFullPath(args[1]));
    foreach (var diagnostic in graph.Diagnostics)
        Console.Error.WriteLine($"{diagnostic.Severity} {diagnostic.Code}: {diagnostic.SourcePath}: {diagnostic.Message}");
    if (graph.HasErrors || graph.Project == null) return 1;
    if (args[0] == "check") return 0;
    // Schema 1 still owns a tracked Editor projection: never silently rewrite it offline.
    if (graph.UsesLocalIndex) ProjectAtlasProjectWriter.WriteGeneratedIndex(graph);
    Console.Write(ProjectAtlasMarkdownProjector.Render(graph));
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
