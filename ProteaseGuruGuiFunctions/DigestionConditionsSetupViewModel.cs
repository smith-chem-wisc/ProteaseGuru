using System.Collections.ObjectModel;
using System.Windows.Input;
using Engine;
using Omics.Modifications;
using Proteomics.AminoAcidPolymer;
using Proteomics.ProteolyticDigestion;
using Tasks;
using Transcriptomics.Digestion;

namespace ProteaseGuruGuiFunctions;

public class DigestionConditionsSetupViewModel : BaseViewModel
{
    public Modification OxidativeMethionine { get; init; }
    public Modification Carbamidomethylation { get; init; }
    public Modification CyclicPhosphate { get; init; }

    public ObservableCollection<ProteaseSpecificParametersViewModel> ProteaseSpecificParameters { get; } = new();

    private RunParameters _parameters;
    public RunParameters Parameters
    {
        get
        {
            _parameters.ProteaseSpecificParameters.Clear();
            foreach (var specificParams in ProteaseSpecificParameters)
                if (specificParams.IsSelected && specificParams.IsVisible)
                    _parameters.ProteaseSpecificParameters.Add(specificParams.ProteaseSpecificParams);
            return _parameters;
        }
    }

    public DigestionConditionsSetupViewModel(RunParameters? parameters)
    {
        _parameters = parameters ?? new RunParameters();
        OxidativeMethionine = GlobalVariables.AllModsKnown.First(p => p.IdWithMotif == "Oxidation on M");
        Carbamidomethylation = GlobalVariables.AllModsKnown.First(p => p.IdWithMotif == "Carbamidomethyl on C");
        CyclicPhosphate = Mods.GetModification("Cyclic Phosphate on X", false, true);

        PopulateProteaseCollection();

        SetDefaultProteasesCommand = new RelayCommand(SetDefaultProteases);
        ClearProteasesCommand = new RelayCommand(ClearProteases);
        ResetDigestionConditionsCommand = new RelayCommand(ResetDigestionConditions);


        // Initialize local fields with current parameter values
        LoadFromParameters();
    }

