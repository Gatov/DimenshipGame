using Dimenship.Core.Content;
using Dimenship.Replay;

// dotnet run --project tools/Dimenship.Replay -- <content-root> <script.json>
//
// The content root is an argument rather than a fixed path so the same script can be run against
// the tree before a change and after it, which is what every later scheduling ticket reports.

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: Dimenship.Replay <content-root> <script.json>");
    return 2;
}

var content = new JsonContentSource(new DirectoryContentFileSystem(args[0])).Load();
if (!content.Succeeded)
{
    foreach (var error in content.Errors)
    {
        Console.Error.WriteLine(error);
    }

    return 1;
}

var parsed = ReplayScript.Parse(File.ReadAllText(args[1]), content.Catalog!, content.Scenarios);
if (!parsed.Succeeded)
{
    foreach (var error in parsed.Errors)
    {
        Console.Error.WriteLine(error);
    }

    return 1;
}

var script = parsed.Script!;
var scenario = content.Scenarios.Single(s => s.Id == script.Scenario);
Console.Out.Write(ReplayReport.Format(Replay.Run(content.Catalog!, scenario, script)));
return 0;
