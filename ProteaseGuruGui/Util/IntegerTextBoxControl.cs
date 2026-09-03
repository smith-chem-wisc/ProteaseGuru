using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ProteaseGuru.GuiFunctions;

namespace ProteaseGuru.Gui
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
                new PropertyMetadata(int.MinValue, OnBoundChanged));

        public static readonly DependencyProperty UpperBoundProperty =
            DependencyProperty.Register(
                nameof(UpperBound),
                typeof(int),
                typeof(IntegerTextBoxControl),
                new PropertyMetadata(int.MaxValue, OnBoundChanged));

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
        /// Re-clamps the current text when either bound changes, so a value that was in range
        /// under the old limits doesn't survive unclamped when the limits shrink. The
        /// IsKeyboardFocused guard preserves partially-typed values while the user is editing.
        /// </summary>
        private static void OnBoundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is IntegerTextBoxControl control && !control.IsKeyboardFocused)
            {
                control.ClampToBounds();
                control.GetBindingExpression(TextProperty)?.UpdateSource();
            }
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
        /// Clamps value changes that do not originate from the user actively typing — i.e. initial XAML
        /// values, programmatic assignments, and binding-driven updates. While the control has keyboard
        /// focus, clamping is deferred to commit time (OnLostKeyboardFocus) so partially-typed values
        /// (e.g. "5" while typing "50" with LowerBound=10) are not rewritten mid-edit.
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
        /// (e.g. "5" while typing "50" with LowerBound=10) are not rewritten mid-edit.
        /// </summary>
        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            ClampToBounds();
            base.OnLostKeyboardFocus(e);
        }

        private void ClampToBounds()
        {
            string clamped = IntegerTextBounds.Clamp(Text, LowerBound, UpperBound);

            // Only assign if the value changed to avoid redundant TextChanged events
            // (e.g. when this is called from the bound-change callback).
            if (clamped != Text)
                Text = clamped;
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
