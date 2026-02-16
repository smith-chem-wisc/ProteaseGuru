using Omics.Digestion;
using Proteomics.AminoAcidPolymer;
using Proteomics.ProteolyticDigestion;
using Tasks;

namespace GuiFunctions;

public class ProteaseSpecificParametersViewModel(ProteaseSpecificParameters dig, DigestionConditionsSetupViewModel root) : BaseViewModel(root)
{
    private bool _isSelected;
    public ProteaseSpecificParameters ProteaseSpecificParams { get; } = dig;

    public string DigestionAgentName => ProteaseSpecificParams.DigestionParams.DigestionAgent.Name;
    public DigestionAgent DigestionAgent => ProteaseSpecificParams.DigestionParams.DigestionAgent;
    public string ToolTip => ProteaseSpecificParams.DigestionParams.DigestionAgent.Name + " -- Cleavage specificity:  " + string.Join(",", DigestionAgent.DigestionMotifs.Select(p => p).ToString());

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            OnPropertyChanged(nameof(IsSelected));
        }
    }

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
