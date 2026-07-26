namespace OpsMonitor.Widget.Models;

public readonly record struct WidgetSizeRecommendation(
    double MinimumWidth,
    double MinimumHeight,
    double SuggestedWidth,
    double SuggestedHeight);

public static class WidgetSizingPolicy
{
    public static WidgetSizeRecommendation Calculate(
        WidgetLayout layout,
        WidgetDensity density,
        int visibleModuleCount,
        int scalePercent)
    {
        var recommendation = OpsMonitor.Core.Settings.WidgetSizingPolicy.Calculate(
            layout switch
            {
                WidgetLayout.Pill => OpsMonitor.Core.Settings.WidgetDesign.Pill,
                WidgetLayout.Dock => OpsMonitor.Core.Settings.WidgetDesign.Dock,
                WidgetLayout.Mini => OpsMonitor.Core.Settings.WidgetDesign.Canvas,
                _ => OpsMonitor.Core.Settings.WidgetDesign.Rail
            },
            density switch
            {
                WidgetDensity.Compact => OpsMonitor.Core.Settings.WidgetDensity.Compact,
                WidgetDensity.Detail => OpsMonitor.Core.Settings.WidgetDensity.Comfortable,
                _ => OpsMonitor.Core.Settings.WidgetDensity.Normal
            },
            visibleModuleCount,
            scalePercent);
        return new WidgetSizeRecommendation(
            recommendation.MinimumWidth,
            recommendation.MinimumHeight,
            recommendation.SuggestedWidth,
            recommendation.SuggestedHeight);
    }
}
