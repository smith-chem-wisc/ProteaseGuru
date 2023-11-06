using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;

namespace GUI
{
    public class MotifTextBoxControl : TextBox
    {
        protected override void OnPreviewTextInput(TextCompositionEventArgs e)
        {

            List<char> acceptableChars = new List<char>()
            {
                'A', 'R', 'N', 'D', 'C', 'E', 'Q', 'G', 'H', 'I', 'L', 'K', 'M', 'F', 'P', 'S', 'T', 'W', 'Y', 'V', ','
            };

            foreach (var character in e.Text)
            {
                if (acceptableChars.Contains(character))
                {
                    e.Handled = false;
                    return;
                }
            }

            e.Handled = true;
        }
    }
}
