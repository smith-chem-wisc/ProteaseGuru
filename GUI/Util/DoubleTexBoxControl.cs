using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GUI
{
    /// <summary>
    /// This text box requires input text to be decimal only.
    /// </summary>
    public class DoubleTextBoxControl : TextBox
    {
        public DoubleTextBoxControl()
        {
            HorizontalContentAlignment = HorizontalAlignment.Center;
            VerticalContentAlignment = VerticalAlignment.Center;
        }

        public static readonly DependencyProperty LowerBoundProperty =
            DependencyProperty.Register(
                nameof(LowerBound),
                typeof(double),
                typeof(DoubleTextBoxControl),
                new PropertyMetadata(double.MinValue));

        public static readonly DependencyProperty UpperBoundProperty =
            DependencyProperty.Register(
                nameof(UpperBound),
                typeof(double),
                typeof(DoubleTextBoxControl),
                new PropertyMetadata(double.MaxValue));

        public double LowerBound
        {
            get => (double)GetValue(LowerBoundProperty);
            set => SetValue(LowerBoundProperty, value);
        }

        public double UpperBound
        {
            get => (double)GetValue(UpperBoundProperty);
            set => SetValue(UpperBoundProperty, value);
        }

        protected override void OnPreviewTextInput(TextCompositionEventArgs e)
        {
            foreach (var character in e.Text)
            {
                if (!char.IsDigit(character) && !(character == '.'))
                {
                    e.Handled = true;
                    return;
                }

                if (((TextBox)e.Source).Text.Contains('.') && character == '.')
                {
                    e.Handled = true;
                    return;
                }
            }
            e.Handled = false;
        }

        /// <summary>
        /// Clamps value changes that do not originate from the user actively typing — i.e. initial XAML
        /// values, programmatic assignments, and binding-driven updates. While the control has keyboard
        /// focus, clamping is deferred to commit time (OnLostKeyboardFocus) so partially-typed values
        /// are not rewritten mid-edit.
        /// </summary>
        protected override void OnTextChanged(TextChangedEventArgs e)
        {
            base.OnTextChanged(e);
            if (!IsKeyboardFocused)
                ClampToBounds();
        }

        /// <summary>
        /// Clamps the committed value to [LowerBound, UpperBound] when the control loses focus.
        /// Clamping happens on commit rather than on every keystroke so that partially-typed values
        /// are not rewritten mid-edit.
        /// </summary>
        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnLostKeyboardFocus(e);
            ClampToBounds();
        }

        private void ClampToBounds()
        {
            if (double.TryParse(Text, out double value))
            {
                if (value < LowerBound)
                    Text = LowerBound.ToString();
                else if (value > UpperBound)
                    Text = UpperBound.ToString();
            }
        }

        /// <summary>
        /// Cursor is removed from text box on pressing Return (which triggers clamping via lost focus)
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
