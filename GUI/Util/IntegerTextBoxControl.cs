using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GUI
{
    /// <summary>
    /// This text box requires input text to be integer only.
    /// Supports LowerBound and UpperBound with automatic clamping.
    /// </summary>
    public class IntegerTextBoxControl : TextBox
    {
        public IntegerTextBoxControl()
        {
            HorizontalContentAlignment = HorizontalAlignment.Center;
            VerticalContentAlignment = VerticalAlignment.Center;
        }

        public static readonly DependencyProperty AllowNegativeProperty =
            DependencyProperty.Register(
                nameof(AllowNegative),
                typeof(bool),
                typeof(IntegerTextBoxControl),
                new PropertyMetadata(false));

        public static readonly DependencyProperty LowerBoundProperty =
            DependencyProperty.Register(
                nameof(LowerBound),
                typeof(int),
                typeof(IntegerTextBoxControl),
                new PropertyMetadata(int.MinValue));

        public static readonly DependencyProperty UpperBoundProperty =
            DependencyProperty.Register(
                nameof(UpperBound),
                typeof(int),
                typeof(IntegerTextBoxControl),
                new PropertyMetadata(int.MaxValue));

        public bool AllowNegative
        {
            get => (bool)GetValue(AllowNegativeProperty);
            set => SetValue(AllowNegativeProperty, value);
        }

        public int LowerBound
        {
            get => (int)GetValue(LowerBoundProperty);
            set => SetValue(LowerBoundProperty, value);
        }

        public int UpperBound
        {
            get => (int)GetValue(UpperBoundProperty);
            set => SetValue(UpperBoundProperty, value);
        }

        /// <summary>
        /// Ensures only integers can be inputted into the text box
        /// </summary>
        /// <param name="e"></param>
        protected override void OnPreviewTextInput(TextCompositionEventArgs e)
        {
            foreach (var character in e.Text)
            {
                if (!char.IsDigit(character))
                {
                    if (character == '-' && AllowNegative)
                    {
                        // Allow '-' only at the start and only once
                        if (CaretIndex == 0 && !Text.Contains("-"))
                        {
                            continue;
                        }
                    }
                    e.Handled = true;
                    return;
                }
            }
            e.Handled = false;
        }

        protected override void OnTextChanged(TextChangedEventArgs e)
        {
            base.OnTextChanged(e);

            if (int.TryParse(Text, out int value))
            {
                if (value < LowerBound)
                    Text = LowerBound.ToString();
                else if (value > UpperBound)
                    Text = UpperBound.ToString();
            }
        }

        /// <summary>
        /// Cursor is removed from text box on pressing Return
        /// </summary>
        /// <param name="e"></param>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Return || e.Key == Key.Enter)
                Keyboard.ClearFocus();
        }
    }
}
