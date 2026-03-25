using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Proteomics;
using Tasks;
using Tasks.CoverageMapConfiguration;
using Proteomics.ProteolyticDigestion;

namespace GUI
{
    public enum CoverageMapDisplayMode
    {
        ProteaseLane,
        PeptidePerBar
    }

    static class SequenceCoverageMap
    {
        // ── Bar geometry constants ────────────────────────────────────────────
        public const int SeqTextHeight = 20;
        public const int BarHeight = 6;
        public const int BarRowGap = 5;
        public const int BarTopMargin = 6;
        public const int BottomLineGap = 12;

        public static int Highlight(int start, int end, Canvas map, Dictionary<int, List<int>> indices,
            int height, Color clr, bool unique, bool startPep, bool endPep, int partial = -1,
            int residueSpacing = 25, int seqLeftOffset = 45)
        {
            int increment = 0;
            int i;

            if (partial >= 0) // if partial peptide 
            {
                increment = partial * 10;
                i = partial;
            }
            else
            {
                // determine where to highlight peptide
                for (i = 0; i < indices.Count; ++i)
                {
                    // only does this if partially highlighted peptides dont continue on the first line
                    if (!indices.ContainsKey(i))
                    {
                        indices.Add(i, new List<int>());
                    }

                    // check if 
                    if (!indices[i].Any(d => d == start))
                    {
                        break;
                    }

                    increment += 10;
                }
            }

            // update list of drawn/highlighted peptides
            if (indices.ContainsKey(i))
            {
                indices[i].AddRange(Enumerable.Range(start, end - start + 1));
            }
            else
            {
                indices.Add(i, Enumerable.Range(start, end - start + 1).ToList());
            }

            // highlight peptide
            if (unique)
            {
                peptideLineDrawing(map, new Point(start * residueSpacing + seqLeftOffset, height + increment),
                    new Point(end * residueSpacing + seqLeftOffset, height + increment), clr, false, startPep, endPep);
            }
            else
            {
                peptideLineDrawing(map, new Point(start * residueSpacing + seqLeftOffset, height + increment),
                    new Point(end * residueSpacing + seqLeftOffset, height + increment), clr, true, startPep, endPep);
            }

            return i;
        }

        /// <summary>
        /// Draws a single amino acid character for COVERED residues.
        /// Uses Bold font for unique peptides, ExtraBold for shared peptides.
        /// </summary>
        public static void txtDrawing(Canvas cav, Point loc, string txt, Brush clr)
        {
            TextBlock tb = new TextBlock();
            tb.Foreground = clr;
            tb.Text = txt;
            tb.FontSize = 15;
            if (clr == Brushes.Black)
            {
                tb.FontWeight = FontWeights.Bold;
            }
            else
            {
                tb.FontWeight = FontWeights.ExtraBold;
            }
            tb.FontFamily = new FontFamily("Arial");

            Canvas.SetTop(tb, loc.Y);
            Canvas.SetLeft(tb, loc.X);
            Panel.SetZIndex(tb, 2);
            cav.Children.Add(tb);
            cav.UpdateLayout();
        }

        /// <summary>
        /// Draws a single amino acid character for UNCOVERED residues.
        /// Uses normal font weight with underline decoration.
        /// </summary>
        public static void txtDrawingUncovered(Canvas cav, Point loc, string txt, Brush clr)
        {
            TextBlock tb = new TextBlock();
            tb.Foreground = clr;
            tb.Text = txt;
            tb.FontSize = 15;
            tb.FontWeight = FontWeights.Normal;
            tb.FontFamily = new FontFamily("Arial");

            // Add underline decoration for uncovered amino acids
            tb.TextDecorations = TextDecorations.Underline;

            Canvas.SetTop(tb, loc.Y);
            Canvas.SetLeft(tb, loc.X);
            Panel.SetZIndex(tb, 2);
            cav.Children.Add(tb);
            cav.UpdateLayout();
        }
        /// <summary>
        /// Draws a single amino acid character for residues covered by SHARED peptides only.
        /// Uses normal font weight with reduced opacity (translucent), no underline.
        /// </summary>
        public static void txtDrawingShared(Canvas cav, Point loc, string txt, Brush clr)
        {
            TextBlock tb = new TextBlock();
            tb.Text = txt;
            tb.FontSize = 15;
            tb.FontWeight = FontWeights.Normal;
            tb.FontFamily = new FontFamily("Arial");

            // Create a translucent brush for shared peptide coverage
            if (clr == Brushes.Red)
            {
                tb.Foreground = new SolidColorBrush(Colors.Red) { Opacity = 0.5 };
            }
            else
            {
                tb.Foreground = new SolidColorBrush(Colors.Black) { Opacity = 0.5 };
            }

            Canvas.SetTop(tb, loc.Y);
            Canvas.SetLeft(tb, loc.X);
            Panel.SetZIndex(tb, 2);
            cav.Children.Add(tb);
            cav.UpdateLayout();
        }
        public static void txtDrawingLabel(Canvas cav, Point loc, string txt, Brush clr)
        {
            TextBlock tb = new TextBlock();
            tb.Foreground = clr;
            tb.Text = txt;
            tb.FontSize = 10;
            if (clr == Brushes.Black)
            {
                tb.FontWeight = FontWeights.Bold;
            }
            else
            {
                tb.FontWeight = FontWeights.ExtraBold;
            }
            tb.FontFamily = new FontFamily("Arial");

            Canvas.SetTop(tb, loc.Y);
            Canvas.SetLeft(tb, loc.X);
            Panel.SetZIndex(tb, 2);
            cav.Children.Add(tb);
            cav.UpdateLayout();
        }

