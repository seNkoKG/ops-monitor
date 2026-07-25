using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace OpsMonitor.Widget.ViewModels;

internal sealed record ThemeDefinition(
    string Name,
    Color Surface,
    Color Card,
    Color Border,
    Color TextPrimary,
    Color TextSecondary,
    Color Cyan,
    Color Magenta,
    Color Mint,
    Color Amber);
