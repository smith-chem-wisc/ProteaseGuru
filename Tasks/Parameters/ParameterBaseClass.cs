using Nett;
using Omics.Digestion;
using Omics.Modifications;
using Proteomics.ProteolyticDigestion;
using Transcriptomics.Digestion;

namespace Tasks;

public abstract class ParameterBaseClass<TParameters> where TParameters : ParameterBaseClass<TParameters>
{
    public static void ToToml(TParameters parameters, string filePath)
    {
        Toml.WriteFile(parameters, filePath, TomlConfig);
    }

    public static TParameters FromToml(string filePath) 
    {
        TParameters parametersFromFile = Toml.ReadFile<TParameters>(filePath, TomlConfig);

        return parametersFromFile;
    }

    public static readonly TomlSettings TomlConfig = TomlSettings.Create(cfg => cfg
        .ConfigureType<IDigestionParams>(type => type
            .WithConversionFor<TomlTable>(c => c
                .FromToml(tmlTable =>
                    tmlTable.ContainsKey("Protease")
                        ? tmlTable.Get<DigestionParams>()
                        : tmlTable.Get<RnaDigestionParams>())))
        .ConfigureType<DigestionParams>(type => type
            .IgnoreProperty(p => p.DigestionAgent)
            .IgnoreProperty(p => p.MaxMods)
            .IgnoreProperty(p => p.MaxLength)
            .IgnoreProperty(p => p.MinLength))
        .ConfigureType<RnaDigestionParams>(type => type
            .IgnoreProperty(p => p.DigestionAgent))
        .ConfigureType<Rnase>(type => type
            .WithConversionFor<TomlString>(convert => convert
                .ToToml(custom => custom.Name)
                .FromToml(tmlString => RnaseDictionary.Dictionary[tmlString.Value])))
        .ConfigureType<Protease>(type => type
            .WithConversionFor<TomlString>(convert => convert
                .ToToml(custom => custom.ToString())
                .FromToml(tmlString => ProteaseDictionary.Dictionary[tmlString.Value])))
        .ConfigureType<Modification>(type => type
            .WithConversionFor<TomlString>(convert => convert
                .ToToml(mod => $"{mod.ModificationType}:{mod.IdWithMotif}")
                .FromToml(tmlString => Mods.AllModsKnownDictionary.TryGetValue(tmlString.Value, out var mod) ? mod : null)))
        .ConfigureType<List<Modification>>(type => type
            .WithConversionFor<TomlString>(convert => convert
                .ToToml(modList => string.Join(",", modList.Select(mod => $"{mod.IdWithMotif}")))
                .FromToml(tmlString =>
                    tmlString.Value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(modStr => Mods.GetModification(modStr))
                        .Where(mod => mod != null).Cast<Modification>()
                        .ToList())))
        .ConfigureType<List<ProteaseSpecificParameters>>(type => type
            .WithConversionFor<TomlTableArray>(convert => convert
                .FromToml(tmlTableArr => tmlTableArr.Items.Select(table => table.Get<ProteaseSpecificParameters>()).ToList())))
        .ConfigureType<ProteaseSpecificParameters>(type => type
            .IgnoreProperty(p => p.DigestionAgentName))
        );
}