        // draw line for peptides
        public static void peptideLineDrawing(Canvas cav, Point start, Point end, Color clr, bool shared, bool pepStart, bool pepEnd)
        {

            // draw top
            Line top = new Line();
            top.Stroke = new SolidColorBrush(clr);
            if (pepStart == false)
            {
                top.X1 = start.X - 10;
            }
            else
            {
                top.X1 = start.X;
            }

            if (pepEnd == false)
            {
                top.X2 = end.X + 21;
            }
            else
            {
                top.X2 = end.X + 11;
            }

            top.Y1 = start.Y + 20;
            top.Y2 = end.Y + 20;
            top.StrokeThickness = 3.25;
            top.StrokeStartLineCap = PenLineCap.Round;
            top.StrokeEndLineCap = PenLineCap.Round;

            if (shared)
            {
                top.Stroke.Opacity = 0.35;
            }

            cav.Children.Add(top);

            Canvas.SetZIndex(top, 1);
        }

        public static void circledTxtDraw(Canvas cav, Point loc, SolidColorBrush clr)
        {
            Ellipse circle = new Ellipse()
            {
                Width = 17,
                Height = 17,
                Stroke = clr,
                StrokeThickness = 1,
                Fill = clr,
                Opacity = 0.85
            };
            Canvas.SetLeft(circle, loc.X);
            Canvas.SetTop(circle, loc.Y - .75);
            Panel.SetZIndex(circle, 1);
            cav.Children.Add(circle);
        }

        public static void stackedCircledTxtDraw(Canvas cav, Point loc, List<SolidColorBrush> clr)
        {
            int circleCount = 0;
            foreach (var mod in clr)
            {
                Ellipse circle = new Ellipse()
                {
                    Width = 17,
                    Height = 17,
                    Stroke = mod,
                    StrokeThickness = 1,
                    Fill = mod,
                    Opacity = 0.85
                };
                Canvas.SetLeft(circle, loc.X);
                Canvas.SetTop(circle, ((loc.Y - .75) - (circleCount * 18)));
                Panel.SetZIndex(circle, 1);
                cav.Children.Add(circle);
                circleCount++;
            }

        }

