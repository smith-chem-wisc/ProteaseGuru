using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GuiFunctions;

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
                new PropertyMetadata(int.MinValue, OnBoundChanged));

        public static readonly DependencyProperty UpperBoundProperty =
            DependencyProperty.Register(
                nameof(UpperBound),
                typeof(int),
                typeof(IntegerTextBoxControl),
                new PropertyMetadata(int.MaxValue, OnBoundChanged));

        /// <summary>
        /// Re-clamps when a bound changes. Without this the value is only validated on text change,
        /// so a bound applied after Text — the usual case when Text is bound and the bound is too —
        /// would never be enforced against the value already sitting in the box.
        /// </summary>
        private static void OnBoundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // Defer while the user is typing, matching OnTextChanged.
            if (d is IntegerTextBoxControl box && !box.IsKeyboardFocused)
                box.ClampToBounds();
        }

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
            base.OnLostKeyboardFocus(e);
            ClampToBounds();
        }

        private void ClampToBounds()
        {
            string clamped = IntegerTextBounds.Clamp(Text, LowerBound, UpperBound);
            if (clamped == Text)
                return;

            Text = clamped;

            // Push the clamped value to the binding instead of waiting for focus to move, so the
            // source cannot keep the out-of-range value the control just rejected.
            GetBindingExpression(TextProperty)?.UpdateSource();
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
