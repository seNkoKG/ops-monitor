using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace OpsMonitor.Widget.ViewModels;

internal sealed record ThemeDefinition(
    string Name,
    string? CoreThemeId,
    Color Surface,
    Color Card,
    Color Border,
    Color TextPrimary,
    Color TextSecondary,
    Color CpuAccent,
    Color GpuAccent,
    Color MemoryAccent,
    Color NetworkAccent,
    Color Warning,
    Color Critical,
    string FontFamily,
    double LabelSize,
    double ValueSize,
    double MinimumReadableSize,
    int LabelWeight,
    int ValueWeight,
    bool UseTabularNumbers);