        public static void drawLegend(Canvas cav, Dictionary<string, Color> proteaseByColor, List<string> proteases, Grid legend, bool variants)
        {
            int i = -1;
            legend.RowDefinitions.Add(new RowDefinition());
            Label legendLabel = new Label();
            legendLabel.Content = "Key: ";
            legend.Children.Add(legendLabel);
            Grid.SetRow(legendLabel, 0);
            legend.RowDefinitions.Add(new RowDefinition());
            int proteaseRows = Convert.ToInt32(Math.Ceiling((proteases.Count() / 3.0)));
            int j = 0;
            while (j < proteaseRows)
            {
                legend.RowDefinitions.Add(new RowDefinition());
                j++;
            }
            legend.RowDefinitions.Add(new RowDefinition());

            legend.ColumnDefinitions.Add(new ColumnDefinition());
            legend.ColumnDefinitions.Add(new ColumnDefinition());
            legend.ColumnDefinitions.Add(new ColumnDefinition());
            legend.ColumnDefinitions.Add(new ColumnDefinition());
            legend.ColumnDefinitions.Add(new ColumnDefinition());
            legend.ColumnDefinitions.Add(new ColumnDefinition());

            // Updated to include three categories
            string[] peptideCategories = new string[3]
            {
                "Shared peptides (translucent)",
                "Unique peptides (bold)",
                "Not covered (underlined)"
            };

            foreach (string peptide in peptideCategories)
            {
                if (peptide.Equals("Not covered (underlined)"))
                {
                    // Draw underlined text example for uncovered residues
                    TextBlock uncoveredExample = new TextBlock();
                    uncoveredExample.Text = "ABC";
                    uncoveredExample.FontSize = 12;
                    uncoveredExample.FontWeight = FontWeights.Normal;
                    uncoveredExample.TextDecorations = TextDecorations.Underline;
                    uncoveredExample.HorizontalAlignment = HorizontalAlignment.Center;
                    uncoveredExample.VerticalAlignment = VerticalAlignment.Center;

                    Label pepLabel = new Label();
                    pepLabel.Content = peptide;
                    pepLabel.FontSize = 12;

                    legend.Children.Add(uncoveredExample);
                    legend.Children.Add(pepLabel);
                    Grid.SetColumn(uncoveredExample, ++i);
                    Grid.SetRow(uncoveredExample, 1);
                    Grid.SetColumn(pepLabel, ++i);
                    Grid.SetRow(pepLabel, 1);
                }
                else
                {
                    Line pepLine = new Line();
                    pepLine.X1 = 0;
                    pepLine.X2 = 50;
                    pepLine.Y1 = 0;
                    pepLine.Y2 = 0;
                    pepLine.StrokeThickness = 4;
                    pepLine.Stroke = new SolidColorBrush(Colors.Black);
                    if (peptide.Equals("Shared peptides (translucent)"))
                    {
                        pepLine.Stroke.Opacity = 0.35;
                    }
                    pepLine.HorizontalAlignment = HorizontalAlignment.Center;
                    pepLine.VerticalAlignment = VerticalAlignment.Center;

                    Label pepLabel = new Label();
                    pepLabel.Content = peptide;
                    pepLabel.FontSize = 12;

                    legend.Children.Add(pepLine);
                    legend.Children.Add(pepLabel);
                    Grid.SetColumn(pepLine, ++i);
                    Grid.SetRow(pepLine, 1);
                    Grid.SetColumn(pepLabel, ++i);
                    Grid.SetRow(pepLabel, 1);
                }
            }

            if (variants == true)
            {
                legend.ColumnDefinitions.Add(new ColumnDefinition());
                Label variantLabel = new Label();
                variantLabel.Content = "Sequence Variant";
                variantLabel.Foreground = Brushes.Red;
                variantLabel.FontWeight = FontWeights.ExtraBold;
                variantLabel.FontSize = 12;
                legend.Children.Add(variantLabel);
                Grid.SetColumn(variantLabel, ++i);
                Grid.SetRow(variantLabel, 1);
            }
            i = -1;
            j = 1;
            int proteaseCount = 0;
            foreach (var protease in proteases)
            {
                proteaseCount++;
                legend.ColumnDefinitions.Add(new ColumnDefinition());
                legend.ColumnDefinitions.Add(new ColumnDefinition());
                Label proteaseName = new Label();
                proteaseName.Content = protease;
                proteaseName.FontSize = 12;

                Rectangle proteaseColor = new Rectangle();
                proteaseColor.Fill = new SolidColorBrush(proteaseByColor[protease]);
                proteaseColor.Width = 20;
                proteaseColor.Height = 10;
                if (proteaseCount == 1)
                {
                    j++;
                    legend.Children.Add(proteaseColor);
                    Grid.SetRow(proteaseColor, j);
                    Grid.SetColumn(proteaseColor, 0);
                    legend.Children.Add(proteaseName);
                    Grid.SetRow(proteaseName, j);
                    Grid.SetColumn(proteaseName, 1);
                }
                if (proteaseCount == 2)
                {
                    legend.Children.Add(proteaseColor);
                    Grid.SetRow(proteaseColor, j);
                    Grid.SetColumn(proteaseColor, 2);
                    legend.Children.Add(proteaseName);
                    Grid.SetRow(proteaseName, j);
                    Grid.SetColumn(proteaseName, 3);
                }
                if (proteaseCount == 3)
                {
                    legend.Children.Add(proteaseColor);
                    Grid.SetRow(proteaseColor, j);
                    Grid.SetColumn(proteaseColor, 4);
                    legend.Children.Add(proteaseName);
                    Grid.SetRow(proteaseName, j);
                    Grid.SetColumn(proteaseName, 5);
                    proteaseCount = 0;
                }

            }

            cav.Visibility = Visibility.Visible;
        }

