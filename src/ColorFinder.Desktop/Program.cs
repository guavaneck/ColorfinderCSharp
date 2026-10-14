using System.Drawing;
using ColorFinder.Core.Colors;
using ColorFinder.Core.FileSystem;
using ColorFinder.Core.Navigation;
using ColorFinder.Core.Rules;
using ColorFinder.Desktop.Interop;
using ColorFinder.Desktop.Platform;
using Photino.NET;

var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
var initialPath = args.FirstOrDefault() is { } requestedPath && Directory.Exists(requestedPath)
    ? Path.GetFullPath(requestedPath)
    : home;
var bridge = new AppBridge(new FileSystemService(), JsonColorService.CreateDefault(), JsonColorRuleService.CreateDefault(), new NavigationService(initialPath), new PlatformService());
var indexPath = Path.Combine(AppContext.BaseDirectory, "ui", "index.html");

if (!File.Exists(indexPath))
    throw new FileNotFoundException("The ColorFinder UI was not copied to the output directory.", indexPath);

var window = new PhotinoWindow()
    .SetTitle("ColorFinder")
    .SetUseOsDefaultSize(false)
    .SetSize(new Size(1180, 760))
    .SetMinSize(760, 480)
    .Center()
    .RegisterWebMessageReceivedHandler((sender, message) =>
    {
        var sourceWindow = sender as PhotinoWindow
            ?? throw new InvalidOperationException("Photino supplied an invalid message sender.");
        sourceWindow.SendWebMessage(bridge.HandleAsync(message).GetAwaiter().GetResult());
    })
    .Load(indexPath);

window.WaitForClose();
