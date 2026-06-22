using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GUI
{
    /// <summary>
    /// This text box requires input text to be integer only.
    /// </summary>
    public class IntegerTextBoxControl : TextBox
    {
        public static readonly DependencyProperty AllowNegativeProperty =
            DependencyProperty.Register(
                nameof(AllowNegative),
                typeof(bool),
                typeof(IntegerTextBoxControl),
                new PropertyMetadata(false));

        public bool AllowNegative
        {
            get => (bool)GetValue(AllowNegativeProperty);
            set => SetValue(AllowNegativeProperty, value);
        }

        // Optional inclusive value bounds. Null (the default) means unbounded, so existing
        // text boxes that don't set these keep their current behavior.
        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register(
                nameof(Minimum),
                typeof(int?),
                typeof(IntegerTextBoxControl),
                new PropertyMetadata(null));

        public int? Minimum
        {
            get => (int?)GetValue(MinimumProperty);
            set => SetValue(MinimumProperty, value);
        }

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register(
                nameof(Maximum),
                typeof(int?),
                typeof(IntegerTextBoxControl),
                new PropertyMetadata(null));

        public int? Maximum
        {
            get => (int?)GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        public IntegerTextBoxControl()
        {
            HorizontalContentAlignment = HorizontalAlignment.Center;
            VerticalContentAlignment = VerticalAlignment.Center;
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

        /// <summary>
        /// Clamps the entered value into [Minimum, Maximum] (when set) once editing finishes.
        /// </summary>
        protected override void OnLostFocus(RoutedEventArgs e)
        {
            ClampToBounds();
            base.OnLostFocus(e);
        }

        private void ClampToBounds()
        {
            if ((Minimum is null && Maximum is null) || !int.TryParse(Text, out int value))
                return;

            int clamped = value;
            if (Minimum.HasValue && clamped < Minimum.Value) clamped = Minimum.Value;
            if (Maximum.HasValue && clamped > Maximum.Value) clamped = Maximum.Value;

            if (clamped != value)
            {
                Text = clamped.ToString();
                CaretIndex = Text.Length;
            }
        }
    }
}