        public static void drawLegendMods(Canvas cav, Dictionary<string, Color> proteaseByColor, Dictionary<string, SolidColorBrush> modsByColor, List<string> proteases, Grid legend, bool variants)
        {
            int i = -1;
            legend.RowDefinitions.Add(new RowDefinition());
            Label legendLabel = new Label();
            legendLabel.Content = "Key: ";
            legend.Children.Add(legendLabel);
            Grid.SetRow(legendLabel, 0);
            legend.RowDefinitions.Add(new RowDefinition());
            int proteaseRows = Convert.ToInt32(Math.Ceiling((proteases.Count() / 2.0)));
            int j = 0;
            while (j < proteaseRows)
            {
                legend.RowDefinitions.Add(new RowDefinition());
                j++;
            }
            legend.RowDefinitions.Add(new RowDefinition());

            legend.ColumnDefinitions.Add(new ColumnDefinition());
            legend.ColumnDefinitions.Add(new ColumnDefinition());
            legend.ColumnDefinitions.Add(new ColumnDefinition());
            legend.ColumnDefinitions.Add(new ColumnDefinition());
            legend.ColumnDefinitions.Add(new ColumnDefinition());
            legend.ColumnDefinitions.Add(new ColumnDefinition());

            // Updated to include three categories
            string[] peptideCategories = new string[3]
            {
                "Shared peptides (translucent)",
                "Unique peptides (bold)",
                "Not covered (underlined)"
            };

            foreach (string peptide in peptideCategories)
            {
                if (peptide.Equals("Not covered (underlined)"))
                {
                    // Draw underlined text example for uncovered residues
                    TextBlock uncoveredExample = new TextBlock();
                    uncoveredExample.Text = "ABC";
                    uncoveredExample.FontSize = 12;
                    uncoveredExample.FontWeight = FontWeights.Normal;
                    uncoveredExample.TextDecorations = TextDecorations.Underline;
                    uncoveredExample.HorizontalAlignment = HorizontalAlignment.Center;
                    uncoveredExample.VerticalAlignment = VerticalAlignment.Center;

                    Label pepLabel = new Label();
                    pepLabel.Content = peptide;
                    pepLabel.FontSize = 12;

                    legend.Children.Add(uncoveredExample);
                    legend.Children.Add(pepLabel);
                    Grid.SetColumn(uncoveredExample, ++i);
                    Grid.SetRow(uncoveredExample, 1);
                    Grid.SetColumn(pepLabel, ++i);
                    Grid.SetRow(pepLabel, 1);
                }
                else
                {
                    Line pepLine = new Line();
                    pepLine.X1 = 0;
                    pepLine.X2 = 50;
                    pepLine.Y1 = 0;
                    pepLine.Y2 = 0;
                    pepLine.StrokeThickness = 1;
                    pepLine.Stroke = new SolidColorBrush(Colors.Black);
                    if (peptide.Equals("Shared peptides (translucent)"))
                    {
                        pepLine.Stroke.Opacity = 0.35;
                    }
                    pepLine.HorizontalAlignment = HorizontalAlignment.Center;
                    pepLine.VerticalAlignment = VerticalAlignment.Center;

                    Label pepLabel = new Label();
                    pepLabel.Content = peptide;
                    pepLabel.FontSize = 12;

                    legend.Children.Add(pepLine);
                    legend.Children.Add(pepLabel);
                    Grid.SetColumn(pepLine, ++i);
                    Grid.SetRow(pepLine, 1);
                    Grid.SetColumn(pepLabel, ++i);
                    Grid.SetRow(pepLabel, 1);
                }
            }

            if (variants == true)
            {
                legend.ColumnDefinitions.Add(new ColumnDefinition());
                Label variantLabel = new Label();
                variantLabel.FontSize = 12;
                variantLabel.Content = "Sequence Variant";
                variantLabel.Foreground = Brushes.Red;
                variantLabel.FontWeight = FontWeights.ExtraBold;
                legend.Children.Add(variantLabel);
                Grid.SetColumn(variantLabel, ++i);
                Grid.SetRow(variantLabel, 1);
            }

            i = -1;

            j = 1;
            int proteaseCount = 0;
            foreach (var protease in proteases)
            {
                proteaseCount++;
                legend.ColumnDefinitions.Add(new ColumnDefinition());
                legend.ColumnDefinitions.Add(new ColumnDefinition());
                Label proteaseName = new Label();
                proteaseName.Content = protease;
                proteaseName.FontSize = 12;

                Rectangle proteaseColor = new Rectangle();
                proteaseColor.Fill = new SolidColorBrush(proteaseByColor[protease]);
                proteaseColor.Width = 20;
                proteaseColor.Height = 10;
                if (proteaseCount == 1)
                {
                    j++;
                    legend.Children.Add(proteaseColor);
                    Grid.SetRow(proteaseColor, j);
                    Grid.SetColumn(proteaseColor, 0);
                    legend.Children.Add(proteaseName);
                    Grid.SetRow(proteaseName, j);
                    Grid.SetColumn(proteaseName, 1);
                }
                if (proteaseCount == 2)
                {
                    legend.Children.Add(proteaseColor);
                    Grid.SetRow(proteaseColor, j);
                    Grid.SetColumn(proteaseColor, 2);
                    legend.Children.Add(proteaseName);
                    Grid.SetRow(proteaseName, j);
                    Grid.SetColumn(proteaseName, 3);
                }
                if (proteaseCount == 3)
                {
                    legend.Children.Add(proteaseColor);
                    Grid.SetRow(proteaseColor, j);
                    Grid.SetColumn(proteaseColor, 4);
                    legend.Children.Add(proteaseName);
                    Grid.SetRow(proteaseName, j);
                    Grid.SetColumn(proteaseName, 5);
                    proteaseCount = 0;
                }

            }

            int modCount = 0;

            foreach (var mod in modsByColor)
            {
                modCount++;

                Ellipse circle = new Ellipse()
                {
                    Width = 17,
                    Height = 17,
                    StrokeThickness = 1,
                    Opacity = 0.85

                };
                circle.Fill = mod.Value;
                circle.Stroke = mod.Value;

                Label modName = new Label();
                modName.FontSize = 12;

                if (modCount == 1)
                {
                    j++;
                    legend.RowDefinitions.Add(new RowDefinition());
                    legend.Children.Add(circle);
                    Grid.SetRow(circle, j);
                    Grid.SetColumn(circle, 0);

                    modName.Content = mod.Key;
                    legend.Children.Add(modName);
                    Grid.SetRow(modName, j);
                    Grid.SetColumn(modName, 1);
                }

                if (modCount == 2)
                {
                    legend.Children.Add(circle);
                    Grid.SetRow(circle, j);
                    Grid.SetColumn(circle, 2);

                    modName.Content = mod.Key;
                    legend.Children.Add(modName);
                    Grid.SetRow(modName, j);
                    Grid.SetColumn(modName, 3);
                }
                if (modCount == 3)
                {
                    legend.Children.Add(circle);
                    Grid.SetRow(circle, j);
                    Grid.SetColumn(circle, 4);

                    modName.Content = mod.Key;
                    legend.Children.Add(modName);
                    Grid.SetRow(modName, j);
                    Grid.SetColumn(modName, 5);
                    modCount = 0;
                }

            }

            cav.Visibility = Visibility.Visible;
        }

        // ── Stable Color Map ──────────────────────────────────────────────────

