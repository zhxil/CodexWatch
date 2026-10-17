using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
namespace CodexWatch;
public sealed class QuotaRing : Grid
{
 private readonly Ellipse track;
 private readonly Microsoft.UI.Xaml.Shapes.Path arc;
 private readonly Ellipse full;
 public QuotaRing()
 {
  Width=112; Height=112;
  track=new Ellipse { Width=100,Height=100,StrokeThickness=7,Stroke=new SolidColorBrush(Windows.UI.Color.FromArgb(32,128,128,128)) };
  full=new Ellipse { Width=100,Height=100,StrokeThickness=7,Visibility=Visibility.Collapsed };
  arc=new Microsoft.UI.Xaml.Shapes.Path { StrokeThickness=7,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round };
  Children.Add(track);Children.Add(full);Children.Add(arc);
 }
 public void Update(int? remaining,Windows.UI.Color color)
 {
  var brush=new SolidColorBrush(color); arc.Stroke=brush;full.Stroke=brush;
  full.Visibility=remaining is >=100 ? Visibility.Visible : Visibility.Collapsed;
  arc.Visibility=remaining is >0 and <100 ? Visibility.Visible : Visibility.Collapsed;
  if(remaining is not (>0 and <100)) return;
  var angle=remaining.Value/100d*Math.PI*2;
  var figure=new PathFigure { StartPoint=new Point(56,9.5),IsClosed=false };
  figure.Segments.Add(new ArcSegment { Point=new Point(56+46.5*Math.Sin(angle),56-46.5*Math.Cos(angle)),Size=new Size(46.5,46.5),IsLargeArc=remaining.Value>50,SweepDirection=SweepDirection.Clockwise });
  var geometry=new PathGeometry();geometry.Figures.Add(figure);arc.Data=geometry;
 }
}
