using ProteaseGuru.Engine;
using Omics.Digestion;
using ProteaseGuru.Tasks;
using Transcriptomics.Digestion;

namespace ProteaseGuru.GuiFunctions;

public class ProteaseSpecificParametersViewModel : BaseViewModel
{
    private bool _isSelected;

    public ProteaseSpecificParametersViewModel(ProteaseSpecificParameters dig, DigestionConditionsSetupViewModel root) : base(root)
    {
        ProteaseSpecificParams = dig;

        // Subscribe to mode changes
        GuiGlobalParamsViewModel.Instance.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(GuiGlobalParamsViewModel.IsRnaMode))
            {
                OnPropertyChanged(nameof(IsVisible));
            }
        };
    }

    public ProteaseSpecificParameters ProteaseSpecificParams { get; }

    public bool IsRna => ProteaseSpecificParams.DigestionParams.DigestionAgent is Rnase;
    public string DigestionAgentName => ProteaseSpecificParams.DigestionParams.DigestionAgent.Name;
    public DigestionAgent DigestionAgent => ProteaseSpecificParams.DigestionParams.DigestionAgent;
    public string ToolTip => ProteaseSpecificParams.DigestionParams.DigestionAgent.Name + " -- Cleavage specificity:  " + DigestionAgent.DigestionMotifs.ToMotifString();

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            OnPropertyChanged(nameof(IsSelected));
        }
    }

    public bool IsVisible => GuiGlobalParamsViewModel.Instance.IsRnaMode == IsRna;

    public int MaxMissedCleavages
    {
        get => ProteaseSpecificParams.DigestionParams.MaxMissedCleavages;
        set
        {
            ProteaseSpecificParams.DigestionParams.MaxMissedCleavages = value;
            OnPropertyChanged(nameof(MaxMissedCleavages));
        }
    }

    public int MinLength
    {
        get => ProteaseSpecificParams.DigestionParams.MinLength;
        set
        {
            ProteaseSpecificParams.DigestionParams.MinLength = value;
            OnPropertyChanged(nameof(MinLength));
        }
    }

    public int MaxLength
    {
        get => ProteaseSpecificParams.DigestionParams.MaxLength;
        set
        {
            ProteaseSpecificParams.DigestionParams.MaxLength = value;
            OnPropertyChanged(nameof(MaxLength));
        }
    }
}