        public static (Dictionary<string, Color> colors, Dictionary<string, SolidColorBrush> brushes)
            BuildStableColorMaps()
        {
            var allNames = ProteaseDictionary.Dictionary.Keys.ToList();
            var rgbMap = CoverageMapConfiguration.CreateProteaseColorMap(allNames);

            var colors = new Dictionary<string, Color>();
            var brushes = new Dictionary<string, SolidColorBrush>();

            foreach (var kvp in rgbMap)
            {
                var wpfColor = Color.FromRgb(kvp.Value.R, kvp.Value.G, kvp.Value.B);
                var brush = new SolidColorBrush(wpfColor);
                brush.Freeze();
                colors[kvp.Key] = wpfColor;
                brushes[kvp.Key] = brush;
            }
            return (colors, brushes);
        }

        public static SolidColorBrush GetProteaseBrush(
            Dictionary<string, SolidColorBrush> brushes, string proteaseName)
        {
            if (brushes.TryGetValue(proteaseName, out var brush))
                return brush;
            var fb = new SolidColorBrush(Colors.DimGray);
            fb.Freeze();
            return fb;
        }

        public static int GetStableColorIndex(string proteaseName)
        {
            int i = 0;
            foreach (var key in ProteaseDictionary.Dictionary.Keys)
            {
                if (key == proteaseName) return i;
                i++;
            }
            return int.MaxValue;
        }

        // ── Lane View Drawing ─────────────────────────────────────────────────

        private static void DrawBarEndCap(Canvas canvas, double x, double laneY,
            SolidColorBrush brush, bool isUnique, int barHeight)
        {
            var cap = new Line
            {
                X1 = x,
                Y1 = laneY - 3,
                X2 = x,
                Y2 = laneY + barHeight + 3,
                Stroke = brush,
                StrokeThickness = 2,
                Opacity = isUnique ? 1.0 : 0.35
            };
            Panel.SetZIndex(cap, 2);
            canvas.Children.Add(cap);
        }

        public static void DrawLaneViewMap(
            Canvas mapCanvas,
            Canvas legendCanvas,
            Grid legendGrid,
            string accession,
            string? fullName,
            string baseSequence,
            List<string> orderedProteases,
            Dictionary<string, List<(int Start, int End)>> intervalsByProtease,
            Func<string, SolidColorBrush> getProteaseBrush,
            double canvasWidth,
            string? coverageHeader = null,
            int residueSpacing = 25,
            int seqLeftOffset = 45)
        {
            mapCanvas.Children.Clear();
            legendCanvas.Children.Clear();
            legendCanvas.Children.Add(legendGrid);
            legendGrid.Children.Clear();
            legendGrid.RowDefinitions.Clear();
            legendGrid.ColumnDefinitions.Clear();
            mapCanvas.Width = canvasWidth;
            mapCanvas.HorizontalAlignment = HorizontalAlignment.Center;
            legendCanvas.Width = canvasWidth;
            legendCanvas.HorizontalAlignment = HorizontalAlignment.Center;

            var splitSeq = CoverageMapDataPreparer.SplitSequenceIntoLines(
                baseSequence, CoverageMapDataPreparer.DefaultResiduesPerLine);

            int height = 10;
            txtDrawing(mapCanvas, new Point(0, height), accession, Brushes.Black);
            height += 20;

            if (!string.IsNullOrWhiteSpace(fullName))
            {
                txtDrawing(mapCanvas, new Point(0, height), fullName, Brushes.Black);
                height += 30;
            }

            if (!string.IsNullOrWhiteSpace(coverageHeader))
            {
                txtDrawing(mapCanvas, new Point(0, height), coverageHeader, Brushes.Black);
                height += 30;
            }

            int laneCount = orderedProteases.Count;
            int barZoneH = laneCount > 0
                ? BarTopMargin + laneCount * (BarHeight + BarRowGap)
                : 0;
            int lineStride = SeqTextHeight + barZoneH + BottomLineGap;

            for (int lineIndex = 0; lineIndex < splitSeq.Count; lineIndex++)
            {
                var line = splitSeq[lineIndex];
                int lineStartRes = lineIndex * CoverageMapDataPreparer.DefaultResiduesPerLine + 1;
                int lineEndRes = lineStartRes + line.Length - 1;

                txtDrawingLabel(mapCanvas, new Point(0, height), lineStartRes.ToString(), Brushes.Black);

                for (int r = 0; r < line.Length; r++)
                {
                    string ch = line[r].ToString().ToUpper();
                    txtDrawing(mapCanvas, new Point(r * residueSpacing + seqLeftOffset, height), ch, Brushes.Black);
                }

                int barBaseY = height + SeqTextHeight + BarTopMargin;

                for (int pi = 0; pi < orderedProteases.Count; pi++)
                {
                    string proteaseName = orderedProteases[pi];
                    var brush = getProteaseBrush(proteaseName);
                    int laneY = barBaseY + pi * (BarHeight + BarRowGap);

                    if (!intervalsByProtease.TryGetValue(proteaseName, out var intervals)) continue;

                    foreach (var (pepStart, pepEnd) in intervals)
                    {
                        if (pepEnd < lineStartRes || pepStart > lineEndRes) continue;

                        int visStart = Math.Max(pepStart, lineStartRes);
                        int visEnd = Math.Min(pepEnd, lineEndRes);
                        int colStart = visStart - lineStartRes;
                        int colEnd = visEnd - lineStartRes;

                        double x1 = colStart * residueSpacing + seqLeftOffset;
                        double x2 = colEnd * residueSpacing + seqLeftOffset + (residueSpacing - 4);

                        var bar = new Rectangle
                        {
                            Fill = brush,
                            Width = Math.Max(x2 - x1, 2),
                            Height = BarHeight,
                            RadiusX = 2,
                            RadiusY = 2
                        };
                        Canvas.SetLeft(bar, x1);
                        Canvas.SetTop(bar, laneY);
                        Panel.SetZIndex(bar, 1);
                        mapCanvas.Children.Add(bar);

                        if (pepStart >= lineStartRes)
                            DrawBarEndCap(mapCanvas, x1, laneY, brush, true, BarHeight);
                        if (pepEnd <= lineEndRes)
                            DrawBarEndCap(mapCanvas, x2, laneY, brush, true, BarHeight);
                    }
                }

                height += lineStride;
            }

            mapCanvas.Height = height + 20;

            DrawLaneViewLegend(legendCanvas, legendGrid, orderedProteases, getProteaseBrush, seqLeftOffset);
        }

