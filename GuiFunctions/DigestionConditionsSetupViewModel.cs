using System.Collections.ObjectModel;
using System.Windows.Input;
using Engine;
using Omics.Modifications;
using Proteomics.ProteolyticDigestion;
using Tasks;

namespace GuiFunctions;

public class DigestionConditionsSetupViewModel : BaseViewModel
{
    public Modification OxidativeMethionine { get; init; }
    public Modification Carbamidomethylation { get; init; }

    public ObservableCollection<ProteaseSpecificParametersViewModel> ProteaseSpecificParameters { get; } = new();

    private readonly Parameters _parameters;
    public Parameters Parameters
    {
        get
        {
            _parameters.ProteaseSpecificParameters.Clear();
            foreach (var specificParams in ProteaseSpecificParameters)
                if (specificParams.IsSelected)
                    _parameters.ProteaseSpecificParameters.Add(specificParams.ProteaseSpecificParams);
            return _parameters;
        }
    }

    public DigestionConditionsSetupViewModel(Parameters? parameters)
    {
        _parameters = parameters ?? new Parameters();
        OxidativeMethionine = GlobalVariables.AllModsKnown.First(p => p.IdWithMotif == "Oxidation on M");
        Carbamidomethylation = GlobalVariables.AllModsKnown.First(p => p.IdWithMotif == "Carbamidomethyl on C");

        PopulateProteaseCollection();

        SetDefaultProteasesCommand = new RelayCommand(SetDefaultProteases);
        ClearProteasesCommand = new RelayCommand(ClearProteases);
        ResetDigestionConditionsCommand = new RelayCommand(ResetDigestionConditions);
    }

    #region Properties to Set AllSpecificParameters

    private int _maxMissedCleavages = 2;
    private int _minLength = 7;
    private int _maxLength = 50;
    private bool _treatModifiedPeptidesAsDifferent = false;
    private bool _applyFixedCarbamidomethylation = false;
    private bool _applyVariableOxidation = false;


    public int MaxMissedCleavages
    {
        get => _maxMissedCleavages;
        set
        {
            _maxMissedCleavages = value;
            foreach (var proteaseSpecific in ProteaseSpecificParameters)
                proteaseSpecific.MaxMissedCleavages = value;
            OnPropertyChanged(nameof(MaxMissedCleavages));
        }
    }

    public int MinLength
    {
        get => _minLength;
        set
        {
            _minLength = value;
            foreach (var proteaseSpecific in ProteaseSpecificParameters)
                proteaseSpecific.MinLength = value;
            OnPropertyChanged(nameof(MinLength));
        }
    }

    public int MaxLength
    {
        get => _maxLength;
        set
        {
            _maxLength = value;
            foreach (var proteaseSpecific in ProteaseSpecificParameters)
                proteaseSpecific.MaxLength = value;
            OnPropertyChanged(nameof(MaxLength));
        }
    }

    public int MinPeptideMass
    {
        get => Parameters.MinPeptideMassAllowed;
        set
        {
            Parameters.MinPeptideMassAllowed = value;
            OnPropertyChanged(nameof(MinPeptideMass));
        }
    }

    public int MaxPeptideMass
    {
        get => Parameters.MaxPeptideMassAllowed;
        set
        {
            Parameters.MaxPeptideMassAllowed = value;
            OnPropertyChanged(nameof(MaxPeptideMass));
        }
    }

    public bool TreatModifiedPeptidesAsDifferent
    {
        get => _treatModifiedPeptidesAsDifferent;
        set
        {
            _treatModifiedPeptidesAsDifferent = value;
            Parameters.TreatModifiedPeptidesAsDifferent = value;
            OnPropertyChanged(nameof(TreatModifiedPeptidesAsDifferent));
        }
    }

    public bool ApplyFixedCarbamidomethylation
    {
        get => _applyFixedCarbamidomethylation;
        set
        {
            _applyFixedCarbamidomethylation = value;
            foreach (var specificParams in ProteaseSpecificParameters.Where(p => !p.ProteaseSpecificParams.FixedMods.Contains(Carbamidomethylation)))
            {
                specificParams.ProteaseSpecificParams.FixedMods.Add(Carbamidomethylation);
            }

            OnPropertyChanged(nameof(ApplyFixedCarbamidomethylation));
        }
    }

    public bool ApplyVariableOxidation
    {
        get => _applyVariableOxidation;
        set
        {
            _applyVariableOxidation = value;
            foreach (var specificParams in ProteaseSpecificParameters.Where(p => !p.ProteaseSpecificParams.VariableMods.Contains(OxidativeMethionine)))
            {
                specificParams.ProteaseSpecificParams.VariableMods.Add(OxidativeMethionine);
            }
            OnPropertyChanged(nameof(ApplyVariableOxidation));
        }
    }

    #endregion

    #region Commands

    public ICommand SetDefaultProteasesCommand { get; }
    public ICommand ClearProteasesCommand { get; }
    public ICommand ResetDigestionConditionsCommand { get; }

    private string[] _defaultProteases = ["Arg-C", "Arg-N", "chymotrypsin (don't cleave before proline)", "Glu-C", "Glu-C (with asp)", "Lys-N"];

    private void SetDefaultProteases()
    {
        // Select the 6 most commonly used proteases (indices 0, 1, 2, 6, 7, 10 from old code)
        foreach (var specificParametersViewModel in ProteaseSpecificParameters)
        {
            specificParametersViewModel.IsSelected = _defaultProteases.Contains(specificParametersViewModel.DigestionAgentName);
        }
    }

    private void ClearProteases()
    {
        // Deselect all proteases
        foreach (var protease in ProteaseSpecificParameters)
        {
            protease.IsSelected = false;
        }
    }

    private void ResetDigestionConditions()
    {
        // Reset all parameters to defaults
        MaxMissedCleavages = 2;
        MinLength = 7;
        MaxLength = 50;
        MinPeptideMass = -1;
        MaxPeptideMass = -1;
        TreatModifiedPeptidesAsDifferent = false;
        ApplyFixedCarbamidomethylation = false;
        ApplyVariableOxidation = false;

        foreach (var specificParametersViewModel in ProteaseSpecificParameters)
        {
            specificParametersViewModel.MaxMissedCleavages = MaxMissedCleavages;
            specificParametersViewModel.MinLength = MinLength;
            specificParametersViewModel.MaxLength = MaxLength;
            specificParametersViewModel.IsSelected = false;
        }
        
        ClearProteases();
    }


    #endregion

    public void PopulateProteaseCollection()
    {
        string proteaseDirectory = System.IO.Path.Combine(GlobalVariables.DataDir, @"ProteolyticDigestion");
        string proteaseFilePath = System.IO.Path.Combine(proteaseDirectory, @"proteases.tsv");
        Dictionary<string, Protease> dict = ProteaseDictionary.LoadProteaseDictionary(proteaseFilePath, GlobalVariables.ProteaseMods);

        foreach (var protease in dict)
        {
            ProteaseSpecificParametersViewModel? current = ProteaseSpecificParameters.FirstOrDefault(p => p.DigestionAgentName == protease.Value.Name);

            if (current == null)
            {
                var newDig = new DigestionParams(protease.Key, MaxMissedCleavages, MinLength, MaxLength);
                var newParams = new ProteaseSpecificParameters(newDig, null, null);
                var newParamsVM = new ProteaseSpecificParametersViewModel(newParams, this);
                ProteaseSpecificParameters.Add(newParamsVM);
            }

        }
    }
}
