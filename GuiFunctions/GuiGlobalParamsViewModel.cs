using Engine;
using Nett;
using Tasks;

namespace GuiFunctions;
public class GuiGlobalParamsViewModel : BaseViewModel
{
    private static GuiGlobalParamsViewModel _instance;
    private GlobalParameters _current;
    private GlobalParameters _loaded;

    public static GuiGlobalParamsViewModel Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new GuiGlobalParamsViewModel();
                _instance.Load();
            }
            return _instance;
        }
    }
    private GuiGlobalParamsViewModel() { }

    public string MainWindowTitle => IsRnaMode
        ? $"Rnase Guru: {GlobalVariables.ProteaseGuruVersion}"
        : $"Protease Guru: {GlobalVariables.ProteaseGuruVersion}";

    #region Protease / Rnase Mode

    public bool IsRnaMode
    {
        get => _current.IsRnaMode;
        set
        {
            _current.IsRnaMode = value;
            GlobalVariables.AnalyteType = _current.IsRnaMode ? AnalyteType.Oligo : AnalyteType.Peptide;

            OnPropertyChanged(nameof(IsRnaMode));
            OnPropertyChanged(nameof(MainWindowTitle));
        }
    }

    #endregion

    #region IO

    // Load from disk
    public void Load()
    {
        if (File.Exists(GlobalParameters.DefaultGlobalParametersFilePath))
        {
            try
            {
                _current = Toml.ReadFile<GlobalParameters>(GlobalParameters.DefaultGlobalParametersFilePath);
            }
            catch
            {
                _current = new GlobalParameters();
                GlobalParameters.ToToml(_current, GlobalParameters.DefaultGlobalParametersFilePath);
            }
        }
        else
        {
            _current = new GlobalParameters();
            GlobalParameters.ToToml(_current, GlobalParameters.DefaultGlobalParametersFilePath);
        }

        IsRnaMode = _current.IsRnaMode;
        _loaded = _current.Clone();
    }

    // Save to disk
    public void Save()
    {
        GlobalParameters.ToToml(_current, GlobalParameters.DefaultGlobalParametersFilePath);
        _loaded = _current.Clone();
    }

    public bool IsDirty() => !_current.Equals(_loaded);

    public static bool SettingsFileExists() => File.Exists(GlobalParameters.DefaultGlobalParametersFilePath);

    #endregion
}