        public static void DrawLaneViewLegend(
            Canvas legendCanvas, Grid legendGrid,
            List<string> proteases,
            Func<string, SolidColorBrush> getProteaseBrush,
            int seqLeftOffset = 45)
        {
            legendCanvas.Children.Clear();
            legendCanvas.Children.Add(legendGrid);
            legendGrid.Children.Clear();
            legendGrid.RowDefinitions.Clear();
            legendGrid.ColumnDefinitions.Clear();

            if (proteases.Count == 0) return;

            const double swatchW = 28;
            const double swatchH = 12;
            const double entryH = 20;
            const double startY = 4;
            const double colWidth = 190;
            const int cols = 3;
            int usedCols = Math.Min(cols, proteases.Count);
            double contentWidth = usedCols * colWidth;
            double startX = Math.Max((legendCanvas.Width - contentWidth) / 2.0, 0);

            for (int i = 0; i < proteases.Count; i++)
            {
                string name = proteases[i];
                var brush = getProteaseBrush(name);
                int col = i % cols;
                int row = i / cols;

                double entryX = startX + col * colWidth;
                double entryY = startY + row * entryH;

                var swatch = new Rectangle
                {
                    Fill = brush,
                    Width = swatchW,
                    Height = swatchH,
                    RadiusX = 2,
                    RadiusY = 2
                };
                Canvas.SetLeft(swatch, entryX);
                Canvas.SetTop(swatch, entryY + (entryH - swatchH) / 2.0);
                legendCanvas.Children.Add(swatch);

                var tb = new TextBlock
                {
                    Text = name,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.Black
                };
                Canvas.SetLeft(tb, entryX + swatchW + 4);
                Canvas.SetTop(tb, entryY + 3);
                legendCanvas.Children.Add(tb);
            }

            int rows = (int)Math.Ceiling(proteases.Count / (double)cols);
            legendCanvas.Height = startY + rows * entryH + 8;

            // Optionally show unique/shared opacity key
            /*
            double keyY = startY + rows * entryH + 6;

            var solidSwatch = new Rectangle
            {
                Fill = Brushes.Gray,
                Width = swatchW,
                Height = swatchH,
                RadiusX = 2,
                RadiusY = 2
            };
            Canvas.SetLeft(solidSwatch, startX);
            Canvas.SetTop(solidSwatch, keyY + (entryH - swatchH) / 2.0);
            legendCanvas.Children.Add(solidSwatch);

            var uniqueLabel = new TextBlock
            {
                Text = "Unique peptide",
                FontSize = 11,
                Foreground = Brushes.Black
            };
            Canvas.SetLeft(uniqueLabel, startX + swatchW + 4);
            Canvas.SetTop(uniqueLabel, keyY + 3);
            legendCanvas.Children.Add(uniqueLabel);

            var sharedSwatch = new Rectangle
            {
                Fill = new SolidColorBrush(Colors.Gray) { Opacity = 0.35 },
                Width = swatchW,
                Height = swatchH,
                RadiusX = 2,
                RadiusY = 2
            };
            Canvas.SetLeft(sharedSwatch, startX + colWidth);
            Canvas.SetTop(sharedSwatch, keyY + (entryH - swatchH) / 2.0);
            legendCanvas.Children.Add(sharedSwatch);

            var sharedLabel = new TextBlock
            {
                Text = "Shared peptide (translucent)",
                FontSize = 11,
                Foreground = Brushes.Black
            };
            Canvas.SetLeft(sharedLabel, startX + colWidth + swatchW + 4);
            Canvas.SetTop(sharedLabel, keyY + 3);
            legendCanvas.Children.Add(sharedLabel);

            legendCanvas.Height = keyY + entryH + 8;
            */
        }

        // ── Peptide-Per-Bar Drawing ───────────────────────────────────────────

        public static (HashSet<int> uniqueCovered, HashSet<int> sharedOnlyCovered)
            CalculateCoveredResiduesByType(List<InSilicoPep> peptides, bool isMultiDatabase)
        {
            var uniqueCovered = new HashSet<int>();
            var sharedCovered = new HashSet<int>();

            foreach (var peptide in peptides)
            {
                bool isUnique = isMultiDatabase ? peptide.UniqueAllDbs : peptide.Unique;
                for (int i = peptide.StartResidue; i <= peptide.EndResidue; i++)
                {
                    if (isUnique) uniqueCovered.Add(i);
                    else sharedCovered.Add(i);
                }
            }

            var sharedOnlyCovered = new HashSet<int>(sharedCovered.Except(uniqueCovered));
            return (uniqueCovered, sharedOnlyCovered);
        }

