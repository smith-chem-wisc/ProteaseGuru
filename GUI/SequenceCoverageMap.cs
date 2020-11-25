using SharpLearning.DecisionTrees.Nodes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Tasks;

namespace ProteaseGuruGUI
{
    class SequenceCoverageMap
    {
        private const int spacing = 22;

        public static int Highlight(int start, int end, Canvas map, Dictionary<int, List<int>> indices,
            int height, Color clr, bool unique, bool startPep, bool endPep,int partial = -1)
        {
            int increment = 0;
            int i;

            if (partial >= 0) // if partial peptide 
            {
                increment = partial * 10;
                i = partial;            }
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
                peptideLineDrawing(map, new Point(start * spacing + 10, height + increment), 
                    new Point(end * spacing + 10, height + increment), clr, false, startPep, endPep);
            }
            else
            {
                peptideLineDrawing(map, new Point(start * spacing + 10, height + increment), 
                    new Point(end * spacing + 10, height + increment), clr, true, startPep, endPep);
            }

            return i;
        }
        
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
            tb.FontFamily = new FontFamily("Arial"); // monospaced font

            Canvas.SetTop(tb, loc.Y);
            Canvas.SetLeft(tb, loc.X);
            Panel.SetZIndex(tb, 2); //lower priority
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
                top.X1 = start.X-10;
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

            Canvas.SetZIndex(top, 1); //on top of any other things in canvas
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
            Canvas.SetLeft(circle, loc.X+3);
            Canvas.SetTop(circle, loc.Y-.75);
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
                Canvas.SetLeft(circle, loc.X + 3);
                Canvas.SetTop(circle, ((loc.Y - .75)-(circleCount*18)));
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
            int proteaseRows = Convert.ToInt32(Math.Ceiling((proteases.Count()/3.0)));
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
            string[] peptides = new string[2] { "Shared", "Unique" };
            foreach (string peptide in peptides)
            {
                Line pepLine = new Line();
                pepLine.X1 = 0;
                pepLine.X2 = 50;
                pepLine.Y1 = 0;
                pepLine.Y2 = 0;
                pepLine.StrokeThickness = 4;
                pepLine.Stroke = new SolidColorBrush(Colors.Black);
                if (peptide.Equals("Shared"))
                {
                    
                    pepLine.Stroke.Opacity = 0.35;                   
                }
                pepLine.HorizontalAlignment = HorizontalAlignment.Center;
                pepLine.VerticalAlignment = VerticalAlignment.Center;

                Label pepLabel = new Label();
                pepLabel.Content = peptide + " peptides";
                pepLabel.FontSize = 12;

                

                legend.Children.Add(pepLine);
                legend.Children.Add(pepLabel);
                Grid.SetColumn(pepLine, ++i);
                Grid.SetRow(pepLine, 1);
                Grid.SetColumn(pepLabel, ++i);
                Grid.SetRow(pepLabel, 1);
            }
            if (variants == true)
            {
                legend.ColumnDefinitions.Add(new ColumnDefinition());
                Label variantLabel = new Label();
                variantLabel.Content = "Sequence Variants";
                variantLabel.Foreground = Brushes.Tomato;
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
            string[] peptides = new string[2] { "Shared", "Unique" };
            foreach (string peptide in peptides)
            {
                Line pepLine = new Line();
                pepLine.X1 = 0;
                pepLine.X2 = 50;
                pepLine.Y1 = 0;
                pepLine.Y2 = 0;
                pepLine.StrokeThickness = 1;
                pepLine.Stroke = new SolidColorBrush(Colors.Black);
                if (peptide.Equals("Shared"))
                {                    
                    pepLine.Stroke.Opacity = 0.35;
                }
                pepLine.HorizontalAlignment = HorizontalAlignment.Center;
                pepLine.VerticalAlignment = VerticalAlignment.Center;

                Label pepLabel = new Label();
                pepLabel.Content = peptide + " peptides";
                pepLabel.FontSize= 12;

                legend.Children.Add(pepLine);
                legend.Children.Add(pepLabel);
                Grid.SetColumn(pepLine, ++i);
                Grid.SetRow(pepLine, 1);
                Grid.SetColumn(pepLabel, ++i);
                Grid.SetRow(pepLabel, 1);
            }

            if (variants == true)
            {
                legend.ColumnDefinitions.Add(new ColumnDefinition());
                Label variantLabel = new Label();
                variantLabel.FontSize = 12;
                variantLabel.Content = "Sequence Variants";
                variantLabel.Foreground = Brushes.Tomato;
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