    /// <summary>
    /// Loads current values from the underlying RunParameters into local fields.
    /// Called when loading a new parameters file.
    /// </summary>
    public void LoadFromParameters(RunParameters? runParams = null)
    {
        if (runParams is not null)
            _parameters = runParams;

        _treatModifiedPeptidesAsDifferent = _parameters.TreatModifiedPeptidesAsDifferent;

        // Load values from first protease if any exist
        if (_parameters.ProteaseSpecificParameters.Any())
        {
            var firstProtease = _parameters.ProteaseSpecificParameters.First();
            _maxMissedCleavages = firstProtease.DigestionParams.MaxMissedCleavages;
            _minLength = firstProtease.DigestionParams.MinLength;
            _maxLength = firstProtease.DigestionParams.MaxLength;
        }

        foreach (var specificParams in ProteaseSpecificParameters)
        {
            var matchingParams = _parameters.ProteaseSpecificParameters.FirstOrDefault(p => p.DigestionParams.DigestionAgent.Name == specificParams.DigestionAgentName);
            if (matchingParams != null)
            {
                specificParams.MaxMissedCleavages = matchingParams.DigestionParams.MaxMissedCleavages;
                specificParams.MinLength = matchingParams.DigestionParams.MinLength;
                specificParams.MaxLength = matchingParams.DigestionParams.MaxLength;
                specificParams.IsSelected = true;
            }
            else
            {
                specificParams.MaxMissedCleavages = _maxMissedCleavages;
                specificParams.MinLength = _minLength;
                specificParams.MaxLength = _maxLength;
                specificParams.IsSelected = false;
            }
        }

        // Refresh UI
        OnPropertyChanged(nameof(MaxMissedCleavages));
        OnPropertyChanged(nameof(MinLength));
        OnPropertyChanged(nameof(MaxLength));
        OnPropertyChanged(nameof(MinPeptideMass));
        OnPropertyChanged(nameof(MaxPeptideMass));
        OnPropertyChanged(nameof(DetectabilityThreshold));
        OnPropertyChanged(nameof(TreatModifiedPeptidesAsDifferent));
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
            foreach (var proteaseSpecific in ProteaseSpecificParameters.Where(p => p.IsVisible))
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
            foreach (var proteaseSpecific in ProteaseSpecificParameters.Where(p => p.IsVisible))
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
            foreach (var proteaseSpecific in ProteaseSpecificParameters.Where(p => p.IsVisible))
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

    public double DetectabilityThreshold
    {
        get => Parameters.DetectabilityThreshold;
        set
        {
            Parameters.DetectabilityThreshold = value;
            OnPropertyChanged(nameof(DetectabilityThreshold));
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
            foreach (var specificParams in ProteaseSpecificParameters.Where(p => p is { IsRna: false, IsVisible: true } && !p.ProteaseSpecificParams.FixedMods.Contains(Carbamidomethylation)))
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
            if (_applyVariableOxidation == value) return;

            _applyVariableOxidation = value;
            var variableMod = GuiGlobalParamsViewModel.Instance.IsRnaMode ? CyclicPhosphate : OxidativeMethionine;

            if (_applyVariableOxidation)
            {
                foreach (var specificParams in ProteaseSpecificParameters.Where(p => p.IsVisible && !p.ProteaseSpecificParams.VariableMods.Contains(variableMod)))
                {
                    specificParams.ProteaseSpecificParams.VariableMods.Add(variableMod);
                }
            }
            else
            {
                foreach (var specificParams in ProteaseSpecificParameters.Where(p => p.IsVisible && p.ProteaseSpecificParams.VariableMods.Contains(variableMod)))
                {
                    specificParams.ProteaseSpecificParams.VariableMods.Remove(variableMod);
                }
            }

            OnPropertyChanged(nameof(ApplyVariableOxidation));
        }
    }

    #endregion

    #region Commands

    public ICommand SetDefaultProteasesCommand { get; }
    public ICommand ClearProteasesCommand { get; }
    public ICommand ResetDigestionConditionsCommand { get; }

    private string[] _defaultProteases = ["trypsin|P", "Lys-C|P", "Asp-N", "Glu-C", "chymotrypsin|P", "Arg-C"];
    private string[] _defaultRnases = ["RNase T1", "RNase_MC1", "Cusativin"];

    private void SetDefaultProteases()
    {
        if (GuiGlobalParamsViewModel.Instance.IsRnaMode)
        {
            foreach (var specificParametersViewModel in ProteaseSpecificParameters.Where(p => p.IsVisible))
            {
                specificParametersViewModel.IsSelected = _defaultRnases.Contains(specificParametersViewModel.DigestionAgentName);
            }
        }
        else
        {
            // Select the 6 most commonly used proteases (indices 0, 1, 2, 6, 7, 10 from old code)
            foreach (var specificParametersViewModel in ProteaseSpecificParameters.Where(p => p.IsVisible))
            {
                specificParametersViewModel.IsSelected = _defaultProteases.Contains(specificParametersViewModel.DigestionAgentName);
            }
        }
    }

    private void ClearProteases()
    {
        // Deselect all proteases
        foreach (var protease in ProteaseSpecificParameters.Where(p => p.IsVisible))
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
        DetectabilityThreshold = 0.5;
        TreatModifiedPeptidesAsDifferent = false;
        ApplyFixedCarbamidomethylation = false;
        ApplyVariableOxidation = false;

        foreach (var specificParametersViewModel in ProteaseSpecificParameters.Where(p => p.IsVisible))
        {
            specificParametersViewModel.MaxMissedCleavages = MaxMissedCleavages;
            specificParametersViewModel.MinLength = MinLength;
            specificParametersViewModel.MaxLength = MaxLength;
            specificParametersViewModel.IsSelected = false;
        }

        ClearProteases();
    }


    #endregion

    // Proteases ProteaseGuru exposes in its UI — exactly the entries from mzLib's
    // embedded ProteaseDictionary that represent real digestive enzymes.
    // Utility/test entries (non-specific, top-down, singleN, singleC, peptidomics,
    // tryptophan oxidation, CNBr_old, CNBr_N, StcE-trypsin, ProAlanase, elastase|P)
    // are excluded.
    private static readonly HashSet<string> _allowedProteases = new(StringComparer.Ordinal)
    {
        "Arg-C",
        "Asp-N",
        "chymotrypsin|P",
        "CNBr",
        "Glu-C",
        "Glu-C (with asp)",
        "Lys-C|P",
        "Lys-N",
        "trypsin",
        "trypsin|P",
        "collagenase",
    };

    public void PopulateProteaseCollection()
    {
        var dict = ProteaseDictionary.Dictionary;
        // Show the curated mzLib proteases plus any proteases the user has added at runtime.
        foreach (var protease in dict.Where(kvp =>
            _allowedProteases.Contains(kvp.Key) ||
            GlobalVariables.UserAddedProteaseNames.Contains(kvp.Key)))
        {
            ProteaseSpecificParametersViewModel? current = ProteaseSpecificParameters.FirstOrDefault(p => p.DigestionAgentName == protease.Value.Name);
            bool shouldSelect = _parameters.ProteaseSpecificParameters.Any(p => p.DigestionParams.DigestionAgent.Name == protease.Value.Name);

            if (current == null)
            {
                var newDig = new DigestionParams(protease.Key, MaxMissedCleavages, MinLength, MaxLength);
                var newParams = new ProteaseSpecificParameters(newDig, null, null);
                var newParamsVM = new ProteaseSpecificParametersViewModel(newParams, this)
                {
                    IsSelected = shouldSelect
                };
                ProteaseSpecificParameters.Add(newParamsVM);
            }
            else
            {
                current.IsSelected = shouldSelect;
            }
        }

        foreach (var rnase in RnaseDictionary.Dictionary)
        {
            ProteaseSpecificParametersViewModel? current = ProteaseSpecificParameters.FirstOrDefault(p => p.DigestionAgentName == rnase.Value.Name);
            bool shouldSelect = _parameters.ProteaseSpecificParameters.Any(p => p.DigestionParams.DigestionAgent.Name == rnase.Value.Name);

            if (current == null)
            {
                var newDig = new RnaDigestionParams(rnase.Key, MaxMissedCleavages, MinLength, MaxLength);
                var newParams = new ProteaseSpecificParameters(newDig, null, null);
                var newParamsVM = new ProteaseSpecificParametersViewModel(newParams, this)
                {
                    IsSelected = shouldSelect
                };
                ProteaseSpecificParameters.Add(newParamsVM);
            }
            else
            {
                current.IsSelected = shouldSelect;
            }
        }
    }
}