        private static void DrawSequenceCharacters(Canvas mapCanvas, string line,
            int height, int spacing, HashSet<int> uniqueCovered, HashSet<int> sharedOnlyCovered,
            int lineStartResidue, int seqLeftOffset)
        {
            for (int r = 0; r < line.Length; r++)
            {
                int residuePosition = lineStartResidue + r;
                bool isCoveredByUnique = uniqueCovered.Contains(residuePosition);
                bool isCoveredBySharedOnly = sharedOnlyCovered.Contains(residuePosition);

                string character = line[r].ToString().ToUpper();

                if (isCoveredByUnique)
                    txtDrawing(mapCanvas, new Point(r * spacing + seqLeftOffset, height), character, Brushes.Black);
                else if (isCoveredBySharedOnly)
                    txtDrawingShared(mapCanvas, new Point(r * spacing + seqLeftOffset, height), character, Brushes.Black);
                else
                    txtDrawingUncovered(mapCanvas, new Point(r * spacing + seqLeftOffset, height), character, Brushes.Black);
            }
        }

        private readonly struct PeptideDrawEntry
        {
            public PeptideDrawEntry(int startResidue, int endResidue, string protease, bool isUnique)
            {
                StartResidue = startResidue;
                EndResidue = endResidue;
                Protease = protease;
                IsUnique = isUnique;
            }

            public int StartResidue { get; }
            public int EndResidue { get; }
            public string Protease { get; }
            public bool IsUnique { get; }
        }

        public static void DrawPeptidePerBarMap(
            Canvas mapCanvas,
            Canvas legendCanvas,
            Grid legendGrid,
            string accession,
            string baseSequence,
            List<string> proteases,
            Dictionary<string, Color> proteaseByColor,
            List<InSilicoPep> peptides,
            HashSet<int> uniqueCovered,
            HashSet<int> sharedOnlyCovered,
            bool isMultiDatabase,
            double canvasWidth,
            string? fullName = null,
            string? coverageHeader = null,
            int residueSpacing = 25,
            int seqLeftOffset = 45)
        {
            var entries = peptides
                .Select(p => new PeptideDrawEntry(
                    p.StartResidue,
                    p.EndResidue,
                    p.Protease,
                    isMultiDatabase ? p.UniqueAllDbs : p.Unique))
                .ToList();

            DrawPeptidePerBarCore(
                mapCanvas,
                legendCanvas,
                legendGrid,
                accession,
                baseSequence,
                proteases,
                proteaseByColor,
                entries,
                uniqueCovered,
                sharedOnlyCovered,
                canvasWidth,
                fullName,
                coverageHeader,
                residueSpacing,
                seqLeftOffset);
        }

        public static void DrawPeptidePerBarIntervalMap(
            Canvas mapCanvas,
            Canvas legendCanvas,
            Grid legendGrid,
            string accession,
            string baseSequence,
            List<string> proteases,
            Dictionary<string, Color> proteaseByColor,
            List<(int Start, int End, string Protease)> intervals,
            HashSet<int> uniqueCovered,
            HashSet<int> sharedOnlyCovered,
            double canvasWidth,
            string? fullName = null,
            string? coverageHeader = null,
            int residueSpacing = 25,
            int seqLeftOffset = 45)
        {
            var entries = intervals
                .Select(i => new PeptideDrawEntry(i.Start, i.End, i.Protease, true))
                .ToList();

            DrawPeptidePerBarCore(
                mapCanvas,
                legendCanvas,
                legendGrid,
                accession,
                baseSequence,
                proteases,
                proteaseByColor,
                entries,
                uniqueCovered,
                sharedOnlyCovered,
                canvasWidth,
                fullName,
                coverageHeader,
                residueSpacing,
                seqLeftOffset);
        }

