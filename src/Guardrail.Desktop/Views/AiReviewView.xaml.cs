using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using Guardrail.Services;

namespace Guardrail.Views;

public sealed record PipelineStep(int Number, string Title, string Detail);

/// <summary>Crash Review AI — automatic video fault analysis. UI only; no analysis is performed yet.</summary>
public partial class AiReviewView : UserControl
{
    public AiReviewView()
    {
        InitializeComponent();

        Steps.ItemsSource = new[]
        {
            new PipelineStep(1, "Ingest the footage", "Stabilize, sync multiple angles and read the dashcam's speed / GPS overlay when present."),
            new PipelineStep(2, "Detect and track", "Find every vehicle, pedestrian, lane line, sign and traffic signal frame by frame."),
            new PipelineStep(3, "Reconstruct the event", "Estimate speed, following distance, lane position and reaction times leading up to impact."),
            new PipelineStep(4, "Apply the rules of the road", "Right-of-way, signals, speed limits, following distance and FMCSA-style preventability criteria."),
            new PipelineStep(5, "Decide fault automatically", "Assign fault percentages with a confidence score, key frames and a written explanation — no reviewer needed."),
            new PipelineStep(6, "File the report", "Attach the result to the incident and Accident Register, and open driver coaching actions."),
        };

        Timeline.SizeChanged += (_, _) => DrawTimeline();
    }

    private void DrawTimeline()
    {
        Timeline.Children.Clear();
        double width = Timeline.ActualWidth;
        if (width <= 0) return;

        // Example markers at 17s, 19s, 20s, 21s of a 42s clip.
        foreach (var (seconds, isImpact) in new[] { (17.0, false), (19.0, false), (20.0, false), (21.0, true) })
        {
            var dot = new Ellipse { Width = isImpact ? 14 : 10, Height = isImpact ? 14 : 10 };
            dot.SetResourceReference(Shape.FillProperty, isImpact ? "Brush.Danger" : "Brush.Accent");
            Canvas.SetLeft(dot, seconds / 42.0 * width - dot.Width / 2);
            Canvas.SetTop(dot, 14 - dot.Height / 2);
            Timeline.Children.Add(dot);
        }
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            Toaster.Show($"Got \"{System.IO.Path.GetFileName(files[0])}\". Automatic AI fault analysis is coming soon.");
    }

    private void Soon_Click(object sender, RoutedEventArgs e) => Toaster.Soon(sender);
}