        private static void DrawPeptidePerBarCore(
            Canvas mapCanvas,
            Canvas legendCanvas,
            Grid legendGrid,
            string accession,
            string baseSequence,
            List<string> proteases,
            Dictionary<string, Color> proteaseByColor,
            List<PeptideDrawEntry> entries,
            HashSet<int> uniqueCovered,
            HashSet<int> sharedOnlyCovered,
            double canvasWidth,
            string? fullName,
            string? coverageHeader,
            int residueSpacing,
            int seqLeftOffset)
        {
            mapCanvas.Children.Clear();
            if (legendCanvas.Children.Count == 0 || !legendCanvas.Children.Contains(legendGrid))
            {
                legendCanvas.Children.Clear();
                legendCanvas.Children.Add(legendGrid);
            }
            legendGrid.Children.Clear();
            legendGrid.RowDefinitions.Clear();
            legendGrid.ColumnDefinitions.Clear();
            mapCanvas.Width = canvasWidth;
            mapCanvas.HorizontalAlignment = HorizontalAlignment.Center;
            legendCanvas.Width = canvasWidth;
            legendCanvas.HorizontalAlignment = HorizontalAlignment.Center;
            legendGrid.HorizontalAlignment = HorizontalAlignment.Center;

            var splitSeq = CoverageMapDataPreparer.SplitSequenceIntoLines(
                baseSequence, CoverageMapDataPreparer.DefaultResiduesPerLine);

            int height = 10;
            var indices = new Dictionary<int, List<int>>();
            int accumIndex = 0;
            var partialPeptideMatches = new Dictionary<PeptideDrawEntry, (int, int)>();

            if (!string.IsNullOrWhiteSpace(fullName) || !string.IsNullOrWhiteSpace(coverageHeader))
            {
                txtDrawing(mapCanvas, new Point(0, height), accession, Brushes.Black);
                height += 20;

                if (!string.IsNullOrWhiteSpace(fullName))
                {
                    txtDrawing(mapCanvas, new Point(0, height), fullName, Brushes.Black);
                    height += 30;
                }

                if (!string.IsNullOrWhiteSpace(coverageHeader))
                {
                    txtDrawing(mapCanvas, new Point(0, height), coverageHeader, Brushes.Black);
                    height += 30;
                }
            }
            else
            {
                txtDrawing(mapCanvas, new Point(0, height),
                    $"Sequence Coverage Map of {accession}:", Brushes.Black);
                height += 30;
            }

            for (int lineIndex = 0; lineIndex < splitSeq.Count; lineIndex++)
            {
                var line = splitSeq[lineIndex];
                indices.Clear();
                var lineLabel = (lineIndex * CoverageMapDataPreparer.DefaultResiduesPerLine) + 1;

                txtDrawingLabel(mapCanvas, new Point(0, height), lineLabel.ToString(), Brushes.Black);

                int lineStartResidue = lineIndex * CoverageMapDataPreparer.DefaultResiduesPerLine + 1;
                DrawSequenceCharacters(mapCanvas, line, height, residueSpacing, uniqueCovered, sharedOnlyCovered, lineStartResidue, seqLeftOffset);

                // Process partial peptides
                if (partialPeptideMatches.Count > 0)
                {
                    var temp = new Dictionary<PeptideDrawEntry, (int, int)>(partialPeptideMatches);
                    partialPeptideMatches.Clear();

                    foreach (var peptide in temp)
                    {
                        var remaining = peptide.Value.Item1;
                        var highlightIndex = peptide.Value.Item2;

                        int start = 0;
                        int end = Math.Min(remaining, line.Length - 1);
                        var partialIndex = CoverageMapDataPreparer.CheckPartialMatch(peptide.Key.EndResidue, line.Length, accumIndex);
                        bool isUnique = peptide.Key.IsUnique;

                        if (partialIndex >= 0)
                        {
                            Highlight(start, end, mapCanvas, indices, height,
                                proteaseByColor[peptide.Key.Protease], isUnique, false, false, highlightIndex,
                                residueSpacing, seqLeftOffset);
                            partialPeptideMatches.Add(peptide.Key, (partialIndex, highlightIndex));
                        }
                        else
                        {
                            Highlight(start, end, mapCanvas, indices, height,
                                proteaseByColor[peptide.Key.Protease], isUnique, false, true, highlightIndex,
                                residueSpacing, seqLeftOffset);
                        }
                    }
                }

                // Draw peptide highlights for this line
                var peptidesOnThisLine = entries
                    .Where(p => p.StartResidue - accumIndex - 1 < line.Length)
                    .OrderBy(p => p.StartResidue)
                    .ToList();

                foreach (var peptide in peptidesOnThisLine)
                {
                    var partialIndex = CoverageMapDataPreparer.CheckPartialMatch(peptide.EndResidue, line.Length, accumIndex);
                    int start = peptide.StartResidue - accumIndex - 1;
                    int end = Math.Min(peptide.EndResidue - accumIndex - 1, line.Length - 1);
                    bool isUnique = peptide.IsUnique;

                    if (partialIndex >= 0)
                    {
                        var highlightIndex = Highlight(start, end, mapCanvas, indices, height,
                            proteaseByColor[peptide.Protease], isUnique, true, false, -1,
                            residueSpacing, seqLeftOffset);
                        if (!partialPeptideMatches.ContainsKey(peptide))
                            partialPeptideMatches.Add(peptide, (partialIndex, highlightIndex));
                    }
                    else
                    {
                        Highlight(start, end, mapCanvas, indices, height,
                            proteaseByColor[peptide.Protease], isUnique, true, true, -1,
                            residueSpacing, seqLeftOffset);
                    }
                    entries.Remove(peptide);
                }

                int addedSpace = indices.Count > 7 ? (indices.Count - 7) * 10 : 0;
                height += 100 + addedSpace;
                accumIndex += line.Length;
            }

            mapCanvas.Height = height + 20;

            drawLegend(legendCanvas, proteaseByColor, proteases, legendGrid, false);

            legendGrid.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var desired = legendGrid.DesiredSize;
            legendCanvas.Height = desired.Height;
            if (legendCanvas.Width <= 0)
            {
                legendCanvas.Width = desired.Width;
            }

            Canvas.SetTop(legendGrid, 0);
            Canvas.SetLeft(legendGrid, Math.Max((legendCanvas.Width - desired.Width) / 2.0, 0));
        }
    }



    class ProteinForSeqCoverage
    {
        public ProteinForSeqCoverage(string accession, string map, double fraction)
        {
            Accession = accession;
            Map = map;
            Fraction = fraction;
        }

        public string Accession { get; set; }
        public string Map { get; set; }
        public double Fraction { get; set; }
    }
}
